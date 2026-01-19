using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddDhadgarMessaging(this IServiceCollection services, IConfiguration config, Action<IBusRegistrationConfigurator>? configure = null)
    {
        services.AddMassTransit(x =>
        {
            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = config.GetConnectionString("RabbitMqHost") ?? "localhost";
                var user = config["RabbitMq:Username"] ?? "dhadgar";
                var pass = config["RabbitMq:Password"] ?? "dhadgar";

                cfg.Host(host, h =>
                {
                    h.Username(user);
                    h.Password(pass);
                });

                // Stable, explicit exchange names (aligns with the scope doc's meridian.* conventions)
                cfg.MessageTopology.SetEntityNameFormatter(new StaticEntityNameFormatter());

                // Configure retry policies for all endpoints
                cfg.UseMessageRetry(r =>
                {
                    // Exponential backoff: 200ms, 400ms, 800ms, 1.6s, 3.2s (5 retries)
                    r.Exponential(5,
                        TimeSpan.FromMilliseconds(200),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromMilliseconds(200));

                    // Don't retry on validation errors
                    r.Ignore<ArgumentNullException>();
                    r.Ignore<ArgumentException>();
                });

                // In-memory outbox prevents duplicate sends on retry
                cfg.UseInMemoryOutbox(ctx);

                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }

    private sealed class StaticEntityNameFormatter : IEntityNameFormatter
    {
        public string FormatEntityName<T>()
        {
            // Keep topology stable even if namespaces/types refactor.
            // You can evolve this to a mapping table later.
            var name = typeof(T).Name;
            return name switch
            {
                _ => $"meridian.{name}".ToLowerInvariant()
            };
        }
    }
}
