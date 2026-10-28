using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Messaging.Tests;

/// <summary>
/// Verifies how <see cref="MessagingExtensions.AddDhadgarMessaging"/> applies the optional
/// <c>RabbitMq:Port</c> setting. The bus is created but never started, so no broker
/// connection is attempted. Providers are disposed asynchronously, as the host does.
/// </summary>
public class MessagingExtensionsHostPortTests
{
    private static IConfiguration BuildConfiguration(string host, string? port)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = host,
            ["RabbitMq:Username"] = "valid-user",
            ["RabbitMq:Password"] = "valid-password",
            ["RabbitMq:Port"] = port
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddDhadgarMessaging(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddDhadgarMessaging_ConfiguredPort_IsUsedForBrokerAddress()
    {
        await using var provider = BuildProvider(BuildConfiguration("broker.test", "20021"));

        var bus = provider.GetRequiredService<IBusControl>();

        Assert.Equal("broker.test", bus.Address.Host);
        Assert.Equal(20021, bus.Address.Port);
    }

    [Fact]
    public async Task AddDhadgarMessaging_NoPort_LeavesDefaultAmqpPort()
    {
        await using var provider = BuildProvider(BuildConfiguration("broker.test", null));

        var bus = provider.GetRequiredService<IBusControl>();

        // MassTransit leaves the default AMQP port out of the address (-1).
        Assert.Equal(-1, bus.Address.Port);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("70000")]
    public async Task AddDhadgarMessaging_InvalidPort_ThrowsNamingConfigKey(string port)
    {
        await using var provider = BuildProvider(BuildConfiguration("broker.test", port));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBusControl>());

        Assert.Contains("RabbitMq:Port", ex.Message);
    }

    [Fact]
    public async Task AddDhadgarMessaging_PortCombinedWithUriHost_ThrowsNamingConfigKey()
    {
        await using var provider = BuildProvider(BuildConfiguration("rabbitmq://broker.test:5673/", "20021"));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBusControl>());

        Assert.Contains("RabbitMq:Port", ex.Message);
    }
}
