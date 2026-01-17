using Dhadgar.Billing;
using Dhadgar.Billing.Data;
using Dhadgar.ServiceDefaults.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Billing API",
    description: "Billing, subscriptions, and usage metering for Meridian Console");

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

app.UseMeridianSwagger();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Billing", message = Hello.Message }))
    .WithTags("Health").WithName("BillingServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("BillingHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Billing", status = "ok" }))
    .WithTags("Health").WithName("BillingHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
