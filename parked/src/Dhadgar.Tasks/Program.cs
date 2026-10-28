using Dhadgar.Tasks;
using Dhadgar.Tasks.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Tasks API",
    description: "Orchestration and background job management for Meridian Console");

builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline (correlation, tenant enrichment, problem details, request logging)
app.UseDhadgarMiddleware();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<TasksDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Tasks", Dhadgar.Tasks.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
