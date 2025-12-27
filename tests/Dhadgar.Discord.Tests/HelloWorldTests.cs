using Xunit;
using Dhadgar.Discord;

namespace Dhadgar.Discord.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Discord", Hello.Message);
    }
}
