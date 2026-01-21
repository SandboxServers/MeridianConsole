# Phase 2: Distributed Tracing - Research

**Researched:** 2026-01-21
**Domain:** OpenTelemetry Tracing, EF Core Instrumentation, Redis Instrumentation, Custom Spans
**Confidence:** HIGH

## Summary

This phase adds distributed tracing instrumentation so that database queries (EF Core), cache operations (Redis), and custom business operations appear as spans in distributed traces. The codebase already has OpenTelemetry tracing configured with ASP.NET Core and HttpClient instrumentation. This phase extends that foundation by adding EF Core and Redis instrumentation packages, and establishing patterns for creating custom business spans using the .NET Activity API.

The standard approach is:
1. Add OpenTelemetry.Instrumentation.EntityFrameworkCore (1.15.0-beta.1) to trace all EF Core queries
2. Add OpenTelemetry.Instrumentation.StackExchangeRedis (1.15.0-beta.1) to trace Redis operations
3. Use System.Diagnostics.Activity/ActivitySource for custom business spans (no additional packages needed)
4. Update ProblemDetailsMiddleware to include TraceId from Activity.Current in error responses

**Primary recommendation:** Centralize all tracing configuration in ServiceDefaults with a new `AddDhadgarTracing()` extension method that configures EF Core, Redis, and custom ActivitySources. Services only need to pass their IConnectionMultiplexer and register their custom ActivitySources.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| OpenTelemetry.Instrumentation.EntityFrameworkCore | 1.15.0-beta.1 | Auto-trace EF Core queries | Official OTEL contrib package, 33M+ downloads |
| OpenTelemetry.Instrumentation.StackExchangeRedis | 1.15.0-beta.1 | Auto-trace Redis operations | Official OTEL contrib package, 29M+ downloads |
| System.Diagnostics.DiagnosticSource | (built-in) | Activity/ActivitySource API | .NET's native tracing API, OTEL uses it internally |

### Already Configured (from Phase 1 / existing codebase)
| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| OpenTelemetry | 1.14.0 | Core OTEL SDK | In Directory.Packages.props |
| OpenTelemetry.Extensions.Hosting | 1.14.0 | Host integration | In all services |
| OpenTelemetry.Instrumentation.AspNetCore | 1.14.0 | HTTP request tracing | In all services |
| OpenTelemetry.Instrumentation.Http | 1.14.0 | HttpClient tracing | In all services |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.14.0 | OTLP export | In all services |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| OTEL EF Core instrumentation | Manual spans around DbContext | OTEL package auto-instruments ALL queries with zero code changes |
| OTEL Redis instrumentation | StackExchange.Redis built-in profiling | OTEL package converts profiled commands to spans automatically |
| Activity API | OpenTelemetry.Api shim | Activity API is .NET-native; shim adds terminology but no new functionality |

**Installation (add to Directory.Packages.props):**
```xml
<PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.15.0-beta.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.StackExchangeRedis" Version="1.15.0-beta.1" />
```

**Note:** These packages are in beta because OpenTelemetry semantic conventions for database spans are still marked experimental. The APIs are stable and widely used in production.

## Architecture Patterns

### Recommended Project Structure
```
src/Shared/Dhadgar.ServiceDefaults/
├── Tracing/
│   ├── TracingExtensions.cs           # AddDhadgarTracing() extension
│   ├── ActivitySources.cs             # Shared ActivitySource definitions
│   └── TracingConstants.cs            # Span name conventions
├── Middleware/
│   └── ProblemDetailsMiddleware.cs    # Update to include TraceId
└── (existing logging infrastructure)

src/Dhadgar.{Service}/
└── Tracing/
    └── {Service}ActivitySource.cs     # Service-specific ActivitySource
```

### Pattern 1: Centralized Tracing Configuration

