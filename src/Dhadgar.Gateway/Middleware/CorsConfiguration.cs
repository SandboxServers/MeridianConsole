namespace Dhadgar.Gateway.Middleware;

public static class CorsConfiguration
{
    public const string PolicyName = "MeridianConsolePolicy";

    public static IServiceCollection AddMeridianConsoleCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        // SECURITY: Require explicit CORS origins in non-Development environments
        if (!environment.IsDevelopment())
        {
            if (allowedOrigins.Length == 0)
            {
                throw new InvalidOperationException(
                    "CORS:AllowedOrigins must be configured in non-Development environments. " +
                    "Add allowed origins to appsettings.json or environment configuration.");
            }

            if (allowedOrigins.Contains("*"))
            {
                throw new InvalidOperationException(
                    "CORS:AllowedOrigins cannot contain the wildcard '*' in non-Development environments. " +
                    "Please specify explicit origins.");
            }
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, builder =>
            {
                if (allowedOrigins.Length > 0)
                {
                    builder.WithOrigins(allowedOrigins);
                }
                else
                {
                    // Development: Allow all origins
                    builder.AllowAnyOrigin();
                }

                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("X-Correlation-Id", "X-Request-Id", "X-Trace-Id");

                // Allow credentials only if not AllowAnyOrigin
                if (allowedOrigins.Length > 0)
                {
                    builder.AllowCredentials();
                }
            });
        });

        return services;
    }
}
