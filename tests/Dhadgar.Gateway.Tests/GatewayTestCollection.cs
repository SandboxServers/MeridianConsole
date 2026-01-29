using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Collection definition for Gateway integration tests.
/// Shares a single WebApplicationFactory instance across all tests in the collection.
/// </summary>
[CollectionDefinition("Gateway Integration")]
public class GatewayTestCollectionDefinition : ICollectionFixture<GatewayWebApplicationFactory>
{
}
