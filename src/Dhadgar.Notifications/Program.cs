using Dhadgar.Contracts.Notifications;
using Dhadgar.Messaging;
using Dhadgar.Notifications;
using Dhadgar.Notifications.Consumers;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Services;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Security;
using Dhadgar.ServiceDefaults.Swagger;
using MassTransit;
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
builder.Services.AddSingleton<IEmailProvider, Office365EmailProvider>();

// Admin API key authentication for internal endpoints
builder.Services.AddAdminApiKeyAuthentication(builder.Configuration);

// MassTransit with consumers and EF Outbox for atomic operations
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<ServerStartedConsumer>();
    x.AddConsumer<ServerStoppedConsumer>();
    x.AddConsumer<ServerCrashedConsumer>();
    x.AddConsumer<SendEmailNotificationConsumer>();

    // Enable EF Outbox for atomic log persistence + message publishing
    x.AddEntityFrameworkOutbox<NotificationsDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
});

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

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

// Admin endpoints - Protected by API key authentication
var adminGroup = app.MapGroup("/api/v1")
    .WithTags("Admin")
    .RequireAuthorization("AdminApi");

adminGroup.MapGet("/notifications/logs", async (
    int? limit,
    string? status,
    Guid? orgId,
    HttpContext context,
    NotificationsDbContext db,
    CancellationToken ct) =>
{
    var query = db.Logs.AsQueryable();

    // Tenant isolation: filter by organization
    // Check X-Tenant-Id header first, then orgId query param
    var tenantHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (!string.IsNullOrEmpty(tenantHeader) && Guid.TryParse(tenantHeader, out var headerOrgId))
    {
        query = query.Where(l => l.OrganizationId == headerOrgId);
    }
    else if (orgId.HasValue)
    {
        query = query.Where(l => l.OrganizationId == orgId.Value);
    }
    // If neither specified, admins can see all logs (admin API key required)

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
}).WithName("GetNotificationLogs");

adminGroup.MapPost("/notifications/test", async (
    TestNotificationRequest request,
    IPublishEndpoint publishEndpoint,
    NotificationsDbContext db,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var notificationId = Guid.NewGuid();
    var orgId = request.OrgId ?? Guid.Empty;

    logger.LogInformation(
        "Sending test notification {NotificationId} for org {OrgId}",
        notificationId, orgId);

    // Send to Discord
    await publishEndpoint.Publish(new Dhadgar.Contracts.Notifications.SendDiscordNotification(
        NotificationId: notificationId,
        OrgId: orgId,
        ServerId: null,
        Title: request.Title ?? "Test Notification",
        Message: request.Message ?? "This is a test notification from Meridian Console.",
        Severity: request.Severity ?? Dhadgar.Contracts.Notifications.NotificationSeverity.Info,
        EventType: "test.notification",
        Fields: new Dictionary<string, string>
        {
            ["Source"] = "CLI Test",
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o")
        },
        OccurredAtUtc: DateTimeOffset.UtcNow), ct);

    // Log to database
    db.Logs.Add(new Dhadgar.Notifications.Data.Entities.NotificationLog
    {
        Id = notificationId,
        OrganizationId = orgId,
        EventType = "test.notification",
        Channel = "discord",
        Title = request.Title ?? "Test Notification",
        Message = request.Message ?? "This is a test notification from Meridian Console.",
        Status = "pending",
        CreatedAtUtc = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { notificationId, message = "Test notification sent" });
}).WithName("SendTestNotification");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
