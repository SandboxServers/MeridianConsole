using Xunit;

namespace Dhadgar.Identity.Tests;

/// <summary>
/// Collection definition for Identity integration tests.
/// Shares a single WebApplicationFactory instance across all tests in the collection.
/// </summary>
[CollectionDefinition("Identity Integration")]
public class IdentityTestCollectionDefinition : ICollectionFixture<IdentityWebApplicationFactory>
{
}
