using Dhadgar.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Audit;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Servers API",
    description: "Game server lifecycle management for Meridian Console");

// Audit infrastructure for compliance logging
builder.Services.AddAuditInfrastructure<ServersDbContext>();

builder.Services.AddDbContext<ServersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline with audit enabled for server lifecycle changes
app.UseDhadgarMiddleware(new DhadgarServiceOptions
{
    EnableAuditMiddleware = true
});

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<ServersDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Servers", Dhadgar.Servers.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
