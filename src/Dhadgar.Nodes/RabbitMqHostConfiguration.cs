using MassTransit;

namespace Dhadgar.Nodes;

/// <summary>
/// Applies <see cref="RabbitMqOptions"/> to the MassTransit RabbitMQ bus factory.
/// </summary>
public static class RabbitMqHostConfiguration
{
    /// <summary>
    /// Configures the RabbitMQ host, port, virtual host and credentials on the bus factory.
    /// </summary>
    /// <param name="cfg">The RabbitMQ bus factory configurator.</param>
    /// <param name="options">The validated RabbitMQ options.</param>
    public static void Configure(IRabbitMqBusFactoryConfigurator cfg, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(options);

        cfg.Host(options.Host, (ushort)options.Port, options.VirtualHost, h =>
        {
            h.Username(options.Username);
            h.Password(options.Password);
        });
    }
}
