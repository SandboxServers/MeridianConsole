using Xunit;
using Dhadgar.Mods;

namespace Dhadgar.Mods.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Mods", Hello.Message);
    }
}
