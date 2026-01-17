using System.Threading.RateLimiting;
using Dhadgar.Secrets;
using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Audit;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Readiness;
using Dhadgar.Secrets.Services;
using SecretsHello = Dhadgar.Secrets.Hello;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Dhadgar Secrets API",
        Version = "v1",
        Description = "Secret storage and rotation for Meridian Console"
    });
});

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

// Register authorization and audit services
builder.Services.AddSingleton<ISecretsAuthorizationService, SecretsAuthorizationService>();
builder.Services.AddSingleton<ISecretsAuditLogger, SecretsAuditLogger>();

// Rate limiting configuration
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Default policy for secrets read operations
    options.AddFixedWindowLimiter("SecretsRead", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 10;
    });

    // Stricter limit for write operations
    options.AddFixedWindowLimiter("SecretsWrite", limiterOptions =>
    {
        limiterOptions.PermitLimit = 20;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 5;
    });

    // Very strict limit for rotation operations
    options.AddFixedWindowLimiter("SecretsRotate", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2;
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? (int)retry.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests. Please try again later.",
            retryAfterSeconds = retryAfter
        }, token);
    };
});

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
app.UseRateLimiter();

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
