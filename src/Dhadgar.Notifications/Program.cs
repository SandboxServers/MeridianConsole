using Dhadgar.Messaging;
using Dhadgar.Notifications;
using Dhadgar.Notifications.Consumers;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Services;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults();
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Notifications API",
    description: "Email, Discord, and webhook notifications for Meridian Console");

// Database
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

// Services
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// MassTransit with consumers
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<ServerStartedConsumer>();
    x.AddConsumer<ServerStoppedConsumer>();
    x.AddConsumer<ServerCrashedConsumer>();
});

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Auto-migrate in dev
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
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

// Basic endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Notifications", message = Dhadgar.Notifications.Hello.Message }))
    .WithTags("Health").WithName("NotificationsServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Notifications.Hello.Message))
    .WithTags("Health").WithName("NotificationsHello");
app.MapDhadgarDefaultEndpoints();

// Admin endpoint - Internal only, not exposed through Gateway
// TODO: Add authentication when exposed publicly (currently internal service-to-service only)
app.MapGet("/api/v1/notifications/logs", async (
    int? limit,
    string? status,
    NotificationsDbContext db,
    CancellationToken ct) =>
{
    var query = db.Logs.AsQueryable();

    if (!string.IsNullOrEmpty(status))
    {
        query = query.Where(l => l.Status == status);
    }

    // Clamp limit to prevent oversized queries (max 100)
    var safeLimit = Math.Clamp(limit ?? 50, 1, 100);

    var logs = await query
        .OrderByDescending(l => l.CreatedAtUtc)
        .Take(safeLimit)
        .ToListAsync(ct);

    return Results.Ok(logs);
}).WithTags("Admin").WithName("GetNotificationLogs");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
