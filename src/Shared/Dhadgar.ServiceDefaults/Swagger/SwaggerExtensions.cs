using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dhadgar.ServiceDefaults.Swagger;

/// <summary>
/// Extension methods for configuring Swagger/OpenAPI in Meridian Console services.
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Adds Swagger/OpenAPI services with standard Meridian Console configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="title">The API title shown in Swagger UI.</param>
    /// <param name="description">The API description shown in Swagger UI.</param>
    /// <param name="configureOptions">Optional additional Swagger configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMeridianSwagger(
        this IServiceCollection services,
        string title,
        string description,
        Action<SwaggerGenOptions>? configureOptions = null)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title,
                Version = "v1",
                Description = description
            });

            // Apply any additional configuration (including security if needed)
            configureOptions?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Configures Swagger middleware for Development and Testing environments.
    /// Call this after app.Build() and before app.Run().
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseMeridianSwagger(this WebApplication app)
    {
        // Enable Swagger in Development and Testing environments
        // Testing is used by WebApplicationFactory integration tests
        if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
                options.RoutePrefix = "swagger";
            });
        }

        return app;
    }
}
