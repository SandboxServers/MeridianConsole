using Dhadgar.Messaging;
using Dhadgar.Notifications;
using Dhadgar.Notifications.Consumers;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

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
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

// Basic endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Notifications", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Notifications", status = "ok" }));

// Admin endpoint to view notification logs
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

    var logs = await query
        .OrderByDescending(l => l.CreatedAtUtc)
        .Take(limit ?? 50)
        .ToListAsync(ct);

    return Results.Ok(logs);
});

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
