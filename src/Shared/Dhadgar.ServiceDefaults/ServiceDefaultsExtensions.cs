using Dhadgar.ServiceDefaults.Readiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        app.MapGet("/healthz", () => Results.Ok(new
        {
            service = app.Environment.ApplicationName,
            status = "ok",
            timestamp = DateTime.UtcNow
        }))
        .AllowAnonymous()
        .WithTags("Health");

        app.MapDhadgarReadinessEndpoints();
        return app;
    }

    public static WebApplication MapDhadgarReadinessEndpoints(this WebApplication app)
    {
        app.MapGet("/livez", () => Results.Ok(new
        {
            service = app.Environment.ApplicationName,
            status = "live",
            timestamp = DateTime.UtcNow
        }))
        .AllowAnonymous()
        .WithTags("Health");

        app.MapGet("/readyz", async (IReadinessCheck? readinessCheck, CancellationToken ct) =>
        {
            ReadinessResult result;

            if (readinessCheck is null)
            {
                result = ReadinessResult.Ready();
            }
            else
            {
                result = await readinessCheck.CheckAsync(ct);
            }

            var payload = new Dictionary<string, object?>
            {
                ["service"] = app.Environment.ApplicationName,
                ["status"] = result.IsReady ? "ready" : "unhealthy",
                ["timestamp"] = DateTime.UtcNow
            };

            if (result.Details is not null)
            {
                payload["details"] = result.Details;
            }

            return Results.Json(payload, statusCode: result.IsReady
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
        })
        .AllowAnonymous()
        .WithTags("Health");

        return app;
    }
}
