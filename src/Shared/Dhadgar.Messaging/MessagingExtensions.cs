using Dhadgar.Messaging.Publishing;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Messaging;

/// <summary>
/// Extension methods for configuring MassTransit messaging in Dhadgar services.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Default retry configuration for transient failures.
    /// </summary>
    public static class RetryDefaults
    {
        /// <summary>Maximum number of retry attempts.</summary>
        public const int RetryCount = 3;

        /// <summary>Initial delay before first retry.</summary>
        public static readonly TimeSpan InitialInterval = TimeSpan.FromSeconds(1);

        /// <summary>Maximum delay between retries.</summary>
        public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(30);

        /// <summary>Multiplier for exponential backoff.</summary>
        public const double IntervalMultiplier = 2.0;
    }

    /// <summary>
    /// Adds MassTransit with RabbitMQ transport and Dhadgar conventions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration to read RabbitMQ settings from.</param>
    /// <param name="configure">Optional callback to register consumers, sagas, etc.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Configures MassTransit with:
    /// <list type="bullet">
    ///   <item><description>RabbitMQ transport with configurable host/credentials</description></item>
    ///   <item><description>Stable exchange naming (<c>meridian.{messagetype}</c>)</description></item>
    ///   <item><description>Exponential backoff retry policy for transient failures</description></item>
    ///   <item><description>Dead-letter queue for poison messages (after retries exhausted)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Configuration keys:
    /// <list type="bullet">
    ///   <item><description><c>RabbitMq:Host</c> - RabbitMQ host (default: localhost)</description></item>
    ///   <item><description><c>RabbitMq:Username</c> - Username (default: dhadgar)</description></item>
    ///   <item><description><c>RabbitMq:Password</c> - Password (default: dhadgar)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDhadgarMessaging(
        this IServiceCollection services,
        IConfiguration config,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        services.AddMassTransit(x =>
        {
            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = config["RabbitMq:Host"] ?? "localhost";
                var user = config["RabbitMq:Username"] ?? "dhadgar";
                var pass = config["RabbitMq:Password"] ?? "dhadgar";

                cfg.Host(host, h =>
                {
                    h.Username(user);
                    h.Password(pass);
                });

                // Stable, explicit exchange names (aligns with the scope doc's meridian.* conventions)
                cfg.MessageTopology.SetEntityNameFormatter(new StaticEntityNameFormatter());

                // Configure default retry policy for all consumers
                cfg.UseMessageRetry(r => ConfigureExponentialRetry(r, RetryDefaults.RetryCount));

                // Configure dead-letter queue for messages that fail all retries
                // RabbitMQ automatically routes to _error queue after all retries exhausted
                cfg.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15),
                    TimeSpan.FromMinutes(30)));

                // Circuit breaker prevents cascade failures when downstream services are unhealthy
                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 15;       // Trip when failure rate exceeds 15% in tracking period
                    cb.ActiveThreshold = 10;     // Start tracking after 10 messages observed
                    cb.ResetInterval = TimeSpan.FromMinutes(5);  // Try again after 5 minutes
                });

                cfg.ConfigureEndpoints(ctx);
            });
        });

        // Register IEventPublisher for standardized event publishing
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services;
    }

    /// <summary>
    /// Configures consumer-specific retry policy with exponential backoff.
    /// </summary>
    /// <param name="configurator">The consumer configurator.</param>
    /// <param name="retryCount">Number of retry attempts (default: 3).</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Use this when a consumer needs different retry behavior than the default.
    /// </remarks>
    /// <example>
    /// <code>
    /// x.AddConsumer&lt;MyConsumer&gt;(cfg =&gt;
    /// {
    ///     cfg.ConfigureRetry(5); // 5 retries instead of default 3
    /// });
    /// </code>
    /// </example>
    public static IConsumerConfigurator<TConsumer> ConfigureRetry<TConsumer>(
        this IConsumerConfigurator<TConsumer> configurator,
        int retryCount = RetryDefaults.RetryCount)
        where TConsumer : class, IConsumer
    {
        configurator.UseMessageRetry(r => ConfigureExponentialRetry(r, retryCount));

        return configurator;
    }

    /// <summary>
    /// Configures exponential retry with consistent backoff parameters.
    /// </summary>
    /// <param name="retryConfigurator">The retry configurator.</param>
    /// <param name="retryCount">Number of retry attempts.</param>
    private static void ConfigureExponentialRetry(IRetryConfigurator retryConfigurator, int retryCount)
    {
        retryConfigurator.Exponential(
            retryCount,
            RetryDefaults.InitialInterval,
            RetryDefaults.MaxInterval,
            TimeSpan.FromMilliseconds(200)); // jitter
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
