using Xunit;

namespace Conflux.Payment.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared test collection for Payment integration tests.
/// </summary>
[CollectionDefinition("Payment integration tests")]
public sealed class PaymentTestCollection :
    ICollectionFixture<PaymentTestFixture>
{
}