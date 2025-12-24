using Xunit;
using Dhadgar.Cli;

namespace Dhadgar.Cli.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Cli", Hello.Message);
    }
}
