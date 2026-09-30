using Xunit;

namespace Conflux.Order.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared test collection used by Order integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class OrderTestCollection :
    ICollectionFixture<OrderTestFixture>
{
    /// <summary>
    /// Gets the name of the Order integration-test collection.
    /// </summary>
    public const string Name = "Order Integration Tests";
}