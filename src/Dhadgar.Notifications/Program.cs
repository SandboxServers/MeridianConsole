using Dhadgar.Notifications;
using Dhadgar.Notifications.Alerting;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Discord;
using Dhadgar.Notifications.Email;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Note: RabbitMq health check removed - MassTransit not yet configured
builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Notifications API",
    description: "Email, Discord, and webhook notifications for Meridian Console");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Configure alerting options
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// Register HTTP client for Discord webhook
builder.Services.AddHttpClient<IDiscordWebhook, DiscordWebhookClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Register email sender (scoped - creates new SmtpClient per call)
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Register throttler with configurable window (minimum 1 minute to prevent misconfiguration)
var throttleMinutes = builder.Configuration.GetValue<int>("Alerting:ThrottleWindowMinutes", 5);
var validatedThrottleMinutes = Math.Max(1, throttleMinutes);
builder.Services.AddSingleton(new AlertThrottler(TimeSpan.FromMinutes(validatedThrottleMinutes)));

// Register alert dispatcher (scoped to match transient Discord and scoped Email dependencies)
builder.Services.AddScoped<IAlertDispatcher, AlertDispatcher>();

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<NotificationsDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Notifications", Dhadgar.Notifications.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
