using Dhadgar.Notifications;
using Dhadgar.Notifications.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres | HealthCheckDependencies.RabbitMq);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Notifications API",
    description: "Email, Discord, and webhook notifications for Meridian Console");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// OpenTelemetry configuration
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
Uri? otlpUri = null;
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsedUri))
    {
        otlpUri = parsedUri;
    }
}
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Notifications");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;

    if (otlpUri is not null)
    {
        options.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
    }
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();

        if (otlpUri is not null)
        {
            metrics.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Notifications", message = Dhadgar.Notifications.Hello.Message }))
    .WithTags("Health").WithName("NotificationsServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Notifications.Hello.Message))
    .WithTags("Health").WithName("NotificationsHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
