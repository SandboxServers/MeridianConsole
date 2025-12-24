using Xunit;
using Dhadgar.Shared;

namespace Dhadgar.Shared.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Shared", Hello.Message);
    }
}