**What:** Single extension method that configures all standard tracing instrumentation
**When to use:** Every service that needs tracing (all of them)
**Example:**
```csharp
// Source: Pattern based on existing ServiceDefaults extensions
public static class TracingExtensions
{
    /// <summary>
    /// Adds Dhadgar tracing configuration including EF Core and Redis instrumentation.
    /// </summary>
    public static IServiceCollection AddDhadgarTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out otlpUri);
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        // Include SQL command text in spans (sanitized by default)
                        options.EnrichWithIDbCommand = (activity, command) =>
                        {
                            // Optionally add custom tags
                            activity.SetTag("db.system", "postgresql");
                        };
                    });

                // Allow service-specific configuration
                configureTracing?.Invoke(tracing);

                if (otlpUri is not null)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
                }
            });

        return services;
    }

    /// <summary>
    /// Adds Redis instrumentation for a registered IConnectionMultiplexer.
    /// Call after AddSingleton<IConnectionMultiplexer>().
    /// </summary>
    public static TracerProviderBuilder AddDhadgarRedisInstrumentation(
        this TracerProviderBuilder builder,
        IServiceProvider? serviceProvider = null)
    {
        // Redis instrumentation resolves IConnectionMultiplexer from DI
        return builder.AddRedisInstrumentation();
    }
}
```

### Pattern 2: EF Core Instrumentation

**What:** Automatic tracing of all Entity Framework Core queries
**When to use:** Services with PostgreSQL databases (Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Notifications, Discord)
**Example:**
```csharp
// Source: https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.EntityFrameworkCore/README.md
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            // Optional: Customize activity based on command
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                // Add custom tags for better trace analysis
                var commandType = command.CommandType.ToString();
                activity.SetTag("db.operation", commandType);
            };

            // Optional: Filter out specific queries
            options.Filter = (providerName, command) =>
            {
                // Don't trace health check queries
                return !command.CommandText.Contains("__EFMigrationsHistory");
            };
        }));
```

**Span attributes produced:**
- `db.system`: Database type (e.g., "postgresql")
- `db.name`: Database name
- `db.statement`: SQL query (if not filtered)
- `db.operation`: Command type (Text, StoredProcedure, etc.)

### Pattern 3: Redis Instrumentation

**What:** Automatic tracing of all Redis commands
**When to use:** Services using StackExchange.Redis (currently Identity)
**Example:**
```csharp
// Source: https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.StackExchangeRedis/README.md

// Step 1: Register IConnectionMultiplexer (already done in Identity)
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect("localhost:6379"));

// Step 2: Add Redis instrumentation (resolves from DI)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddRedisInstrumentation(options =>
        {
            // Flush profiled commands to activities every 5 seconds
            options.FlushInterval = TimeSpan.FromSeconds(5);

            // Include Redis keys in spans (careful: may contain sensitive data)
            options.SetVerboseDatabaseStatements = false; // Default: false

            // Optional: Enrich spans with custom tags
            options.Enrich = (activity, command) =>
            {
                if (command.ElapsedTime > TimeSpan.FromMilliseconds(100))
                {
                    activity.SetTag("redis.slow_query", true);
                }
            };
        }));
```

**Span attributes produced:**
- `db.system`: "redis"
- `db.operation`: Command name (GET, SET, etc.)
- `db.redis.database_index`: Database index
- `net.peer.name`: Redis server hostname
- `net.peer.port`: Redis server port

### Pattern 4: Custom Business Spans with Activity API

**What:** Create spans for important business operations
**When to use:** Long-running operations, external service calls, batch processing
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs
using System.Diagnostics;

public static class ServersActivitySource
{
    // Create once, reuse everywhere
    private static readonly ActivitySource Source = new("Dhadgar.Servers", "1.0.0");

    // Activity names follow OpenTelemetry semantic conventions
    public const string StartServer = "server.start";
    public const string StopServer = "server.stop";
    public const string InstallMod = "server.mod.install";

    public static Activity? StartServerActivity(Guid serverId, Guid tenantId)
    {
        var activity = Source.StartActivity(StartServer, ActivityKind.Internal);

        // Only set tags if someone is listening
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("server.id", serverId.ToString());
            activity.SetTag("tenant.id", tenantId.ToString());
        }

        return activity;
    }

    // Expose ActivitySource for registration
    public static string ActivitySourceName => Source.Name;
}

