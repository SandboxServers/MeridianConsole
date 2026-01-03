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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using IdentityHello = Dhadgar.Identity.Hello;

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

    options.AddPolicy("token-exchange", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
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

        if (builder.Environment.IsProduction())
        {
            var signingPath = builder.Configuration["OpenIddict:SigningCertificatePath"];
            var signingPassword = builder.Configuration["OpenIddict:SigningCertificatePassword"];
            var encryptionPath = builder.Configuration["OpenIddict:EncryptionCertificatePath"];
            var encryptionPassword = builder.Configuration["OpenIddict:EncryptionCertificatePassword"];

            if (string.IsNullOrWhiteSpace(signingPath) || string.IsNullOrWhiteSpace(encryptionPath))
            {
                throw new InvalidOperationException("OpenIddict certificates are required in production.");
            }

            var signingCert = new X509Certificate2(signingPath, signingPassword);
            var encryptionCert = new X509Certificate2(encryptionPath, encryptionPassword);

            options.AddSigningCertificate(signingCert)
                .AddEncryptionCertificate(encryptionCert);
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

app.UseRateLimiter();
app.UseAuthentication();
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
app.MapPost("/exchange", TokenExchangeEndpoint.Handle)
    .RequireRateLimiting("token-exchange");
OAuthEndpoints.Map(app);
OrganizationEndpoints.Map(app);
MembershipEndpoints.Map(app);
WebhookEndpoint.Map(app);

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
