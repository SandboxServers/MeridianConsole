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
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Swagger;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

// Add custom Nodes metrics
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
{
    metrics.AddMeter(NodesMetrics.MeterName);
});

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Nodes API",
    description: "Node inventory, health, and capacity management for Meridian Console");

// Configure Nodes service options with validation
builder.Services.AddOptions<NodesOptions>()
    .Bind(builder.Configuration.GetSection(NodesOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register the custom validator for cross-property validation
builder.Services.AddSingleton<IValidateOptions<NodesOptions>, NodesOptions>();

builder.Services.AddDbContext<NodesDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Note: PostgreSQL health check is already registered by AddDhadgarServiceDefaults
// with HealthCheckDependencies.Postgres - no need to add it again here.

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

// Configure RabbitMQ options with validation
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Configure MassTransit (no consumers in core - just publisher capability)
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

    // Configure Entity Framework Core outbox for transactional messaging
    // This ensures DB commits and event publication are atomic
    x.AddEntityFrameworkOutbox<NodesDbContext>(o =>
    {
        // Use PostgreSQL for the outbox
        o.UsePostgres();

        // Disable inbox for now (no consumers in core)
        o.DisableInboxCleanupService();

        // Query delay and delivery delay settings
        o.QueryDelay = TimeSpan.FromSeconds(1);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitOptions = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        cfg.Host(rabbitOptions.Host, rabbitOptions.VirtualHost, h =>
        {
            h.Username(rabbitOptions.Username);
            h.Password(rabbitOptions.Password);
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
        // Require org_id claim for tenant-scoped operations
        policy.RequireClaim("org_id");
    })
    .AddPolicy("AgentPolicy", policy =>
    {
        // Require authenticated user (via mTLS certificate in production)
        policy.RequireAuthenticatedUser();
    });

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline (correlation, tenant enrichment, problem details, request logging)
app.UseDhadgarMiddleware();

// Authentication and authorization
app.UseAuthentication();
// mTLS authentication middleware for agent endpoints
app.UseMtlsAuthentication();
app.UseAuthorization();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<NodesDbContext>();

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
app.MapServiceInfoEndpoints("Dhadgar.Nodes", Dhadgar.Nodes.Hello.Message);
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
