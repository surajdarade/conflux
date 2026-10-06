using Xunit;

namespace Conflux.Payment.OutboxPublisher.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared collection for Outbox Publisher integration tests.
/// </summary>
[CollectionDefinition("Payment Outbox Publisher integration tests")]
public sealed class OutboxPublisherTestCollection :
    ICollectionFixture<OutboxPublisherTestFixture>
{
}