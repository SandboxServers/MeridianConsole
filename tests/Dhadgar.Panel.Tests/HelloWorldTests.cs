using Xunit;
using Dhadgar.Panel;

namespace Dhadgar.Panel.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Panel", Hello.Message);
    }
}
