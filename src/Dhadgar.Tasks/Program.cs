using Dhadgar.Tasks;
using Dhadgar.Tasks.Data;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Tasks API",
    description: "Orchestration and background job management for Meridian Console");

builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

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

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Tasks", message = Hello.Message }))
    .WithTags("Health").WithName("TasksServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("TasksHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Tasks", status = "ok" }))
    .WithTags("Health").WithName("TasksHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
