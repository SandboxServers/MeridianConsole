using Dhadgar.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Audit;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Logging;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using Dhadgar.ServiceDefaults.Tracing;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Servers API",
    description: "Game server lifecycle management for Meridian Console");

// Add Dhadgar logging infrastructure with PII redaction
builder.Services.AddDhadgarLogging();
builder.Logging.AddDhadgarLogging("Dhadgar.Servers", builder.Configuration);

// Audit infrastructure for compliance logging
builder.Services.AddAuditInfrastructure<ServersDbContext>();

builder.Services.AddDbContext<ServersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// OpenTelemetry configuration
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
Uri? otlpUri = null;
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsedUri))
    {
        otlpUri = parsedUri;
    }
}
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Servers");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;

    if (otlpUri is not null)
    {
        options.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
    }
});

// Tracing (centralized with EF Core instrumentation)
builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Servers");

// Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();

        if (otlpUri is not null)
        {
            metrics.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware (includes Correlation, TenantEnrichment, and RequestLogging)
app.UseDhadgarMiddleware();
app.UseMiddleware<ProblemDetailsMiddleware>();

// Audit middleware - MUST run after authentication
// Currently skips all requests (no auth configured yet); will capture authenticated requests once auth is added
app.UseAuditMiddleware();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServersDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Servers", message = Dhadgar.Servers.Hello.Message }))
    .WithTags("Health").WithName("ServersServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Servers.Hello.Message))
    .WithTags("Health").WithName("ServersHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
