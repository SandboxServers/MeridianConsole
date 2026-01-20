using Dhadgar.Discord;
using Dhadgar.Discord.Bot;
using Dhadgar.Discord.Commands;
using Dhadgar.Discord.Consumers;
using Dhadgar.Discord.Data;
using Dhadgar.Discord.Services;
using Dhadgar.Messaging;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Security;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults();
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Discord API",
    description: "Discord bot integration for Meridian Console");

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
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Discord");

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

// Database (minimal - just notification logs)
builder.Services.AddDbContext<DiscordDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// HTTP clients
builder.Services.AddHttpClient<IDiscordCredentialProvider, DiscordCredentialProvider>(client =>
{
    var secretsUrl = builder.Configuration["SecretsService:Url"] ?? "http://localhost:5080";
    client.BaseAddress = new Uri(secretsUrl);
});

builder.Services.AddHttpClient(); // For webhook posts

// Platform health checking
builder.Services.AddHttpClient<IPlatformHealthService, PlatformHealthService>();

// Discord bot
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddSingleton<IDiscordBotService>(sp => sp.GetRequiredService<DiscordBotService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());
builder.Services.AddSingleton<SlashCommandHandler>();

// Admin API key authentication for internal endpoints
builder.Services.AddAdminApiKeyAuthentication(builder.Configuration);

// MassTransit
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<SendDiscordNotificationConsumer>();
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
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

// Basic endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Discord", message = Dhadgar.Discord.Hello.Message }))
    .WithTags("Health").WithName("DiscordServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Discord.Hello.Message))
    .WithTags("Health").WithName("DiscordHello");
app.MapGet("/healthz", (IDiscordBotService bot) =>
{
    var botStatus = bot.ConnectionState.ToString();
    return Results.Ok(new { service = "Dhadgar.Discord", status = "ok", botStatus });
}).WithTags("Health").WithName("DiscordHealthCheck");

// Admin endpoints - Protected by API key authentication
var adminGroup = app.MapGroup("/api/v1")
    .WithTags("Admin")
    .RequireAuthorization("AdminApi");

adminGroup.MapGet("/discord/logs", async (
    int? limit,
    Guid? orgId,
    HttpContext context,
    DiscordDbContext db,
    CancellationToken ct) =>
{
    var query = db.NotificationLogs.AsQueryable();

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

    // Clamp limit to prevent oversized queries (max 100)
    var safeLimit = Math.Clamp(limit ?? 50, 1, 100);

    var logs = await query
        .OrderByDescending(l => l.CreatedAtUtc)
        .Take(safeLimit)
        .ToListAsync(ct);

    return Results.Ok(logs);
}).WithName("GetDiscordLogs");

adminGroup.MapGet("/platform/health", async (
    IPlatformHealthService healthService,
    CancellationToken ct) =>
{
    var status = await healthService.CheckAllServicesAsync(ct);
    return Results.Ok(status);
}).WithName("GetPlatformHealth");

adminGroup.MapGet("/discord/channels", (
    IDiscordBotService bot,
    ulong? guildId) =>
{
    if (bot.ConnectionState != Discord.ConnectionState.Connected)
    {
        return Results.Ok(new
        {
            connected = false,
            message = "Bot is not connected to Discord",
            guilds = Array.Empty<object>()
        });
    }

    var client = bot.Client;
    var guilds = client.Guilds
        .Where(g => !guildId.HasValue || g.Id == guildId.Value)
        .Select(g => new
        {
            guildId = g.Id,
            guildName = g.Name,
            channels = g.TextChannels
                .OrderBy(c => c.Position)
                .Select(c => new
                {
                    channelId = c.Id,
                    name = c.Name,
                    category = c.Category?.Name,
                    position = c.Position
                })
                .ToList()
        })
        .ToList();

    return Results.Ok(new
    {
        connected = true,
        guildCount = guilds.Count,
        guilds
    });
}).WithName("GetDiscordChannels");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
