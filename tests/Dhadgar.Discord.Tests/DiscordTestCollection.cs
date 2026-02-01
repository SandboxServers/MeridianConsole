using Xunit;

namespace Dhadgar.Discord.Tests;

[CollectionDefinition("Discord Integration")]
public class DiscordTestCollectionDefinition : ICollectionFixture<DiscordWebApplicationFactory>
{
}
