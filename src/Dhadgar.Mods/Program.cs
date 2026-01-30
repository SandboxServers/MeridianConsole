using Dhadgar.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Extensions;
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

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<ModsDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Mods", Dhadgar.Mods.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
