using Dhadgar.Discord;
using Dhadgar.Discord.Bot;
using Dhadgar.Discord.Commands;
using Dhadgar.Discord.Consumers;
using Dhadgar.Discord.Data;
using Dhadgar.Discord.Services;
using Dhadgar.Messaging;
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
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());
builder.Services.AddSingleton<SlashCommandHandler>();

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

// Register slash command handler
var botService = app.Services.GetRequiredService<DiscordBotService>();
var commandHandler = app.Services.GetRequiredService<SlashCommandHandler>();
commandHandler.Register(botService.Client);

// Basic endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Discord", message = Hello.Message }))
    .WithTags("Health").WithName("DiscordServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("DiscordHello");
app.MapGet("/healthz", () =>
{
    var bot = app.Services.GetRequiredService<DiscordBotService>();
    var botStatus = bot.Client.ConnectionState.ToString();
    return Results.Ok(new { service = "Dhadgar.Discord", status = "ok", botStatus });
}).WithTags("Health").WithName("DiscordHealthCheck");

// Admin endpoints
app.MapGet("/api/v1/discord/logs", async (
    int? limit,
    DiscordDbContext db,
    CancellationToken ct) =>
{
    var logs = await db.NotificationLogs
        .OrderByDescending(l => l.CreatedAtUtc)
        .Take(limit ?? 50)
        .ToListAsync(ct);

    return Results.Ok(logs);
}).WithTags("Admin").WithName("GetDiscordLogs");

// Platform health check endpoint
app.MapGet("/api/v1/platform/health", async (
    IPlatformHealthService healthService,
    CancellationToken ct) =>
{
    var status = await healthService.CheckAllServicesAsync(ct);
    return Results.Ok(status);
}).WithTags("Admin").WithName("GetPlatformHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
