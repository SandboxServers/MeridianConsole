using Xunit;
using Dhadgar.Identity;

namespace Dhadgar.Identity.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Identity", Hello.Message);
    }
}
