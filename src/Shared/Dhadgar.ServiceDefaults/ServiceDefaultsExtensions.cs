using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhadgar.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return services;
    }

    public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
    {
        Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
        {
            var payload = new Dictionary<string, object?>
            {
                ["service"] = app.Environment.ApplicationName,
                ["status"] = report.Status == HealthStatus.Healthy ? "ok" : "unhealthy",
                ["timestamp"] = DateTime.UtcNow
            };

            if (report.Entries.Count > 0)
            {
                var checks = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in report.Entries)
                {
                    var entryPayload = new Dictionary<string, object?>
                    {
                        ["status"] = entry.Value.Status.ToString(),
                        ["duration_ms"] = entry.Value.Duration.TotalMilliseconds
                    };

                    if (!string.IsNullOrWhiteSpace(entry.Value.Description))
                    {
                        entryPayload["description"] = entry.Value.Description;
                    }

                    if (entry.Value.Data.Count > 0)
                    {
                        entryPayload["data"] = entry.Value.Data;
                    }

                    checks[entry.Key] = entryPayload;
                }

                payload["checks"] = checks;
            }

            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(payload);
        }

        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/livez", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        return app;
    }
}
