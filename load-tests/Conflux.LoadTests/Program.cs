using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
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

        try
        {
            using var response = await client.SendAsync(
                warmup,
                HttpCompletionOption.ResponseHeadersRead);
        }
        catch
        {
            // Warmup failures do not affect measured results.
        }
    }
}

var latencies = new long[options.Requests];
var statusCodes = new int[options.Requests];

var failedRequests = 0;
var exceptionRequests = 0;
var nextIndex = -1;

var started = Stopwatch.GetTimestamp();

var workers = Enumerable
    .Range(0, options.Concurrency)
    .Select(_ => Task.Run(async () =>
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

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead);

                stopwatch.Stop();

                latencies[index] = stopwatch.ElapsedTicks;
                statusCodes[index] = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody =
                        await response.Content.ReadAsStringAsync();

                    Console.Error.WriteLine(
                        $"Request {index} failed: " +
                        $"{(int)response.StatusCode} " +
                        $"{response.StatusCode} - " +
                        $"{responseBody}");

                    Interlocked.Increment(ref failedRequests);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failedRequests);
                Interlocked.Increment(ref exceptionRequests);

                Console.Error.WriteLine(
                    $"Request {index} threw " +
                    $"{exception.GetType().Name}: " +
                    $"{exception.Message}");
            }
        }
    }))
    .ToArray();

await Task.WhenAll(workers);

var elapsed = Stopwatch.GetElapsedTime(started);

var completedLatencies = latencies
    .Where(value => value > 0)
    .OrderBy(value => value)
    .ToArray();

var completedRequests = completedLatencies.Length;

var successfulRequests = statusCodes.Count(
    code => code >= 200 && code <= 299);

var statusDistribution = statusCodes
    .Where(code => code != 0)
    .GroupBy(code => code)
    .OrderBy(group => group.Key)
    .ToDictionary(
        group => group.Key.ToString(),
        group => group.Count());

var summary = new
{
    options.BaseUrl,
    options.Path,
    options.Method,
    options.Requests,
    options.Concurrency,
    options.WarmupRequests,
    options.TimeoutSeconds,

    CompletedRequests = completedRequests,
    SuccessfulRequests = successfulRequests,
    FailedRequests = failedRequests,
    ExceptionRequests = exceptionRequests,

    ErrorRate = options.Requests == 0
        ? 0d
        : (double)failedRequests / options.Requests,

    DurationSeconds = elapsed.TotalSeconds,

    ThroughputRequestsPerSecond =
        completedRequests / Math.Max(
            elapsed.TotalSeconds,
            double.Epsilon),

    P50Milliseconds = Percentile(
        completedLatencies,
        0.50),

    P95Milliseconds = Percentile(
        completedLatencies,
        0.95),

    P99Milliseconds = Percentile(
        completedLatencies,
        0.99),

    MaxMilliseconds = completedLatencies.Length == 0
        ? 0d
        : completedLatencies[^1]
            * 1_000d
            / Stopwatch.Frequency,

    StatusCodes = statusDistribution
};

Directory.CreateDirectory(options.OutputDirectory);

var outputPath = Path.Combine(
    options.OutputDirectory,
    options.OutputFileName);

var json = JsonSerializer.Serialize(
    summary,
    new JsonSerializerOptions
    {
        WriteIndented = true
    });

await File.WriteAllTextAsync(
    outputPath,
    json);

Console.WriteLine(json);
Console.WriteLine($"Results: {Path.GetFullPath(outputPath)}");


static HttpRequestMessage CreateRequest(
    LoadOptions options,
    int index)
{
    var path = options.ReplaceTokens(
        options.Path,
        index);

    var request = new HttpRequestMessage(
        new HttpMethod(options.Method),
        path);

    var body = options.GetBody(index);

    if (!string.IsNullOrEmpty(body))
    {
        var content = new ByteArrayContent(
            Encoding.UTF8.GetBytes(body));

        content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");

        request.Content = content;
    }

    if (!string.IsNullOrWhiteSpace(
        options.IdempotencyKeyTemplate))
    {
        var idempotencyKey = options.ReplaceTokens(
            options.IdempotencyKeyTemplate,
            index);

        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey);
    }

    if (!string.IsNullOrWhiteSpace(
        options.Authorization))
    {
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            options.Authorization);
    }

    return request;
}


