using Xunit;
using Dhadgar.Gateway;

namespace Dhadgar.Gateway.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Gateway", Hello.Message);
    }
}
