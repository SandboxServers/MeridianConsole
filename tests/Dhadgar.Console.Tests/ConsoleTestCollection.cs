using Xunit;

namespace Dhadgar.Console.Tests;

[CollectionDefinition("Console Integration")]
public class ConsoleTestCollectionDefinition : ICollectionFixture<ConsoleWebApplicationFactory>
{
}