static double Percentile(
    long[] ticks,
    double percentile)
{
    if (ticks.Length == 0)
    {
        return 0d;
    }

    var rank = percentile * (ticks.Length - 1);

    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);

    var value = lower == upper
        ? ticks[lower]
        : ticks[lower]
            + (ticks[upper] - ticks[lower])
            * (rank - lower);

    return value
        * 1_000d
        / Stopwatch.Frequency;
}


file sealed record LoadOptions(
    string BaseUrl,
    string Path,
    string Method,
    string Body,
    string BodyBase64,
    string IdempotencyKeyTemplate,
    string Authorization,
    int Requests,
    int Concurrency,
    int TimeoutSeconds,
    int WarmupRequests,
    string OutputDirectory,
    string OutputFileName,
    string[] InventoryIds)
{
    public string GetBody(int index)
    {
        if (!string.IsNullOrWhiteSpace(BodyBase64))
        {
            var decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(BodyBase64));

            return ReplaceTokens(
                decoded,
                index);
        }

        return ReplaceTokens(
            Body,
            index);
    }

    public string ReplaceTokens(
        string template,
        int index)
    {
        var result = template
            .Replace(
                "{index}",
                index.ToString(),
                StringComparison.Ordinal)
            .Replace(
                "{guid}",
                Guid.NewGuid().ToString(),
                StringComparison.Ordinal);

        if (result.Contains(
            "{inventoryId}",
            StringComparison.Ordinal))
        {
            if (InventoryIds.Length == 0)
            {
                throw new InvalidOperationException(
                    "The request contains {inventoryId}, " +
                    "but no inventory IDs were provided. " +
                    "Specify --inventory-ids-file.");
            }

            var inventoryId =
                InventoryIds[index % InventoryIds.Length];

            result = result.Replace(
                "{inventoryId}",
                inventoryId,
                StringComparison.Ordinal);
        }

        return result;
    }

    public static LoadOptions Parse(
        string[] args)
    {
        string Get(
            string name,
            string fallback)
        {
            return args
                .FirstOrDefault(
                    argument => argument.StartsWith(
                        name + "=",
                        StringComparison.OrdinalIgnoreCase))
                ?.Split(
                    '=',
                    2)[1]
                ?? fallback;
        }

        var requests = int.Parse(
            Get(
                "--requests",
                "10000"));

        var concurrency = int.Parse(
            Get(
                "--concurrency",
                "64"));

        if (requests < 1 ||
            concurrency < 1)
        {
            throw new ArgumentOutOfRangeException(
                "requests/concurrency",
                "Requests and concurrency must be positive.");
        }

        var body = Get(
            "--body",
            string.Empty);

        var bodyBase64 = Get(
            "--body-base64",
            string.Empty);

        if (!string.IsNullOrWhiteSpace(body) &&
            !string.IsNullOrWhiteSpace(bodyBase64))
        {
            throw new ArgumentException(
                "Specify either --body or --body-base64, not both.");
        }

        var inventoryIdsFile = Get(
            "--inventory-ids-file",
            string.Empty);

        var inventoryIds = Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(inventoryIdsFile))
        {
            if (!File.Exists(inventoryIdsFile))
            {
                throw new FileNotFoundException(
                    "Inventory IDs file was not found.",
                    inventoryIdsFile);
            }

            inventoryIds = File.ReadAllLines(
                    inventoryIdsFile)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (inventoryIds.Length == 0)
            {
                throw new ArgumentException(
                    $"Inventory IDs file '{inventoryIdsFile}' " +
                    "does not contain any inventory IDs.");
            }
        }

        return new LoadOptions(
            Get(
                "--base-url",
                "http://localhost:5096"),

            Get(
                "--path",
                "/health"),

            Get(
                "--method",
                "GET")
                .ToUpperInvariant(),

            body,

            bodyBase64,

            Get(
                "--idempotency-key",
                string.Empty),

            Get(
                "--authorization",
                string.Empty),

            requests,

            concurrency,

            int.Parse(
                Get(
                    "--timeout-seconds",
                    "30")),

            int.Parse(
                Get(
                    "--warmup",
                    "100")),

            Get(
                "--output-directory",
                "artifacts/load-tests"),

            Get(
                "--output",
                "run.json"),

            inventoryIds);
    }
}