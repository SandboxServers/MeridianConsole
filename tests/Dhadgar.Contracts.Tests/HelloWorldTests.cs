using Xunit;
using Dhadgar.Contracts;

namespace Dhadgar.Contracts.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Contracts", Hello.Message);
    }
}
