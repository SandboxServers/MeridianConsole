using Dhadgar.Secrets;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Readiness;
using Dhadgar.Secrets.Services;
using SecretsHello = Dhadgar.Secrets.Hello;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure options
builder.Services.Configure<SecretsOptions>(builder.Configuration.GetSection("Secrets"));
builder.Services.Configure<SecretsReadinessOptions>(builder.Configuration.GetSection("Readiness"));

// Add memory cache for secret caching
builder.Services.AddMemoryCache();

// Authentication/authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Auth:Issuer"];
        var metadataAddress = builder.Configuration["Auth:MetadataAddress"];

        options.Authority = issuer;
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        // Support separate MetadataAddress for internal service discovery in Docker/K8s
        // Token issuer remains external URL but JWKS is fetched from internal address
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer, // Explicitly set to match token issuer
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(
                builder.Configuration.GetValue<int?>("Auth:ClockSkewSeconds") ?? 60)
        };
        options.RefreshOnIssuerKeyNotFound = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks()
    .AddCheck<SecretsReadinessCheck>("secrets_ready", tags: ["ready"]);

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
var otlpUri = !string.IsNullOrWhiteSpace(otlpEndpoint) ? new Uri(otlpEndpoint) : null;
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Secrets");

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
        // OTLP export requires explicit endpoint configuration; skipped when not set
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
        // OTLP export requires explicit endpoint configuration; skipped when not set
    });

var useDevelopmentProvider = builder.Configuration.GetValue<bool>("Secrets:UseDevelopmentProvider");

if (useDevelopmentProvider && builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ISecretProvider, DevelopmentSecretProvider>();
}
else
{
    // Default to Key Vault for non-dev environments or when explicitly configured.
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

// Certificate provider (always use Key Vault)
builder.Services.AddSingleton<ICertificateProvider, KeyVaultCertificateProvider>();

// Key Vault manager for vault CRUD operations
builder.Services.AddSingleton<IKeyVaultManager, AzureKeyVaultManager>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Standard service endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Secrets", message = SecretsHello.Message }));
app.MapGet("/hello", () => Results.Text(SecretsHello.Message));
app.MapDhadgarDefaultEndpoints();

// Secrets API endpoints
app.MapSecretsEndpoints();           // Read operations
app.MapSecretWriteEndpoints();       // Write operations (set, rotate, delete)
app.MapCertificateEndpoints();       // Certificate management
app.MapKeyVaultEndpoints();          // Key Vault management

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
