using Xunit;
using Dhadgar.Scope;

namespace Dhadgar.Scope.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Scope", Hello.Message);
    }
}
