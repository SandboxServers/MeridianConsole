using Xunit;
using Dhadgar.ServiceDefaults;

namespace Dhadgar.ServiceDefaults.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.ServiceDefaults", Hello.Message);
    }
}
