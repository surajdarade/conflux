using Xunit;
using System.Text.Json;
using Conflux.Contracts.Events;
using Conflux.Kafka;
using FluentAssertions;

namespace Conflux.ContractTests;

/// <summary>Verifies versioned Kafka event contracts remain serializable and compatible.</summary>
public sealed class EventContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Ensures all registered v1 event contracts round-trip through JSON.</summary>
    [Fact]
    public void AllV1Events_RoundTripWithoutDataLoss()
    {
        var fixtures = new object[]
        {
            new ProductCreated
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Sku = "SKU-1",
                Name = "Test", Description = "Test", Price = 12.34m, Currency = "INR",
                IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new InventoryReserved
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), ReservationId = Guid.NewGuid(), Sku = "SKU-1", Quantity = 2
            },
            new InventoryReservationReleased
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), ReservationId = Guid.NewGuid(), Sku = "SKU-1", Quantity = 2
            },
            new OrderInventoryReserved
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), OrderId = Guid.NewGuid(), CustomerId = Guid.NewGuid(),
                TotalAmount = 42m, Currency = "INR"
            },
            new OrderConfirmed
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), OrderId = Guid.NewGuid(), CustomerId = Guid.NewGuid(),
                PaymentId = Guid.NewGuid(), TotalAmount = 42m, Currency = "INR"
            },
            new PaymentAuthorized
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(), Amount = 42m, Currency = "INR"
            },
            new PaymentCaptured
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(), Amount = 42m, Currency = "INR"
            },
            new PaymentVoided
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid()
            },
            new PaymentRefunded
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(), Amount = 42m, Currency = "INR"
            }
        };

        foreach (var fixture in fixtures)
        {
            var json = JsonSerializer.Serialize(fixture, fixture.GetType(), JsonOptions);
            json.Should().NotBeNullOrWhiteSpace();
            var roundTrip = JsonSerializer.Deserialize(json, fixture.GetType(), JsonOptions);
            roundTrip.Should().NotBeNull();
            JsonSerializer.Serialize(roundTrip, fixture.GetType(), JsonOptions)
                .Should().Be(json);
        }
    }

    /// <summary>Ensures every stable v1 Kafka event identifier resolves to the intended CLR contract.</summary>
    [Fact]
    public void V1Registry_ResolvesAllStableEventTypes()
    {
        EventContractRegistry.V1.Should().HaveCount(9);
        EventContractRegistry.Resolve("payment.captured.v1").Should().Be(typeof(PaymentCaptured));
        EventContractRegistry.Resolve("payment.refunded.v1").Should().Be(typeof(PaymentRefunded));
        EventContractRegistry.Resolve("unknown.v1").Should().BeNull();
    }

    /// <summary>Ensures retry and DLQ topic names are deterministic and version-independent.</summary>
    [Fact]
    public void RetryTopology_UsesStableTopicNames()
    {
        KafkaRetryOptions.GetRetryTopic("conflux.order.events", 1).Should().Be("conflux.order.events.retry.1");
        KafkaRetryOptions.GetRetryTopic("conflux.order.events", 2).Should().Be("conflux.order.events.retry.2");
        KafkaRetryOptions.GetDlqTopic("conflux.order.events").Should().Be("conflux.order.events.dlq");
    }

    /// <summary>Ensures additive unknown fields remain forward-compatible for every contract.</summary>
    [Fact]
    public void V1Events_IgnoreUnknownFields()
    {
        var json = "{\"eventId\":\"00000000-0000-0000-0000-000000000001\",\"occurredAt\":\"2026-01-01T00:00:00Z\",\"correlationId\":\"00000000-0000-0000-0000-000000000002\",\"reservationId\":\"00000000-0000-0000-0000-000000000003\",\"sku\":\"SKU-1\",\"quantity\":1,\"futureField\":\"ignored\"}";
        var value = JsonSerializer.Deserialize<InventoryReserved>(json, JsonOptions);
        value.Should().NotBeNull();
        value!.Quantity.Should().Be(1);
    }
}
