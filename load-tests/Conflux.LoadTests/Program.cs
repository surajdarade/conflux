using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

var options = LoadOptions.Parse(args);
using var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = Math.Max(options.Concurrency * 2, 256),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    AutomaticDecompression = DecompressionMethods.All
};
using var client = new HttpClient(handler)
{
    BaseAddress = new Uri(options.BaseUrl),
    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
};

if (options.WarmupRequests > 0)
{
    for (var i = 0; i < options.WarmupRequests; i++)
    {
        using var warmup = CreateRequest(options, i);
        using var response = await client.SendAsync(warmup, HttpCompletionOption.ResponseHeadersRead);
    }
}

var latencies = new long[options.Requests];
var statusCodes = new int[options.Requests];
var failures = 0;
var nextIndex = -1;
var started = Stopwatch.GetTimestamp();
var workers = Enumerable.Range(0, options.Concurrency).Select(_ => Task.Run(async () =>
{
    while (true)
    {
        var index = Interlocked.Increment(ref nextIndex);
        if (index >= options.Requests)
        {
            return;
        }

        try
        {
            using var request = CreateRequest(options, index);
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();
            latencies[index] = stopwatch.ElapsedTicks;
            statusCodes[index] = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref failures);
            }
        }
        catch
        {
            Interlocked.Increment(ref failures);
        }
    }
})).ToArray();

await Task.WhenAll(workers);
var elapsed = Stopwatch.GetElapsedTime(started);
var ordered = latencies.Where(value => value > 0).OrderBy(value => value).ToArray();
var successful = ordered.Length;
var statusDistribution = statusCodes
    .Where(code => code != 0)
    .GroupBy(code => code)
    .OrderBy(group => group.Key)
    .ToDictionary(group => group.Key.ToString(), group => group.Count());

var summary = new
{
    options.BaseUrl,
    options.Path,
    options.Method,
    options.Requests,
    options.Concurrency,
    options.WarmupRequests,
    options.TimeoutSeconds,
    SuccessfulRequests = successful,
    FailedRequests = failures,
    ErrorRate = options.Requests == 0 ? 0d : (double)failures / options.Requests,
    DurationSeconds = elapsed.TotalSeconds,
    ThroughputRequestsPerSecond = successful / Math.Max(elapsed.TotalSeconds, double.Epsilon),
    P50Milliseconds = Percentile(ordered, 0.50),
    P95Milliseconds = Percentile(ordered, 0.95),
    P99Milliseconds = Percentile(ordered, 0.99),
    MaxMilliseconds = ordered.Length == 0 ? double.NaN : ordered[^1] * 1_000d / Stopwatch.Frequency,
    StatusCodes = statusDistribution
};

Directory.CreateDirectory(options.OutputDirectory);
var outputPath = Path.Combine(options.OutputDirectory, options.OutputFileName);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Results: {Path.GetFullPath(outputPath)}");

static HttpRequestMessage CreateRequest(LoadOptions options, int index)
{
    var path = options.Path.Replace("{index}", index.ToString(), StringComparison.Ordinal);
    var request = new HttpRequestMessage(new HttpMethod(options.Method), path);
    if (!string.IsNullOrWhiteSpace(options.Body))
    {
        request.Content = new StringContent(
            options.Body.Replace("{index}", index.ToString(), StringComparison.Ordinal),
            Encoding.UTF8,
            "application/json");
    }

    if (!string.IsNullOrWhiteSpace(options.IdempotencyKeyTemplate))
    {
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            options.IdempotencyKeyTemplate.Replace("{index}", index.ToString(), StringComparison.Ordinal));
    }

    if (!string.IsNullOrWhiteSpace(options.Authorization))
    {
        request.Headers.TryAddWithoutValidation("Authorization", options.Authorization);
    }

    return request;
}

static double Percentile(long[] ticks, double percentile)
{
    if (ticks.Length == 0)
    {
        return double.NaN;
    }

    var rank = percentile * (ticks.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    var value = lower == upper
        ? ticks[lower]
        : ticks[lower] + (ticks[upper] - ticks[lower]) * (rank - lower);
    return value * 1_000d / Stopwatch.Frequency;
}

file sealed record LoadOptions(
    string BaseUrl,
    string Path,
    string Method,
    string Body,
    string IdempotencyKeyTemplate,
    string Authorization,
    int Requests,
    int Concurrency,
    int TimeoutSeconds,
    int WarmupRequests,
    string OutputDirectory,
    string OutputFileName)
{
    public static LoadOptions Parse(string[] args)
    {
        string Get(string name, string fallback) =>
            args.FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?
                .Split('=', 2)[1] ?? fallback;

        var requests = int.Parse(Get("--requests", "10000"));
        var concurrency = int.Parse(Get("--concurrency", "64"));
        if (requests < 1 || concurrency < 1)
        {
            throw new ArgumentOutOfRangeException("requests/concurrency", "Requests and concurrency must be positive.");
        }

        return new LoadOptions(
            Get("--base-url", "http://localhost:5096"),
            Get("--path", "/health"),
            Get("--method", "GET").ToUpperInvariant(),
            Get("--body", string.Empty),
            Get("--idempotency-key", string.Empty),
            Get("--authorization", string.Empty),
            requests,
            concurrency,
            int.Parse(Get("--timeout-seconds", "30")),
            int.Parse(Get("--warmup", "100")),
            Get("--output-directory", "artifacts/load-tests"),
            Get("--output", "run.json"));
    }
}
