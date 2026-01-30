using Dhadgar.Billing;
using Dhadgar.Billing.Data;
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
    title: "Dhadgar Billing API",
    description: "Billing, subscriptions, and usage metering for Meridian Console");

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<BillingDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Billing", Dhadgar.Billing.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
