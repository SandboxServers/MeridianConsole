using Xunit;
using Dhadgar.Files;

namespace Dhadgar.Files.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Files", Hello.Message);
    }
}
