using Xunit;
using Dhadgar.Web;

namespace Dhadgar.Web.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Web", Hello.Message);
    }
}
