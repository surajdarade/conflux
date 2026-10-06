namespace Conflux.Payment.OutboxPublisher.Configuration;

/// <summary>
/// Provides configuration values required by the Payment Outbox Publisher.
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
    public string BootstrapServers { get; set; } =
        "localhost:9092";

    /// <summary>
    /// Gets or sets the Kafka topic used for Payment integration events.
    /// </summary>
    public string PaymentEventsTopic { get; set; } =
        "conflux.payment.events";

    /// <summary>
    /// Gets or sets the number of Outbox messages processed in one batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the delay between polling attempts when no messages are available.
    /// </summary>
    public TimeSpan PollInterval { get; set; } =
        TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the duration for which an Outbox claim remains valid.
    /// </summary>
    public TimeSpan ClaimLeaseDuration { get; set; } =
        TimeSpan.FromSeconds(30);
}