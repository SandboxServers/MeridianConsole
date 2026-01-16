using Dhadgar.Files;
using Dhadgar.Files.Data;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Files API",
    description: "File metadata and transfer orchestration for Meridian Console");

builder.Services.AddDbContext<FilesDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Files", message = Hello.Message }))
    .WithTags("Health").WithName("FilesServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("FilesHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Files", status = "ok" }))
    .WithTags("Health").WithName("FilesHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
