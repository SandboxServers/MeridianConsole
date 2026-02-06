using Dhadgar.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Endpoints;
using Dhadgar.Servers.Services;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Audit;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Dhadgar.ServiceDefaults.Swagger;
using Dhadgar.ServiceDefaults.Tracing;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults(HealthCheckDependencies.Postgres);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Servers API",
    description: "Game server lifecycle management for Meridian Console");

// Audit infrastructure for compliance logging
builder.Services.AddAuditInfrastructure<ServersDbContext>();

// Configure Servers service options with validation
builder.Services.AddOptions<ServersOptions>()
    .Bind(builder.Configuration.GetSection(ServersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<ServersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Register services
builder.Services.AddScoped<IServerService, ServerService>();
builder.Services.AddScoped<IServerLifecycleService, ServerLifecycleService>();
builder.Services.AddScoped<IServerTemplateService, ServerTemplateService>();

// Register TimeProvider for testability
builder.Services.AddSingleton(TimeProvider.System);

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Configure RabbitMQ options with validation
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Configure MassTransit
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // Configure MassTransit health checks
    x.ConfigureHealthCheckOptions(options =>
    {
        options.Name = "masstransit";
        options.Tags.Add("ready");
        options.Tags.Add("messaging");
    });

    // Configure Entity Framework Core outbox for transactional messaging
    x.AddEntityFrameworkOutbox<ServersDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.DisableInboxCleanupService();
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

// Configure authorization with tenant-scoped validation
builder.Services.AddTenantScopedAuthorization();

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

// Dhadgar middleware pipeline with audit enabled for server lifecycle changes
app.UseDhadgarMiddleware(new DhadgarServiceOptions
{
    EnableAuditMiddleware = true
});

// Authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<ServersDbContext>();

// Map endpoints
app.MapServiceInfoEndpoints("Dhadgar.Servers", Dhadgar.Servers.Hello.Message);
app.MapDhadgarDefaultEndpoints();

ServersEndpoints.Map(app);
ServerLifecycleEndpoints.Map(app);
ServerTemplatesEndpoints.Map(app);

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
