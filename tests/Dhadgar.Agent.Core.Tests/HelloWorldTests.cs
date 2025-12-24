using Xunit;
using Dhadgar.Agent.Core;

namespace Dhadgar.Agent.Core.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Core", Hello.Message);
    }
}
