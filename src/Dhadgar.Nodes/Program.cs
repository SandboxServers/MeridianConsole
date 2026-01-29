using Dhadgar.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Consumers;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Endpoints;
using Dhadgar.Nodes.Observability;
using Dhadgar.Nodes.Services;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Nodes API",
    description: "Node inventory, health, and capacity management for Meridian Console");

// Configure Nodes service options
builder.Services.Configure<NodesOptions>(
    builder.Configuration.GetSection(NodesOptions.SectionName));

builder.Services.AddDbContext<NodesDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Health checks for dependencies (PostgreSQL only - MassTransit handles RabbitMQ health checks)
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("Postgres") ?? string.Empty,
        name: "postgres",
        tags: ["db", "ready"]);

// Register services
builder.Services.AddScoped<INodeService, NodeService>();
builder.Services.AddScoped<IEnrollmentTokenService, EnrollmentTokenService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IHeartbeatService, HeartbeatService>();
builder.Services.AddScoped<IHealthScoringService, HealthScoringService>();
builder.Services.AddScoped<ICapacityReservationService, CapacityReservationService>();

// Register Certificate Authority services
builder.Services.AddSingleton<ICaStorageProvider, LocalFileCaStorageProvider>();
builder.Services.AddSingleton<ICertificateAuthorityService, CertificateAuthorityService>();

// Register mTLS authentication services
builder.Services.AddMtlsAuthentication(builder.Configuration);

// Register audit services
builder.Services.AddScoped<IAuditContextAccessor, AuditContextAccessor>();
builder.Services.AddScoped<IAuditService, AuditService>();

// Register TimeProvider for testability
builder.Services.AddSingleton(TimeProvider.System);

// Register background services
builder.Services.AddHostedService<StaleNodeDetectionService>();
builder.Services.AddHostedService<AuditLogCleanupService>();
builder.Services.AddHostedService<ReservationExpiryService>();

// Configure MassTransit
var rabbitHost = builder.Configuration.GetConnectionString("RabbitMqHost") ?? "localhost";
var rabbitUsername = builder.Configuration["RabbitMq:Username"] ?? "dhadgar";
var rabbitPassword = builder.Configuration["RabbitMq:Password"] ?? "dhadgar";

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // Register consumers for node and capacity events
    x.AddConsumer<CapacityReservedConsumer>();
    x.AddConsumer<CapacityReleasedConsumer>();
    x.AddConsumer<CapacityReservationExpiredConsumer>();
    x.AddConsumer<NodeDegradedConsumer>();
    x.AddConsumer<NodeOfflineConsumer>();

    // Configure MassTransit health checks with "ready" tag for /readyz endpoint
    x.ConfigureHealthCheckOptions(options =>
    {
        options.Name = "masstransit";
        options.Tags.Add("ready");
        options.Tags.Add("messaging");
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUsername);
            h.Password(rabbitPassword);
        });

        cfg.ConfigureEndpoints(context);
    });
});

// Configure authorization
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationHandler, TenantScopedHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("TenantScoped", policy =>
    {
        // Require authenticated user and tenant scope validation
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new TenantScopedRequirement());
    })
    .AddPolicy("AgentPolicy", policy =>
    {
        // Require authenticated user (via mTLS certificate in production)
        policy.RequireAuthenticatedUser();
    });

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
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Nodes");

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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddMeter(NodesMetrics.MeterName); // Custom Nodes service metrics

        if (otlpUri is not null)
        {
            metrics.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// mTLS authentication middleware for agent endpoints
app.UseMtlsAuthentication();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

// Initialize the Certificate Authority on startup
try
{
    var caService = app.Services.GetRequiredService<ICertificateAuthorityService>();
    await caService.InitializeAsync();
    app.Logger.LogInformation("Certificate Authority initialized successfully");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to initialize Certificate Authority. Certificate operations will fail.");
}

// Map endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Nodes", message = Dhadgar.Nodes.Hello.Message }))
    .WithTags("Health").WithName("NodesServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Nodes.Hello.Message))
    .WithTags("Health").WithName("NodesHello");
app.MapDhadgarDefaultEndpoints();

// Map API endpoints
NodesEndpoints.Map(app);
EnrollmentEndpoints.Map(app);
AgentEndpoints.Map(app);
AuditEndpoints.Map(app);
ReservationEndpoints.Map(app);

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
