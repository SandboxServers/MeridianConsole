using System.Net;
using System.Threading.RateLimiting;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
using Dhadgar.Gateway;
using Dhadgar.Gateway.Endpoints;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Options;
using Dhadgar.Gateway.Readiness;
using Dhadgar.Gateway.Services;
using GatewayHello = Dhadgar.Gateway.Hello;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Resilience;
using Dhadgar.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    // Default 10 MB request body limit; Files service has its own 5-minute timeout for large uploads
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("gateway", new OpenApiInfo
        {
            Title = "Dhadgar Gateway API",
            Version = "v1",
            Description = "API Gateway endpoints (health checks, diagnostics)"
        });
    });
}

// Add CORS support
builder.Services.AddMeridianConsoleCors(builder.Configuration, builder.Environment);

// Configure options with validation
builder.Services.AddOptions<ReadyzOptions>()
    .Bind(builder.Configuration.GetSection("Readyz"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add circuit breaker (shared resilience library)
builder.Services.AddCircuitBreaker(builder.Configuration);

// Add Problem Details for standardized error responses
builder.Services.AddProblemDetails();

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

    // SECURITY: DenyAll policy for blocking internal endpoints from external access
    options.AddPolicy("DenyAll", policy =>
    {
        policy.RequireAssertion(_ => false);
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// HttpClient for diagnostics endpoints
builder.Services.AddHttpClient();

// Memory cache and OpenAPI aggregation service
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("OpenApiAggregation")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<OpenApiAggregationService>();

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
            .AddHttpClientInstrumentation()
            .AddSource("Yarp.ReverseProxy"); // Add YARP distributed tracing

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
            .AddProcessInstrumentation()
            .AddMeter("Dhadgar.ServiceDefaults.CircuitBreaker"); // Custom circuit breaker metrics

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

    // Add Retry-After header when rate limited
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        // Calculate retry after based on rate limiter window
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterTime)
            ? (int)retryAfterTime.TotalSeconds
            : 60; // Default to 60 seconds
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = "https://httpstatuses.com/429",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Request rate limit exceeded. Please retry after the specified time.",
            Instance = context.HttpContext.Request.Path.Value
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken);
    };

    options.AddPolicy("Global", _ =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:Global:PermitLimit") ?? 1000,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:Global:WindowSeconds") ?? 60),
                QueueLimit = 0
            }));

    // SECURITY: Use /64 prefix for IPv6 to prevent address rotation attacks
    // IPv6 users typically get at least a /64 prefix, so rotating within that
    // range would bypass IP-based rate limiting if we used full addresses
    options.AddPolicy("Auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress;
        string partitionKey;

        if (ip == null)
        {
            partitionKey = "unknown";
        }
        else if (ip.IsIPv6LinkLocal)
        {
            // SECURITY: Link-local addresses (fe80::) are not globally unique and can be
            // reused across different networks/interfaces. Treat them as a special bucket
            // to prevent multiple clients from unfairly sharing the same rate limit.
            partitionKey = "unknown-linklocal";
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                 !ip.IsIPv4MappedToIPv6)
        {
            // Use /64 prefix for IPv6 to prevent rotation attacks
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8); // Zero out host portion (last 64 bits)
            partitionKey = new IPAddress(bytes).ToString();
        }
        else
        {
            partitionKey = ip.ToString();
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:Auth:PermitLimit") ?? 30,
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Policies:Auth:WindowSeconds") ?? 60),
                QueueLimit = 0
            });
    });

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

// Configure Cloudflare IP service for dynamic IP range fetching
builder.Services.AddOptions<CloudflareOptions>()
    .Bind(builder.Configuration.GetSection(CloudflareOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddHttpClient<ICloudflareIpService, CloudflareIpService>(client =>
{
    var timeout = builder.Configuration.GetValue<int?>("Cloudflare:FetchTimeoutSeconds") ?? 30;
    client.Timeout = TimeSpan.FromSeconds(timeout);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Dhadgar-Gateway/1.0");
});

builder.Services.AddHostedService<CloudflareIpHostedService>();

// Configure ForwardedHeaders to trust Cloudflare proxy
// SECURITY: Only trust X-Forwarded-* headers from known Cloudflare IP ranges
// Without this, attackers could spoof CF-Connecting-IP or X-Forwarded-For headers
builder.Services.AddSingleton<IPostConfigureOptions<ForwardedHeadersOptions>>(sp =>
{
    var cloudflareService = sp.GetRequiredService<ICloudflareIpService>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new CloudflareForwardedHeadersPostConfigure(cloudflareService, env);
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2; // Cloudflare + potential internal proxy
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

// 9. YARP circuit breaker adapter - extracts cluster ID for circuit breaker
// Must run before circuit breaker middleware
app.UseYarpCircuitBreakerAdapter();

// 10. Circuit breaker - protects against cascading failures (shared resilience library)
// Uses ICircuitBreakerStateStore for distributed scenarios and TimeProvider for testability
app.UseCircuitBreaker();

if (app.Environment.IsDevelopment())
{
    // Serve aggregated OpenAPI spec at a path that won't be intercepted by UseSwagger middleware
    app.MapGet("/openapi/all.json", async (OpenApiAggregationService aggregator, CancellationToken ct) =>
    {
        var spec = await aggregator.GetAggregatedSpecAsync(ct);
        return Results.Json(spec, contentType: "application/json");
    })
    .AllowAnonymous()
    .ExcludeFromDescription();

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Single aggregated spec containing all services (served from /openapi/all.json)
        options.SwaggerEndpoint("/openapi/all.json", "All Services");

        // Individual service specs (still available if needed)
        options.SwaggerEndpoint("/swagger/gateway/swagger.json", "Gateway Only");

        options.DocumentTitle = "Meridian Console API";
        options.RoutePrefix = "swagger";
    });
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
