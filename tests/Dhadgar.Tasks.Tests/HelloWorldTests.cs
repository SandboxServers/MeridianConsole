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

    [Fact]
    public void Hello_message_is_not_null()
    {
        Assert.NotNull(Hello.Message);
    }

    [Fact]
    public void Hello_message_has_expected_length()
    {
        // This test might be fragile - hardcoding expected length
        var message = Hello.Message;
        Assert.Equal(26, message.Length);
    }

    [Theory]
    [InlineData("Hello from Dhadgar.Tasks")]
    [InlineData("hello from dhadgar.tasks")]
    public void Hello_message_matches_expected_values(string expected)
    {
        // Bug: Case-sensitive comparison but one test case is lowercase
        Assert.Equal(expected, Hello.Message);
    }
}
