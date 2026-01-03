using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

// Add CORS support
builder.Services.AddMeridianConsoleCors(builder.Configuration);

// Placeholder policies required by YARP route metadata (replace with real policies in Phase 3)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantScoped", policy => policy.RequireAssertion(_ => true));
    options.AddPolicy("Agent", policy => policy.RequireAssertion(_ => true));
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

// 5. Request logging (after correlation)
app.UseMiddleware<RequestLoggingMiddleware>();

// 6. Request enrichment WITH HEADER STRIPPING (before proxy, after correlation)
//    CRITICAL: Must run after authentication (Phase 3) but shown here for Phase 2
app.UseMiddleware<RequestEnrichmentMiddleware>();

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
app.MapReverseProxy();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
