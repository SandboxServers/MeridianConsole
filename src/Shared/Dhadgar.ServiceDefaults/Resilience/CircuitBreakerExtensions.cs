using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.Resilience;

/// <summary>
/// Extension methods for registering and configuring the circuit breaker middleware.
/// </summary>
public static class CircuitBreakerExtensions
{
    /// <summary>
    /// Adds circuit breaker services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind options from.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCircuitBreaker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CircuitBreakerOptions>()
            .Bind(configuration.GetSection(CircuitBreakerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register the in-memory state store as singleton
        services.AddSingleton<ICircuitBreakerStateStore, InMemoryCircuitBreakerStateStore>();

        return services;
    }

    /// <summary>
    /// Adds circuit breaker services with a custom state store implementation.
    /// Use this for distributed scenarios (e.g., Redis-backed state store).
    /// </summary>
    /// <typeparam name="TStateStore">The state store implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind options from.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCircuitBreaker<TStateStore>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TStateStore : class, ICircuitBreakerStateStore
    {
        services.AddOptions<CircuitBreakerOptions>()
            .Bind(configuration.GetSection(CircuitBreakerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ICircuitBreakerStateStore, TStateStore>();

        return services;
    }

    /// <summary>
    /// Adds the circuit breaker middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseCircuitBreaker(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CircuitBreakerMiddleware>();
    }
}
