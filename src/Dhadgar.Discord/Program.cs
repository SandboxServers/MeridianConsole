using Dhadgar.Discord;
using Dhadgar.Discord.Bot;
using Dhadgar.Discord.Commands;
using Dhadgar.Discord.Consumers;
using Dhadgar.Discord.Data;
using Dhadgar.Discord.Services;
using Dhadgar.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Discord", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () =>
{
    var bot = app.Services.GetRequiredService<DiscordBotService>();
    var botStatus = bot.Client.ConnectionState.ToString();
    return Results.Ok(new { service = "Dhadgar.Discord", status = "ok", botStatus });
});

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
});

// Platform health check endpoint
app.MapGet("/api/v1/platform/health", async (
    IPlatformHealthService healthService,
    CancellationToken ct) =>
{
    var status = await healthService.CheckAllServicesAsync(ct);
    return Results.Ok(status);
});

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
