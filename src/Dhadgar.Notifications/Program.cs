using Dhadgar.Notifications;
using Dhadgar.Notifications.Alerting;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Discord;
using Dhadgar.Notifications.Email;
using Dhadgar.ServiceDefaults;
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

// Register throttler with configurable window
var throttleMinutes = builder.Configuration.GetValue<int>("Alerting:ThrottleWindowMinutes", 5);
builder.Services.AddSingleton(new AlertThrottler(TimeSpan.FromMinutes(throttleMinutes)));

// Register alert dispatcher (scoped to match transient Discord and scoped Email dependencies)
builder.Services.AddScoped<IAlertDispatcher, AlertDispatcher>();

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
