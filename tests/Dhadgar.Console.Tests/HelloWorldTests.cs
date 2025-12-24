using Xunit;
using Dhadgar.Console;

namespace Dhadgar.Console.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Console", Hello.Message);
    }
}
