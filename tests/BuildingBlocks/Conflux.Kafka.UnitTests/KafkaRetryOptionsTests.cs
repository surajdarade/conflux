using Conflux.Kafka;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests Kafka retry configuration defaults and topic naming.
/// </summary>
public sealed class KafkaRetryOptionsTests
{
    /// <summary>Uses the configured default maximum attempt count.</summary>
    [Fact]
    public void Defaults_MaxAttempts_IsThree()
    {
        var options = new KafkaRetryOptions();

        Assert.Equal(3, options.MaxAttempts);
    }

    /// <summary>Uses the expected default base retry delay.</summary>
    [Fact]
    public void Defaults_BaseDelay_IsTwoSeconds()
    {
        var options = new KafkaRetryOptions();

        Assert.Equal(TimeSpan.FromSeconds(2), options.BaseDelay);
    }

    /// <summary>Uses the expected default DLQ recovery delay.</summary>
    [Fact]
    public void Defaults_DlqRecoveryDelay_IsFiveMinutes()
    {
        var options = new KafkaRetryOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.DlqRecoveryDelay);
    }

    /// <summary>Builds isolated retry topics for different logical consumers.</summary>
    [Fact]
    public void GetRetryTopic_WithRetryIdentity_IsolatesConsumers()
    {
        var orderTopic = KafkaRetryOptions.GetRetryTopic(
            "conflux.order.events",
            "order-checkout-saga",
            1);

        var fulfillmentTopic = KafkaRetryOptions.GetRetryTopic(
            "conflux.order.events",
            "fulfillment-order-confirmed",
            1);

        Assert.Equal(
            "conflux.order.events.retry.order-checkout-saga.1",
            orderTopic);

        Assert.Equal(
            "conflux.order.events.retry.fulfillment-order-confirmed.1",
            fulfillmentTopic);

        Assert.NotEqual(orderTopic, fulfillmentTopic);
    }

    /// <summary>Builds isolated DLQ topics for different logical consumers.</summary>
    [Fact]
    public void GetDlqTopic_WithRetryIdentity_IsolatesConsumers()
    {
        var orderTopic = KafkaRetryOptions.GetDlqTopic(
            "conflux.order.events",
            "order-checkout-saga");

        var fulfillmentTopic = KafkaRetryOptions.GetDlqTopic(
            "conflux.order.events",
            "fulfillment-order-confirmed");

        Assert.Equal(
            "conflux.order.events.dlq.order-checkout-saga",
            orderTopic);

        Assert.Equal(
            "conflux.order.events.dlq.fulfillment-order-confirmed",
            fulfillmentTopic);
    }

    /// <summary>Rejects invalid retry identities.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("order saga")]
    [InlineData("order.saga")]
    [InlineData("order/saga")]
    public void GetRetryTopic_WithInvalidIdentity_Throws(
        string retryIdentity)
    {
        Assert.Throws<ArgumentException>(() =>
            KafkaRetryOptions.GetRetryTopic(
                "conflux.order.events",
                retryIdentity,
                1));
    }

    /// <summary>Rejects retry attempts below one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetRetryTopic_WithInvalidAttempt_Throws(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KafkaRetryOptions.GetRetryTopic(
                "conflux.order.events",
                "order-checkout-saga",
                attempt));
    }

    /// <summary>Builds the retry topic name for the requested attempt.</summary>
    [Theory]
    [InlineData("conflux.catalog.events", 1, "conflux.catalog.events.retry.1")]
    [InlineData("conflux.catalog.events", 2, "conflux.catalog.events.retry.2")]
    [InlineData("conflux.inventory.events", 3, "conflux.inventory.events.retry.3")]
    public void GetRetryTopic_ReturnsExpectedTopic(
        string sourceTopic,
        int attempt,
        string expected)
    {
        Assert.Equal(expected, KafkaRetryOptions.GetRetryTopic(sourceTopic, attempt));
    }

    /// <summary>Builds the dead-letter topic name for the source topic.</summary>
    [Theory]
    [InlineData("conflux.catalog.events", "conflux.catalog.events.dlq")]
    [InlineData("conflux.inventory.events", "conflux.inventory.events.dlq")]
    public void GetDlqTopic_ReturnsExpectedTopic(string sourceTopic, string expected)
    {
        Assert.Equal(expected, KafkaRetryOptions.GetDlqTopic(sourceTopic));
    }
}
