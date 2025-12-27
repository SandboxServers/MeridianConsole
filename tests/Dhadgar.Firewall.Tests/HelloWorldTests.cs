using Xunit;
using Dhadgar.Firewall;

namespace Dhadgar.Firewall.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Firewall", Hello.Message);
    }
}
