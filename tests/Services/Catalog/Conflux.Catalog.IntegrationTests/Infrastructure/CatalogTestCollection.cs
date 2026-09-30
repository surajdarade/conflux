using Xunit;

namespace Conflux.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared test collection used by Catalog integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CatalogTestCollection :
    ICollectionFixture<CatalogTestFixture>
{
    /// <summary>
    /// Gets the name of the Catalog integration-test collection.
    /// </summary>
    public const string Name = "Catalog Integration Tests";
}