// Usage in service code:
public async Task StartServerAsync(Guid serverId, Guid tenantId)
{
    using var activity = ServersActivitySource.StartServerActivity(serverId, tenantId);

    try
    {
        // Business logic here...
        await ValidateServerConfigAsync(serverId);
        await ProvisionResourcesAsync(serverId);
        await LaunchProcessAsync(serverId);

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
}

// Register ActivitySource with OTEL:
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(ServersActivitySource.ActivitySourceName));
```

### Pattern 5: TraceId in Problem Details Responses

**What:** Include the OpenTelemetry TraceId in all error responses
**When to use:** ProblemDetailsMiddleware (all services)
**Example:**
```csharp
// Source: Existing ProblemDetailsMiddleware pattern + Activity.Current usage
private async Task HandleExceptionAsync(HttpContext context, Exception exception)
{
    if (context.Response.HasStarted)
    {
        return;
    }

    // Get TraceId from current Activity (set by ASP.NET Core instrumentation)
    var traceId = Activity.Current?.TraceId.ToString()
        ?? context.TraceIdentifier
        ?? "unknown";

    _logger.LogError(exception,
        "Unhandled exception. TraceId: {TraceId}, Path: {Path}",
        traceId, context.Request.Path);

    var includeDetails = _environment.IsDevelopment();

    var problemDetails = new
    {
        type = "https://meridian.console/errors/internal-server-error",
        title = "Internal Server Error",
        status = (int)HttpStatusCode.InternalServerError,
        detail = includeDetails ? exception.Message : "An unexpected error occurred.",
        instance = context.Request.Path.ToString(),
        traceId = traceId,  // TRACE-04: Always include TraceId
        extensions = includeDetails ? new { stackTrace = exception.StackTrace } : null
    };

    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
    context.Response.ContentType = "application/problem+json";

    await context.Response.WriteAsJsonAsync(problemDetails);
}
```

### Anti-Patterns to Avoid

- **Creating ActivitySource per request:** Create once as static field, reuse for all requests
- **Starting Activity without checking for listeners:** Use `source.StartActivity()` which returns null if not sampled
- **Setting tags without checking IsAllDataRequested:** Unnecessary overhead if span is not being recorded
- **Including sensitive data in span tags:** Redis keys, SQL parameters may contain PII
- **Forgetting to register ActivitySource:** Custom spans won't appear unless `AddSource()` is called
- **Using Activity.Current directly instead of StartActivity return value:** May get parent activity instead of new child

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| EF Core query tracing | Manual spans around DbContext calls | OpenTelemetry.Instrumentation.EntityFrameworkCore | Auto-instruments all queries, includes timing, handles async |
| Redis operation tracing | Manual IProfiler implementation | OpenTelemetry.Instrumentation.StackExchangeRedis | Converts profiled commands to spans, handles batching |
| Trace context propagation | Manual header passing | OpenTelemetry automatic propagation | Already configured via AspNetCore/HttpClient instrumentation |
| Span creation | Custom timing code | Activity API | .NET-native, integrates with OTEL automatically |
| Activity naming | Arbitrary strings | OpenTelemetry semantic conventions | Standard attributes enable better tooling support |

**Key insight:** The Activity/ActivitySource API is .NET's native implementation of OpenTelemetry spans. Using it directly (not the OTEL shim) is the recommended approach for .NET applications.

## Common Pitfalls

### Pitfall 1: Redis Instrumentation Not Capturing Spans

**What goes wrong:** Redis commands don't appear in traces
**Why it happens:** IConnectionMultiplexer not passed to instrumentation or FlushInterval too long
**How to avoid:**
- Ensure `AddRedisInstrumentation()` is called AFTER registering IConnectionMultiplexer
- Use service provider resolution: `AddRedisInstrumentation()` without parameters
- For debugging, reduce FlushInterval: `options.FlushInterval = TimeSpan.FromSeconds(1)`
**Warning signs:** HTTP and EF Core spans appear but no Redis spans; check profiled command count in Redis client

### Pitfall 2: EF Core Spans Missing for Some Queries

**What goes wrong:** Some database queries don't show spans
**Why it happens:** Filter callback is too aggressive, or queries are NoSQL (Cosmos DB)
**How to avoid:**
- EF Core instrumentation only supports relational databases (PostgreSQL, SQL Server)
- Review Filter callback logic if using one
- Check that AddEntityFrameworkCoreInstrumentation is called in tracing setup
**Warning signs:** Spans appear for some queries but not others with same DbContext

### Pitfall 3: Custom ActivitySource Not Producing Spans

**What goes wrong:** Custom spans created with StartActivity but don't appear in traces
**Why it happens:** ActivitySource not registered with TracerProvider via AddSource()
**How to avoid:**
```csharp
// MUST register the ActivitySource name
.WithTracing(tracing => tracing
    .AddSource("Dhadgar.Servers")  // Must match ActivitySource name exactly
    .AddSource("Dhadgar.Tasks"));
