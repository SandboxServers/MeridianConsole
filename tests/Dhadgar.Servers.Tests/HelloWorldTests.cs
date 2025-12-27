using Xunit;
using Dhadgar.Servers;

namespace Dhadgar.Servers.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Servers", Hello.Message);
    }
}
