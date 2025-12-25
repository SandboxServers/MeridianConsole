using Xunit;
using Dhadgar.ShoppingCart;

namespace Dhadgar.ShoppingCart.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.ShoppingCart", Hello.Message);
    }
}
