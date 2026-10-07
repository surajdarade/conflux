namespace Conflux.Order.OutboxPublisher.Configuration;

/// <summary>
/// Provides configuration values required by the Order Outbox Publisher.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Kafka";

    /// <summary>
    /// Gets or sets the Kafka bootstrap server addresses.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Gets or sets the Kafka topic used for Order integration events.
    /// </summary>
    public string OrderEventsTopic { get; set; } =
        "conflux.order.events";

    /// <summary>
    /// Gets or sets the maximum number of Outbox messages claimed per batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the delay between polling attempts when no messages exist.
    /// </summary>
    public TimeSpan PollInterval { get; set; } =
        TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the duration for which a claimed message remains leased.
    /// </summary>
    public TimeSpan ClaimLeaseDuration { get; set; } =
        TimeSpan.FromSeconds(30);
}