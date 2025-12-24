using Xunit;
using Dhadgar.Notifications;

namespace Dhadgar.Notifications.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Notifications", Hello.Message);
    }
}
