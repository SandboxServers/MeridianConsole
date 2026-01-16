using Dhadgar.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Nodes API",
    description: "Node inventory, health, and capacity management for Meridian Console");

builder.Services.AddDbContext<NodesDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Nodes", message = Hello.Message }))
    .WithTags("Health").WithName("NodesServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("NodesHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Nodes", status = "ok" }))
    .WithTags("Health").WithName("NodesHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
