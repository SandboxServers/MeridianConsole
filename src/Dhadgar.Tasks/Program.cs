using Dhadgar.Tasks;
using Dhadgar.Tasks.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Note: RabbitMq health check removed until MassTransit is configured
builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Tasks API",
    description: "Orchestration and background job management for Meridian Console");

builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

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
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Tasks", message = Dhadgar.Tasks.Hello.Message }))
    .WithTags("Health").WithName("TasksServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Tasks.Hello.Message))
    .WithTags("Health").WithName("TasksHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
