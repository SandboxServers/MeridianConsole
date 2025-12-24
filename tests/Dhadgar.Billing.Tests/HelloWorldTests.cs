using Xunit;
using Dhadgar.Billing;

namespace Dhadgar.Billing.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Billing", Hello.Message);
    }
}
