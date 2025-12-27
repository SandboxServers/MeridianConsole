using Xunit;
using Dhadgar.Agent.Windows;

namespace Dhadgar.Agent.Windows.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Windows", Hello.Message);
    }
}
