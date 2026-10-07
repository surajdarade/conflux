namespace Conflux.Kafka;

/// <summary>Configures Kafka retry and dead-letter processing.</summary>
public sealed class KafkaRetryOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "KafkaRetry";

    /// <summary>Gets or sets Kafka bootstrap servers.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Gets or sets source topics whose failed messages are retried.</summary>
    public List<string> SourceTopics { get; set; } = [];

    /// <summary>Gets or sets the consumer group suffix for retry processing.</summary>
    public string ConsumerGroup { get; set; } = "conflux-kafka-retry-v1";

    /// <summary>Gets or sets the number of attempts before a message enters the DLQ.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the base delay for exponential retry backoff.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the polling delay for retry workers.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets whether automatic DLQ recovery is enabled.</summary>
    public bool AutomaticDlqRecovery { get; set; }

    /// <summary>Gets or sets the delay before an automatic DLQ recovery attempt.</summary>
    public TimeSpan DlqRecoveryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the retry topic for an attempt.</summary>
    public static string GetRetryTopic(string sourceTopic, int attempt) =>
        $"{sourceTopic}.retry.{attempt}";

    /// <summary>Gets the dead-letter topic for a source topic.</summary>
    public static string GetDlqTopic(string sourceTopic) =>
        $"{sourceTopic}.dlq";
}