```
**Warning signs:** Activity.Current returns null inside business methods, spans don't appear in Jaeger/Tempo

### Pitfall 4: Duplicate Spans from Nested Instrumentation

**What goes wrong:** HTTP call shows multiple spans for single operation
**Why it happens:** Multiple instrumentation layers (AspNetCore + HttpClient + custom span) all active
**How to avoid:**
- This is expected behavior - each layer adds context
- Parent-child relationships should be preserved
- Only suppress if causing performance issues (use Filter callbacks)
**Warning signs:** Trace waterfall shows redundant spans without additional information

### Pitfall 5: TraceId Mismatch Between Logs and Problem Details

**What goes wrong:** TraceId in error response doesn't match log entries
**Why it happens:** Getting Activity.Current from wrong context or after Activity disposed
**How to avoid:**
- Get Activity.Current at the START of the middleware invoke, before calling next()
- Store TraceId in HttpContext.Items if needed later
- Prefer Activity.Current?.TraceId over HttpContext.TraceIdentifier for OTEL correlation
**Warning signs:** Error response traceId doesn't find any matching trace in observability tool

### Pitfall 6: Activity API Used Without Understanding Sampling

**What goes wrong:** Some requests have full spans, others have nothing
**Why it happens:** OTEL uses sampling to reduce data volume; some requests may not be sampled
**How to avoid:**
- Always check `activity != null` after StartActivity()
- Production: Use appropriate sampler (default is ParentBased+AlwaysOn for child spans)
- Development: Consider AlwaysOnSampler for debugging
**Warning signs:** Intermittent trace completeness, especially for low-traffic endpoints

## Code Examples

Verified patterns from official sources and existing codebase:

### Complete Service Configuration
```csharp
// Source: Pattern based on existing Gateway/Identity Program.cs + official OTEL docs
var builder = WebApplication.CreateBuilder(args);

// Configure services...
builder.Services.AddDbContext<ServersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Add tracing with EF Core instrumentation
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
Uri? otlpUri = !string.IsNullOrWhiteSpace(otlpEndpoint)
    ? new Uri(otlpEndpoint)
    : null;

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("Dhadgar.Servers");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("Dhadgar.Servers"); // Custom business spans

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });
```

### Service with Redis (Identity pattern)
```csharp
// Source: Existing Identity/Program.cs pattern + OTEL Redis docs
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString");
    return ConnectionMultiplexer.Connect(connectionString!);
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddRedisInstrumentation(); // Resolves IConnectionMultiplexer from DI

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });
```

### Custom ActivitySource Pattern
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs
using System.Diagnostics;

namespace Dhadgar.Tasks.Tracing;

/// <summary>
/// ActivitySource for task orchestration operations.
/// </summary>
public static class TasksActivitySource
{
    private static readonly ActivitySource Source = new("Dhadgar.Tasks", "1.0.0");

    public static string Name => Source.Name;

    /// <summary>
    /// Starts a span for task execution.
    /// </summary>
    public static Activity? StartTaskExecution(Guid taskId, string taskType, Guid tenantId)
    {
        var activity = Source.StartActivity("task.execute", ActivityKind.Internal);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("task.id", taskId.ToString());
            activity.SetTag("task.type", taskType);
            activity.SetTag("tenant.id", tenantId.ToString());
        }

        return activity;
    }

    /// <summary>
    /// Starts a span for task step execution (child of task execution).
    /// </summary>
    public static Activity? StartTaskStep(string stepName)
    {
        var activity = Source.StartActivity($"task.step.{stepName}", ActivityKind.Internal);
        return activity;
    }
}

// Usage:
public async Task ExecuteTaskAsync(TaskDefinition task)
{
    using var taskActivity = TasksActivitySource.StartTaskExecution(
        task.Id, task.Type, task.TenantId);

    foreach (var step in task.Steps)
    {
        using var stepActivity = TasksActivitySource.StartTaskStep(step.Name);

        try
        {
            await ExecuteStepAsync(step);
            stepActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            stepActivity?.RecordException(ex);
            throw;
        }
    }

    taskActivity?.SetStatus(ActivityStatusCode.Ok);
}
```

