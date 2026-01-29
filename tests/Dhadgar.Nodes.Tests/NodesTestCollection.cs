using Xunit;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Collection definition for Nodes integration tests.
/// Shares a single WebApplicationFactory instance across all tests in the collection.
/// </summary>
[CollectionDefinition("Nodes Integration")]
public class NodesTestCollectionDefinition : ICollectionFixture<NodesWebApplicationFactory>
{
}
