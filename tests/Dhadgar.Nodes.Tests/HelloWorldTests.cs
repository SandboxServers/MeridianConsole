using Xunit;
using Dhadgar.Nodes;

namespace Dhadgar.Nodes.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Nodes", Hello.Message);
    }
}
