using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace Conflux.Kafka;

/// <summary>Provides W3C trace-context propagation for Kafka messages.</summary>
public static class KafkaTrace
{
    /// <summary>Gets the ActivitySource name used for Kafka spans.</summary>
    public const string ActivitySourceName = "Conflux.Kafka";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>Injects the current W3C trace context into Kafka headers.</summary>
    public static void Inject(Headers headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        headers.Remove("traceparent");
        headers.Add("traceparent", Encoding.UTF8.GetBytes(activity.Id ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
        {
            headers.Remove("tracestate");
            headers.Add("tracestate", Encoding.UTF8.GetBytes(activity.TraceStateString));
        }
    }

    /// <summary>Starts a consumer Activity using the W3C parent carried by Kafka headers.</summary>
    public static Activity? StartConsumerActivity(ConsumeResult<string, string> result, string operation)
    {
        var traceparent = Header(result.Message.Headers, "traceparent");
        var tracestate = Header(result.Message.Headers, "tracestate");
        var parent = ActivityContext.TryParse(traceparent, tracestate, out var context)
            ? context
            : default;
        return Source.StartActivity(operation, ActivityKind.Consumer, parent);
    }

    private static string? Header(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (string.Equals(headers[index].Key, name, StringComparison.Ordinal))
            {
                var bytes = headers[index].GetValueBytes();
                return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
            }
        }
        return null;
    }
}
