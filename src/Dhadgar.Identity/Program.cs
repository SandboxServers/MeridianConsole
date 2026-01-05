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

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultSignInScheme = AuthSchemes.External;
});

authenticationBuilder.AddCookie(AuthSchemes.External, options =>
{
    options.Cookie.Name = "__Host-dhadgar-external";
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
    OAuthProviderRegistry.ConfigureProviders(authenticationBuilder, builder.Configuration, builder.Environment, AuthSchemes.External);
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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

        // Default: no limiter
        return RateLimitPartition.GetNoLimiter<string>("nolimit");
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

        var vaultUri = builder.Configuration["Auth:KeyVault:VaultUri"];
        var signingCertName = builder.Configuration["Auth:KeyVault:SigningKeyName"];
        var encryptionCertName = builder.Configuration["Auth:KeyVault:EncryptionCertName"];

        if (builder.Environment.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(vaultUri) ||
                string.IsNullOrWhiteSpace(signingCertName) ||
                string.IsNullOrWhiteSpace(encryptionCertName))
            {
                throw new InvalidOperationException("Key Vault certificate configuration is required in production.");
            }

            var credential = new DefaultAzureCredential();
            var certClient = new CertificateClient(new Uri(vaultUri), credential);

            try
            {
                var signingCert = certClient.GetCertificate(signingCertName).Value;
                var encryptionCert = certClient.GetCertificate(encryptionCertName).Value;

                options.AddSigningCertificate(new X509Certificate2(signingCert.Cer))
                    .AddEncryptionCertificate(new X509Certificate2(encryptionCert.Cer));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load OpenIddict certificates from Key Vault.", ex);
            }
        }
        else
        {
            options.AddEphemeralEncryptionKey();
            options.AddEphemeralSigningKey();
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
        else
        {
            tracing.AddOtlpExporter();
        }
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
