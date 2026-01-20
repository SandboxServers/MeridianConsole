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

            // Enable delayed message scheduler using RabbitMQ delayed message plugin
            // This allows scheduling message redelivery when transient failures occur
            // Requires rabbitmq_delayed_message_exchange plugin on the server
            x.AddDelayedMessageScheduler();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = config["RabbitMq:Host"] ?? config.GetConnectionString("RabbitMqHost") ?? "localhost";
                var user = config["RabbitMq:Username"] ?? "dhadgar";
                var pass = config["RabbitMq:Password"] ?? "dhadgar";

                cfg.Host(host, h =>
                {
                    h.Username(user);
                    h.Password(pass);
                });

                // Enable delayed message scheduler
                cfg.UseDelayedMessageScheduler();

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

                // Schedule message redelivery after immediate retries are exhausted
                // Messages that fail all retries will be redelivered after delay before hitting error queue
                cfg.UseDelayedRedelivery(r =>
                {
                    // Redelivery intervals: 5min, 15min, 1hr (3 attempts)
                    r.Intervals(
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(15),
                        TimeSpan.FromHours(1));
                });

                // In-memory outbox prevents duplicate sends on retry
                cfg.UseInMemoryOutbox(ctx);

                // Dead letter queue endpoint is intentionally not configured here.
                // MassTransit automatically routes failed messages to {queue}_error queues.
                // To implement centralized dead letter handling:
                // 1. Create a DeadLetterConsumer that implements IConsumer<Fault<T>> for each message type
                // 2. Register the consumer(s) in the service's MassTransit configuration
                // 3. Configure error queue forwarding or use MassTransit's fault handling
                // See: https://masstransit.io/documentation/concepts/exceptions

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
