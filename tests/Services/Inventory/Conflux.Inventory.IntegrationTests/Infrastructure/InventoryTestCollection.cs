using Xunit;

namespace Conflux.Inventory.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared test collection used by Inventory integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InventoryTestCollection :
    ICollectionFixture<InventoryTestFixture>
{
    /// <summary>
    /// Gets the name of the Inventory integration-test collection.
    /// </summary>
    public const string Name = "Inventory Integration Tests";
}