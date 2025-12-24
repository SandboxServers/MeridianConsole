using Xunit;
using Dhadgar.Agent.Linux;

namespace Dhadgar.Agent.Linux.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Linux", Hello.Message);
    }
}
