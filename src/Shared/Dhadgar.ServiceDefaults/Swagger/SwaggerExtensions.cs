using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace Dhadgar.ServiceDefaults.Swagger;

/// <summary>
/// Extension methods for configuring OpenAPI in Meridian Console services.
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Adds OpenAPI services with standard Meridian Console configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="title">The API title shown in Swagger UI.</param>
    /// <param name="description">The API description shown in Swagger UI.</param>
    /// <param name="configureOptions">Optional additional OpenAPI configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMeridianSwagger(
        this IServiceCollection services,
        string title,
        string description,
        Action<OpenApiOptions>? configureOptions = null)
    {
        services.AddEndpointsApiExplorer();
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = title;
                document.Info.Version = "v1";
                document.Info.Description = description;

                return Task.CompletedTask;
            });

            // Apply any additional configuration (including security if needed)
            configureOptions?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Configures OpenAPI middleware for Development and Testing environments.
    /// Call this after app.Build() and before app.Run().
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseMeridianSwagger(this WebApplication app)
    {
        // Enable OpenAPI in Development and Testing environments
        // Testing is used by WebApplicationFactory integration tests
        if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference();
        }

        return app;
    }
}
