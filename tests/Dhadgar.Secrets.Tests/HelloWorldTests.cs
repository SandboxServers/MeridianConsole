using Xunit;
using Dhadgar.Secrets;

namespace Dhadgar.Secrets.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Secrets", Hello.Message);
    }
}
