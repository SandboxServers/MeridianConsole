using Dhadgar.Console;
using Dhadgar.Console.Data;
using Dhadgar.Console.Endpoints;
using Dhadgar.Console.Hubs;
using Dhadgar.Console.Services;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Extensions;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Dhadgar.ServiceDefaults.Swagger;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults(HealthCheckDependencies.Postgres | HealthCheckDependencies.Redis);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Console API",
    description: "Real-time console streaming via SignalR for Meridian Console");

// Configure Console service options
builder.Services.AddOptions<ConsoleOptions>()
    .Bind(builder.Configuration.GetSection(ConsoleOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Database
builder.Services.AddDbContext<ConsoleDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Redis for distributed caching, SignalR backplane, and atomic set operations
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

// Register IConnectionMultiplexer for atomic Redis operations (SADD/SREM)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "console:";
});

// SignalR with Redis backplane
builder.Services.AddSignalR(hubOptions =>
    {
        hubOptions.MaximumReceiveMessageSize = 64 * 1024; // 64 KB
        hubOptions.StreamBufferCapacity = 16;
        hubOptions.EnableDetailedErrors = builder.Environment.IsDevelopment();
    })
    .AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("console");
    });

// Register TimeProvider for testability
builder.Services.AddSingleton(TimeProvider.System);

// Register services
builder.Services.AddSingleton<IConsoleSessionManager, ConsoleSessionManager>();
builder.Services.AddScoped<IConsoleHistoryService, ConsoleHistoryService>();
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();

// Server ownership validation via Servers API
builder.Services.AddHttpClient<IServerOwnershipValidator, ServerOwnershipValidator>(client =>
{
    var serversBaseUrl = builder.Configuration["Services:Servers:BaseUrl"]
        ?? throw new InvalidOperationException("'Services:Servers:BaseUrl' configuration is required.");
    client.BaseAddress = new Uri(serversBaseUrl);
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Configure RabbitMQ options
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
    x.AddEntityFrameworkOutbox<ConsoleDbContext>(o =>
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

// Configure authentication and authorization with tenant-scoped validation
builder.Services.AddTenantScopedAuthorization(builder.Configuration, builder.Environment);

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
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Console");

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
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("MassTransit");

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
            .AddProcessInstrumentation();

        if (otlpUri is not null)
        {
            metrics.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline (correlation, tenant enrichment, request logging)
app.UseDhadgarMiddleware();

// Authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Auto-migrate database in development
await app.AutoMigrateDatabaseAsync<ConsoleDbContext>();

// Map endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Console", message = Dhadgar.Console.Hello.Message }))
    .WithTags("Health").WithName("ConsoleServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Console.Hello.Message))
    .WithTags("Health").WithName("ConsoleHello");

app.MapHub<ConsoleHub>("/hubs/console")
    .RequireAuthorization();
app.MapDhadgarDefaultEndpoints();
ConsoleEndpoints.Map(app);

await app.RunAsync();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
