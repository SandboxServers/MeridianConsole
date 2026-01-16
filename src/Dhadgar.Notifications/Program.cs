using Dhadgar.Notifications;
using Dhadgar.Notifications.Data;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Notifications API",
    description: "Email, Discord, and webhook notifications for Meridian Console");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

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

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Notifications", message = Hello.Message }))
    .WithTags("Health").WithName("NotificationsServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("NotificationsHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Notifications", status = "ok" }))
    .WithTags("Health").WithName("NotificationsHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
