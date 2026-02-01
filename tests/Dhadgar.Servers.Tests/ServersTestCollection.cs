using Xunit;

namespace Dhadgar.Servers.Tests;

[CollectionDefinition("Servers Integration")]
public class ServersTestCollectionDefinition : ICollectionFixture<ServersWebApplicationFactory>
{
}