### Updated ProblemDetailsMiddleware
```csharp
// Source: Existing middleware + Activity.Current pattern
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Middleware;

public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write problem details after headers sent. Exception: {ExceptionType}",
                exception.GetType().Name);
            return;
        }

        // TRACE-04: Get TraceId from Activity.Current (set by OTEL AspNetCore instrumentation)
        // Fall back to HttpContext.TraceIdentifier if no activity (shouldn't happen with OTEL)
        var traceId = Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier
            ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception. TraceId: {TraceId}, Path: {Path}",
            traceId, context.Request.Path);

        var includeDetails = _environment.IsDevelopment() || _environment.IsEnvironment("Testing");

        var problemDetails = new
        {
            type = "https://meridian.console/errors/internal-server-error",
            title = "Internal Server Error",
            status = (int)HttpStatusCode.InternalServerError,
            detail = includeDetails
                ? exception.Message
                : "An unexpected error occurred. Please contact support with the trace ID.",
            instance = context.Request.Path.ToString(),
            traceId,  // TRACE-04: Always include for support correlation
            extensions = includeDetails
                ? new { stackTrace = exception.StackTrace }
                : null
        };

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, SerializerOptions);
        await context.Response.WriteAsync(json, context.RequestAborted);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual DbContext timing | AddEntityFrameworkCoreInstrumentation() | OTEL Contrib stable | Zero-code instrumentation |
| Redis IProfiler + manual spans | AddRedisInstrumentation() | OTEL Contrib stable | Automatic command-to-span conversion |
| TraceListener/CorrelationManager | Activity/ActivitySource | .NET 5+ | Native OTEL compatibility |
| Custom trace headers | W3C Trace Context | OTEL spec | Automatic propagation |

**Deprecated/outdated:**
- OpenTelemetry.Contrib.Instrumentation.EntityFrameworkCore: Replaced by main OTEL.Instrumentation package
- Manual correlation ID propagation: OTEL does this automatically via traceparent header
- System.Diagnostics.Trace: Use Activity API instead

## Open Questions

Things that couldn't be fully resolved:

1. **Redis instrumentation flush timing**
   - What we know: Default FlushInterval is 10 seconds
   - What's unclear: Optimal interval for dev vs production (latency vs overhead tradeoff)
   - Recommendation: Use 5 seconds for development, 10 seconds for production; make configurable

2. **EF Core query text in spans**
   - What we know: SQL is included by default, can enable parameter capture via env var
   - What's unclear: Whether parameter values could leak PII in production
   - Recommendation: Do NOT enable OTEL_DOTNET_EXPERIMENTAL_EFCORE_ENABLE_TRACE_DB_QUERY_PARAMETERS in production

3. **ActivitySource naming convention**
   - What we know: Pattern is typically "Company.Service" or "Dhadgar.{Service}"
   - What's unclear: Should we use single shared ActivitySource or per-service
   - Recommendation: Per-service ActivitySources (e.g., "Dhadgar.Servers", "Dhadgar.Tasks") for better filtering

## Sources

### Primary (HIGH confidence)
- [OpenTelemetry.Instrumentation.EntityFrameworkCore README](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.EntityFrameworkCore/README.md) - Official documentation
- [OpenTelemetry.Instrumentation.StackExchangeRedis README](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.StackExchangeRedis/README.md) - Official documentation
- [Microsoft Distributed Tracing Instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs) - Activity API patterns
- [NuGet: OpenTelemetry.Instrumentation.EntityFrameworkCore 1.15.0-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.EntityFrameworkCore) - Current version
- [NuGet: OpenTelemetry.Instrumentation.StackExchangeRedis 1.15.0-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.StackExchangeRedis) - Current version

### Secondary (MEDIUM confidence)
- [OpenTelemetry .NET Instrumentation Docs](https://opentelemetry.io/docs/languages/dotnet/instrumentation/) - General patterns
- [OpenTelemetry Getting Started ASP.NET Core](https://opentelemetry.io/docs/languages/dotnet/traces/getting-started-aspnetcore/) - ASP.NET Core integration
- Existing codebase: `src/Dhadgar.Gateway/Program.cs`, `src/Dhadgar.Identity/Program.cs` - Current OTEL setup patterns

### Tertiary (LOW confidence)
- [GitHub Discussion: traceresponse header](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3797) - No built-in support yet

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Official OTEL packages, well-documented
- Architecture patterns: HIGH - Based on official docs and existing codebase patterns
- EF Core instrumentation: HIGH - Straightforward, single method call
- Redis instrumentation: HIGH - Requires IConnectionMultiplexer registration first
- Custom spans: HIGH - .NET-native Activity API, well-documented
- Problem Details TraceId: HIGH - Simple Activity.Current usage

**Research date:** 2026-01-21
**Valid until:** 2026-02-21 (30 days - stable packages in beta but APIs stable)
