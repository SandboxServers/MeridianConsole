using System.Net;
using Dhadgar.Gateway;
using Dhadgar.Gateway.Endpoints;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Options;
using Dhadgar.Gateway.Readiness;
using GatewayHello = Dhadgar.Gateway.Hello;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

// Add CORS support
builder.Services.AddMeridianConsoleCors(builder.Configuration);
builder.Services.Configure<ReadyzOptions>(builder.Configuration.GetSection("Readyz"));
builder.Services.AddHealthChecks()
    .AddCheck<YarpReadinessCheck>("yarp_ready", tags: ["ready"]);

// Authentication/authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Issuer"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(
                builder.Configuration.GetValue<int?>("Auth:ClockSkewSeconds") ?? 60)
        };
        options.RefreshOnIssuerKeyNotFound = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantScoped", policy =>
    {
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy("Agent", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("client_type", "agent");
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// HttpClient for diagnostics endpoints
builder.Services.AddHttpClient();

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
Uri? otlpUri = null;
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out otlpUri))
    {
        Console.WriteLine($"Warning: Invalid OpenTelemetry:OtlpEndpoint '{otlpEndpoint}'. OTLP export disabled.");
        otlpUri = null;
    }
}
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Gateway");

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

// Rate limiting (global + route policies)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("Global", _ =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:Global:PermitLimit") ?? 1000,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:Global:WindowSeconds") ?? 60),
                QueueLimit = 0
            }));

    options.AddPolicy("Auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:Auth:PermitLimit") ?? 30,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:Auth:WindowSeconds") ?? 60),
                QueueLimit = 0
            }));

    // SECURITY: Only use JWT claim for tenant identification - never trust client headers
    // Unauthenticated requests fall back to IP-based limiting
    options.AddPolicy("PerTenant", httpContext =>
    {
        var tenantId = httpContext.User.FindFirst("org_id")?.Value
                       ?? httpContext.Connection.RemoteIpAddress?.ToString()
                       ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"tenant:{tenantId}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerTenant:PermitLimit") ?? 100,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerTenant:WindowSeconds") ?? 60),
                QueueLimit = 0
            });
    });

    options.AddPolicy("PerAgent", httpContext =>
    {
        var agentId = httpContext.User.FindFirst("sub")?.Value
                      ?? httpContext.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"agent:{agentId}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerAgent:PermitLimit") ?? 500,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerAgent:WindowSeconds") ?? 60),
                QueueLimit = 0
            });
    });
});

// Configure ForwardedHeaders to trust Cloudflare proxy
// SECURITY: Only trust X-Forwarded-* headers from known Cloudflare IP ranges
// Without this, attackers could spoof CF-Connecting-IP or X-Forwarded-For headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2; // Cloudflare + potential internal proxy

    // Cloudflare IPv4 ranges (https://www.cloudflare.com/ips-v4)
    var cloudflareIpv4Ranges = new[]
    {
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "172.64.0.0/13",
        "131.0.72.0/22"
    };

    // Cloudflare IPv6 ranges (https://www.cloudflare.com/ips-v6)
    var cloudflareIpv6Ranges = new[]
    {
        "2400:cb00::/32",
        "2606:4700::/32",
        "2803:f800::/32",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2c0f:f248::/32"
    };

    foreach (var range in cloudflareIpv4Ranges)
    {
        if (System.Net.IPNetwork.TryParse(range, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    foreach (var range in cloudflareIpv6Ranges)
    {
        if (System.Net.IPNetwork.TryParse(range, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    // Allow localhost for development
    if (builder.Environment.IsDevelopment())
    {
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
    }
});

var app = builder.Build();

// Middleware pipeline (ORDER MATTERS!)
// 0. ForwardedHeaders MUST run first to set correct RemoteIpAddress before any other middleware
app.UseForwardedHeaders();

// 1. Security headers (earliest to apply to all responses)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Correlation ID tracking (needed by all downstream middleware)
app.UseMiddleware<CorrelationMiddleware>();

// 3. Problem Details exception handler (catch exceptions early)
app.UseMiddleware<ProblemDetailsMiddleware>();

// 4. Request logging (wraps downstream pipeline)
app.UseMiddleware<RequestLoggingMiddleware>();

// 5. CORS (before authentication/authorization)
app.UseCors(CorsConfiguration.PolicyName);

// 6. Authentication/authorization
app.UseAuthentication();
app.UseAuthorization();

// 7. Rate limiting (after auth so tenant/agent context is available)
app.UseRateLimiter();

// 8. Request enrichment WITH HEADER STRIPPING (runs after auth to inject validated claims)
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Diagnostics endpoints (Development only)
app.MapDiagnosticsEndpoints();

// Gateway endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = GatewayHello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(GatewayHello.Message))
    .AllowAnonymous()
    .WithTags("Gateway");

app.MapDhadgarDefaultEndpoints();

// YARP reverse proxy
app.MapReverseProxy()
    .RequireRateLimiting("Global");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
