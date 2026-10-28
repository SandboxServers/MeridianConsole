using Xunit;

namespace Dhadgar.Mods.Tests;

[CollectionDefinition("Mods Integration")]
public class ModsTestCollectionDefinition : ICollectionFixture<ModsWebApplicationFactory>
{
}
