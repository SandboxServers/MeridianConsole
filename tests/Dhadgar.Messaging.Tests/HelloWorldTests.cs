using Xunit;
using Dhadgar.Messaging;

namespace Dhadgar.Messaging.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Messaging", Hello.Message);
    }
}
