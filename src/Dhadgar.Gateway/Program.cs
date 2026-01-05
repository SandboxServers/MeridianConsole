using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
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

// Authentication/authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Issuer"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(
                builder.Configuration.GetValue<int?>("Jwt:ClockSkewSeconds") ?? 60)
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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Dhadgar.Gateway"))
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

    options.AddPolicy("PerTenant", httpContext =>
    {
        var tenantId = httpContext.User.FindFirst("org_id")?.Value
                       ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
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

var app = builder.Build();

// Middleware pipeline (ORDER MATTERS!)
// 1. Security headers (earliest to apply to all responses)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Problem Details exception handler (catch exceptions early)
app.UseMiddleware<ProblemDetailsMiddleware>();

// 3. CORS (before authentication/authorization)
app.UseCors(CorsConfiguration.PolicyName);

// 4. Correlation ID tracking (needed by all downstream middleware)
app.UseMiddleware<CorrelationMiddleware>();

// 5. Authentication/authorization
app.UseAuthentication();
app.UseAuthorization();

// 6. Rate limiting (after auth so tenant/agent context is available)
app.UseRateLimiter();

// 7. Request enrichment WITH HEADER STRIPPING (runs after auth to inject validated claims)
app.UseMiddleware<RequestEnrichmentMiddleware>();

// 8. Request logging (after enrichment)
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Gateway endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
    .AllowAnonymous()
    .WithTags("Gateway");

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    status = "ok",
    timestamp = DateTime.UtcNow
}))
.AllowAnonymous()
.WithTags("Health");

// YARP reverse proxy
app.MapReverseProxy()
    .RequireRateLimiting("Global");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
