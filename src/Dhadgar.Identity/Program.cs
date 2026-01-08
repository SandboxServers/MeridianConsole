using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Dhadgar.Identity;
using Dhadgar.Identity.Authentication;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Endpoints;
using Dhadgar.Identity.OAuth;
using Dhadgar.Identity.Options;
using Dhadgar.Identity.Services;
using Dhadgar.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Keys;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using IdentityHello = Dhadgar.Identity.Hello;
using Dhadgar.Identity.Data.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<ExchangeTokenOptions>(builder.Configuration.GetSection("Auth:Exchange"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Redis connection string is required.");
    }

    return ConnectionMultiplexer.Connect(connectionString);
});

// ASP.NET Core Identity (no passwords; external + Better Auth exchange)
builder.Services.AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 12; // unused for Better Auth, but set sane default
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddSignInManager();

builder.Services.AddSingleton<IExchangeTokenValidator, ExchangeTokenValidator>();
builder.Services.AddSingleton<IExchangeTokenReplayStore, RedisExchangeTokenReplayStore>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<TokenExchangeService>();
builder.Services.AddScoped<ILinkedAccountService, LinkedAccountService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddScoped<OrganizationSwitchService>();

// Memory cache for webhook secret caching
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWebhookSecretProvider, WebhookSecretProvider>();

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultSignInScheme = AuthSchemes.External;
});

authenticationBuilder.AddCookie(AuthSchemes.External, options =>
{
    // __Host- prefix requires: Secure=true, Path="/", no Domain attribute
    options.Cookie.Name = "__Host-dhadgar-external";
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});

if (builder.Environment.IsEnvironment("Testing"))
{
    OAuthProviderRegistry.ConfigureMockProviders(authenticationBuilder, AuthSchemes.External);
}
else
{
    // Load gaming OAuth secrets from Secrets Service at startup
    var secretsServiceUrl = builder.Configuration["SecretsService:Url"] ?? "http://localhost:5000";
    using var oauthSecrets = new OAuthSecretProvider(new Uri(secretsServiceUrl));
    oauthSecrets.LoadSecretsAsync().GetAwaiter().GetResult();

    OAuthProviderRegistry.ConfigureProviders(
        authenticationBuilder,
        builder.Configuration,
        builder.Environment,
        oauthSecrets,
        AuthSchemes.External);
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"auth:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        if (path == "/exchange")
        {
            return RateLimitPartition.GetFixedWindowLimiter($"exchange:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path == "/connect/token" || path == "/refresh")
        {
            return RateLimitPartition.GetFixedWindowLimiter($"token:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.StartsWith("/connect/authorize") || path.StartsWith("/oauth/"))
        {
            return RateLimitPartition.GetSlidingWindowLimiter($"authcb:{ip}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
        }

        if (path.StartsWith("/webhooks/better-auth"))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"webhook:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var orgId = context.User.FindFirst("org_id")?.Value;
            if (!string.IsNullOrWhiteSpace(orgId))
            {
                return RateLimitPartition.GetFixedWindowLimiter($"tenant:{orgId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            }

            var clientType = context.User.FindFirst("client_type")?.Value;
            if (string.Equals(clientType, "agent", StringComparison.OrdinalIgnoreCase))
            {
                var agentId = context.User.FindFirst("sub")?.Value ?? ip;
                return RateLimitPartition.GetFixedWindowLimiter($"agent:{agentId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 500,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            }
        }

        // Default: conservative limit for unmatched/unauthenticated requests
        return RateLimitPartition.GetFixedWindowLimiter($"default:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddMassTransit(x =>
    {
        x.UsingInMemory((ctx, cfg) =>
        {
            cfg.ConfigureEndpoints(ctx);
        });
    });
}
else
{
    builder.Services.AddDhadgarMessaging(builder.Configuration);
}

builder.Services.AddScoped<IIdentityEventPublisher, IdentityEventPublisher>();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<IdentityDbContext>();
    })
    .AddServer(options =>
    {
        var issuer = builder.Configuration["Auth:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            options.SetIssuer(new Uri(issuer));
        }

        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetUserInfoEndpointUris("connect/userinfo")
            .SetIntrospectionEndpointUris("connect/introspect")
            .SetRevocationEndpointUris("connect/revocation")
            .SetJsonWebKeySetEndpointUris(".well-known/jwks.json");

        options.AllowAuthorizationCodeFlow()
            .RequireProofKeyForCodeExchange()
            .AllowClientCredentialsFlow()
            .AllowRefreshTokenFlow();

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "servers:read",
            "servers:write",
            "nodes:manage",
            "billing:read");

        if (builder.Environment.IsEnvironment("Testing"))
        {
            // Avoid Key Vault dependency in tests.
            options.AddDevelopmentSigningCertificate()
                .AddDevelopmentEncryptionCertificate();
        }
        else
        {
        var vaultUri = builder.Configuration["Auth:KeyVault:VaultUri"];
        var signingCertName = builder.Configuration["Auth:KeyVault:SigningCertName"];
        var encryptionCertName = builder.Configuration["Auth:KeyVault:EncryptionCertName"];

        // Always use Key Vault for certificates (including local dev)
        if (string.IsNullOrWhiteSpace(vaultUri) ||
            string.IsNullOrWhiteSpace(signingCertName) ||
            string.IsNullOrWhiteSpace(encryptionCertName))
        {
            throw new InvalidOperationException(
                "Key Vault certificate configuration is required. " +
                "Configure Auth:KeyVault:VaultUri, SigningCertName, and EncryptionCertName. " +
                "Ensure you are logged in via 'az login' for local development.");
        }

        var credential = new DefaultAzureCredential();
        var certClient = new CertificateClient(new Uri(vaultUri), credential);

        try
        {
            // DownloadCertificate returns X509Certificate2 with private key (required for signing)
            // GetCertificate only returns public cert metadata which cannot be used for signing
            var signingCert = certClient.DownloadCertificate(signingCertName);
            var encryptionCert = certClient.DownloadCertificate(encryptionCertName);

            options.AddSigningCertificate(signingCert)
                .AddEncryptionCertificate(encryptionCert);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load OpenIddict certificates from Key Vault ({vaultUri}). " +
                $"Ensure you are logged in via 'az login' and have Key Vault Certificates User role. " +
                $"Error: {ex.Message}", ex);
        }
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Dhadgar.Identity"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
        // OTLP export requires explicit endpoint configuration; skipped when not set
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Optional: apply EF Core migrations automatically during local/dev runs.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Keep startup resilient for first-run dev scenarios.
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Identity", message = IdentityHello.Message }));
app.MapGet("/hello", () => Results.Text(IdentityHello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Identity", status = "ok" }));
app.MapPost("/exchange", TokenExchangeEndpoint.Handle);
OAuthEndpoints.Map(app);
OrganizationEndpoints.Map(app);
MembershipEndpoints.Map(app);
WebhookEndpoint.Map(app);

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
