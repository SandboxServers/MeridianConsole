using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhadgar.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy());

        return services;
    }

    public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.MapHealthChecks("/readyz");
        return app;
    }
}
