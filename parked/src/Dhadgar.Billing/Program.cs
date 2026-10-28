using Dhadgar.Billing;
using Dhadgar.Billing.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Billing API",
    description: "Billing, subscriptions, and usage metering for Meridian Console");

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline (correlation, tenant enrichment, request logging)
app.UseDhadgarMiddleware();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<BillingDbContext>();

app.MapServiceInfoEndpoints("Dhadgar.Billing", Dhadgar.Billing.Hello.Message);
app.MapDhadgarDefaultEndpoints();

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
