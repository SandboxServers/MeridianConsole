using Dhadgar.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Mods API",
    description: "Mod registry and versioning for Meridian Console");

builder.Services.AddDbContext<ModsDbContext>(options =>
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
        var db = scope.ServiceProvider.GetRequiredService<ModsDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Mods", message = Dhadgar.Mods.Hello.Message }))
    .WithTags("Health").WithName("ModsServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Mods.Hello.Message))
    .WithTags("Health").WithName("ModsHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
