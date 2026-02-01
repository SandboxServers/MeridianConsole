using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// Extension methods for registering error handling infrastructure.
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Adds Dhadgar error handling services including Problem Details and GlobalExceptionHandler.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDhadgarErrorHandling(this IServiceCollection services)
    {
        // Register TimeProvider.System if not already registered (allows test overrides)
        services.TryAddSingleton(TimeProvider.System);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                // Ensure trace IDs are always present (for Results.Problem() calls)
                var traceId = Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? context.HttpContext.TraceIdentifier
                    ?? "unknown";

                var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? context.HttpContext.TraceIdentifier
                    ?? "unknown";

                context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
                context.ProblemDetails.Extensions.TryAdd("correlationId", correlationId);
                context.ProblemDetails.Extensions.TryAdd("timestamp", TimeProvider.System.GetUtcNow());
            };
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    /// <summary>
    /// Registers the Dhadgar error handling middleware pipeline.
    /// </summary>
    /// <param name="app">The application builder to configure.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// <list type="number">
    ///   <item><see cref="Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"/> - Activates IExceptionHandler implementations</item>
    ///   <item>StatusCodePages middleware - Handles non-exception errors (404 from routing, etc.)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Call this early in the pipeline, before routing and authentication:
    /// <code>
    /// app.UseDhadgarErrorHandling();
    /// app.UseRouting();
    /// app.UseAuthentication();
    /// </code>
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseDhadgarErrorHandling(this IApplicationBuilder app)
    {
        // UseExceptionHandler activates IExceptionHandler implementations
        app.UseExceptionHandler();

        // StatusCodePages handles non-exception errors (404 from routing, etc.)
        app.UseStatusCodePages(async context =>
        {
            // Only handle if response body hasn't been written
            if (!context.HttpContext.Response.HasStarted)
            {
                var traceId = Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? context.HttpContext.TraceIdentifier
                    ?? "unknown";

                var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? context.HttpContext.TraceIdentifier
                    ?? "unknown";

                var statusCode = context.HttpContext.Response.StatusCode;
                var problemDetails = new ProblemDetails
                {
                    Type = $"https://httpstatuses.com/{statusCode}",
                    Title = GetTitle(statusCode),
                    Status = statusCode,
                    Instance = context.HttpContext.Request.Path
                };
                problemDetails.Extensions["traceId"] = traceId;
                problemDetails.Extensions["correlationId"] = correlationId;
                problemDetails.Extensions["timestamp"] = TimeProvider.System.GetUtcNow();

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(problemDetails);
            }
        });

        return app;
    }

    /// <summary>
    /// Gets the standard HTTP status title for a given status code.
    /// </summary>
    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Error"
    };
}
