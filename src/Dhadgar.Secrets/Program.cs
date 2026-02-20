using System.Threading.RateLimiting;
using Dhadgar.Secrets;
using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Audit;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Infrastructure;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Readiness;
using Dhadgar.Secrets.Services;
using SecretsHello = Dhadgar.Secrets.Hello;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Dhadgar Secrets API";
        document.Info.Version = "v1";
        document.Info.Description = "Secret storage and rotation for Meridian Console";

        // Add JWT Bearer authentication security scheme
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter your JWT token"
        };

        // Apply Bearer authentication globally to all operations
        document.Security ??= [];
        var schemeRef = new OpenApiSecuritySchemeReference("Bearer", document);
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [schemeRef] = new List<string>()
        });

        return Task.CompletedTask;
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
        var internalBaseUrl = builder.Configuration["Auth:InternalBaseUrl"]; // e.g., http://identity:8080

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

        // For local dev/Docker: use a document retriever that rewrites external URLs to internal
        // This is needed because the OIDC metadata returns external URLs for jwks_uri
        // which containers can't reach in local Docker environments
        if (!string.IsNullOrWhiteSpace(internalBaseUrl) && !string.IsNullOrWhiteSpace(issuer))
        {
            var docRetriever = new UrlRewritingDocumentRetriever(issuer, internalBaseUrl)
            {
                RequireHttps = !builder.Environment.IsDevelopment()
            };

            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                metadataAddress ?? $"{issuer}.well-known/openid-configuration",
                new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
                docRetriever);
        }
    });

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks()
    .AddCheck<SecretsReadinessCheck>("secrets_ready", tags: ["ready"]);

// Register authorization and audit services
builder.Services.AddSingleton<IBreakGlassNonceTracker, InMemoryBreakGlassNonceTracker>();
builder.Services.AddSingleton<ISecretsAuthorizationService, SecretsAuthorizationService>();
builder.Services.AddSingleton<ISecretsAuditLogger, SecretsAuditLogger>();

// Error handling infrastructure (RFC 9457 Problem Details)
builder.Services.AddDhadgarErrorHandling();

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

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests. Please try again later.",
            retryAfterSeconds = retryAfter
        }, token);
    };
});

// HttpClient for WIF token requests
builder.Services.AddHttpClient("IdentityWif", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// WIF credential provider (used by KeyVaultSecretProvider)
builder.Services.AddSingleton<IWifCredentialProvider, WifCredentialProvider>();

var useDevelopmentProvider = builder.Configuration.GetValue<bool>("Secrets:UseDevelopmentProvider");

if (useDevelopmentProvider && builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ISecretProvider, DevelopmentSecretProvider>();
}
else
{
    // Default to Key Vault for non-dev environments or when explicitly configured.
    // Uses WIF for authentication if configured, otherwise DefaultAzureCredential.
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

// Certificate provider (always use Key Vault)
builder.Services.AddSingleton<ICertificateProvider, KeyVaultCertificateProvider>();

// Key Vault manager for vault CRUD operations
builder.Services.AddSingleton<IKeyVaultManager, AzureKeyVaultManager>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Dhadgar middleware pipeline
// Note: Using individual middleware instead of UseDhadgarMiddleware because
// we need UseDhadgarErrorHandling for proper ProblemDetails customization
app.UseMiddleware<Dhadgar.ServiceDefaults.Middleware.CorrelationMiddleware>();
app.UseDhadgarErrorHandling();  // RFC 9457 Problem Details with trace context
app.UseMiddleware<Dhadgar.ServiceDefaults.Middleware.RequestLoggingMiddleware>();
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
