using Xunit;
using Dhadgar.Tasks;

namespace Dhadgar.Tasks.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Tasks", Hello.Message);
    }
}
