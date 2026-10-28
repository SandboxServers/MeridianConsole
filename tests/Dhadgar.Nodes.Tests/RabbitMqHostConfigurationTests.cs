using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Verifies that <see cref="RabbitMqHostConfiguration.Configure"/> (the call Nodes makes at
/// startup) gives the bus the configured broker port. The bus is created but never started,
/// so no broker connection is attempted.
/// </summary>
public class RabbitMqHostConfigurationTests
{
    private static ServiceProvider BuildProvider(RabbitMqOptions options)
    {
        var services = new ServiceCollection();
        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((_, cfg) => RabbitMqHostConfiguration.Configure(cfg, options));
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Configure_UsesConfiguredPort()
    {
        var options = new RabbitMqOptions
        {
            Host = "broker.test",
            Port = 20021,
            VirtualHost = "/",
            Username = "valid-user",
            Password = "valid-password"
        };
        await using var provider = BuildProvider(options);

        var bus = provider.GetRequiredService<IBusControl>();

        Assert.Equal("broker.test", bus.Address.Host);
        Assert.Equal(20021, bus.Address.Port);
    }

    [Fact]
    public async Task Configure_DefaultPort_Is5672()
    {
        var options = new RabbitMqOptions
        {
            Host = "broker.test",
            Username = "valid-user",
            Password = "valid-password"
        };
        await using var provider = BuildProvider(options);

        var bus = provider.GetRequiredService<IBusControl>();

        // MassTransit leaves the default AMQP port out of the address (-1).
        Assert.Equal(-1, bus.Address.Port);
    }
}
