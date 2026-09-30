using Xunit;

namespace Conflux.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Defines the shared test collection used by Identity integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IdentityTestCollection : ICollectionFixture<IdentityTestFixture> {
    /// <summary>
    /// Gets the name of the Identity integration-test collection.
    /// </summary>
    public const string Name = "Identity Integration Tests";
}