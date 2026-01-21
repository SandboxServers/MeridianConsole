# Phase 1: Logging Foundation - Research

**Researched:** 2026-01-21
**Domain:** .NET Logging, OpenTelemetry, PII Redaction, Multi-Tenant Observability
**Confidence:** HIGH

## Summary

This phase establishes consistent, high-performance, secure logging across all 13+ microservices in the Meridian Console platform. The research confirms that the existing codebase already has strong foundational elements in place (CorrelationMiddleware, SecurityEventLogger using [LoggerMessage], OpenTelemetry integration) that should be extended rather than replaced.

The standard approach is:
1. Keep Microsoft.Extensions.Logging as the logging abstraction (already in use)
2. Use [LoggerMessage] source-generated logging for all application logging (pattern already proven in SecurityEventLogger)
3. Add Microsoft.Extensions.Compliance.Redaction for PII scrubbing (new dependency)
4. Extend existing middleware to include tenant context in log scopes
5. Leverage OpenTelemetry's automatic trace-log correlation (already configured)

**Primary recommendation:** Create a centralized logging infrastructure in ServiceDefaults with source-generated loggers, redaction services, and enrichment middleware that all services inherit automatically.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Logging | 10.0.0 | Logging abstraction | Built-in, all services already use it |
| Microsoft.Extensions.Compliance.Redaction | 10.2.0 | PII/sensitive data scrubbing | Official Microsoft library, integrates with LoggerMessage |
| Microsoft.Extensions.Telemetry | 10.2.0 | Extended logging with redaction support | Required for EnableRedaction() on ILoggingBuilder |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.14.0 | OTLP log export | Already in use, provides trace-log correlation |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| OpenTelemetry.Extensions.Hosting | 1.14.0 | Host integration | Already configured in all services |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Microsoft.Extensions.Logging | Serilog | More features but adds complexity; CLAUDE.md explicitly says "Keep Microsoft.Extensions.Logging (no Serilog needed)" |
| Microsoft.Extensions.Compliance.Redaction | Custom regex scrubbing | Redaction library is type-safe, attribute-based, and integrates with source generators |

**Installation (new packages to add to Directory.Packages.props):**
```xml
<PackageVersion Include="Microsoft.Extensions.Compliance.Redaction" Version="10.2.0" />
<PackageVersion Include="Microsoft.Extensions.Telemetry" Version="10.2.0" />
```

## Architecture Patterns

### Recommended Project Structure
```
src/Shared/Dhadgar.ServiceDefaults/
├── Logging/
│   ├── DhadgarLoggerMessages.cs      # Shared [LoggerMessage] definitions
│   ├── LoggingExtensions.cs          # AddDhadgarLogging() extension
│   ├── TenantLoggingScope.cs         # Tenant context for log enrichment
│   └── LogCategories.cs              # Category constants (EventId ranges)
├── Compliance/
│   ├── DataClassifications.cs        # PII taxonomy (Email, Token, etc.)
│   ├── Redactors/
│   │   ├── EmailRedactor.cs          # user@domain.com -> u***@***.com
│   │   ├── TokenRedactor.cs          # Bearer xyz... -> [REDACTED-TOKEN]
│   │   └── ConnectionStringRedactor.cs
│   └── RedactionExtensions.cs        # AddDhadgarRedaction() extension
├── Middleware/
│   ├── CorrelationMiddleware.cs      # (existing) - extend with tenant
│   ├── RequestLoggingMiddleware.cs   # (existing) - convert to [LoggerMessage]
│   └── TenantEnrichmentMiddleware.cs # New - adds tenant to logging scope
└── MultiTenancy/
    └── OrganizationContext.cs        # (existing) - provides tenant ID
```

### Pattern 1: Source-Generated Logging with [LoggerMessage]

**What:** Use compile-time source generation for all logging calls
**When to use:** ALL application logging - no exceptions
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator
public sealed partial class ServerLifecycleLogger
{
    private readonly ILogger<ServerLifecycleLogger> _logger;

    public ServerLifecycleLogger(ILogger<ServerLifecycleLogger> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Server {ServerId} started for tenant {TenantId}")]
    public partial void ServerStarted(Guid serverId, Guid tenantId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Server {ServerId} failed to start: {Reason}")]
    public partial void ServerStartFailed(Guid serverId, string reason);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Server {ServerId} crashed unexpectedly")]
    public partial void ServerCrashed(Guid serverId, Exception? exception = null);
}
```

### Pattern 2: Centralized Log Enrichment via Middleware

**What:** Add tenant ID, correlation ID, and service context to ALL log entries via middleware
**When to use:** Every HTTP request
**Example:**
```csharp
// Extend existing RequestLoggingMiddleware pattern
public sealed class TenantEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public TenantEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOrganizationContext orgContext, ILogger<TenantEnrichmentMiddleware> logger)
    {
        var tenantId = orgContext.OrganizationId?.ToString() ?? "system";
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId,
            ["CorrelationId"] = correlationId,
            ["ServiceName"] = "Dhadgar.Servers",
            ["ServiceVersion"] = typeof(TenantEnrichmentMiddleware).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["Hostname"] = Environment.MachineName
        }))
        {
            await _next(context);
        }
    }
}
```

### Pattern 3: PII Redaction with Data Classification

**What:** Classify data types and apply automatic redaction when logging
**When to use:** Any log that might contain user data
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction
public static class DhadgarDataClassifications
{
    public static string TaxonomyName => "Dhadgar";

    public static DataClassification Email => new(TaxonomyName, nameof(Email));
    public static DataClassification Token => new(TaxonomyName, nameof(Token));
    public static DataClassification ConnectionString => new(TaxonomyName, nameof(ConnectionString));
    public static DataClassification Password => new(TaxonomyName, nameof(Password));
    public static DataClassification ApiKey => new(TaxonomyName, nameof(ApiKey));
}

// Usage in LoggerMessage
[LoggerMessage(
    EventId = 2001,
    Level = LogLevel.Information,
    Message = "User {Email} authenticated successfully")]
public static partial void UserAuthenticated(
    this ILogger logger,
    [DhadgarDataClassifications.Email] string email);
```

### Pattern 4: EventId Conventions

**What:** Structured EventId ranges by domain
**When to use:** All [LoggerMessage] definitions
**Convention:**
```
EventId Ranges:
- 1000-1999: Server lifecycle events
- 2000-2999: Authentication/Identity events
- 3000-3999: Node management events
- 4000-4999: Task orchestration events
- 5000-5999: Security events (already defined in SecurityEventLogger)
- 6000-6999: File operations events
- 7000-7999: Mod management events
- 8000-8999: Billing events
- 9000-9999: Infrastructure/system events
```

### Anti-Patterns to Avoid

- **String interpolation in log calls:** `_logger.LogInformation($"User {userId} logged in")` - Use [LoggerMessage] instead
- **Logging sensitive data without redaction:** NEVER log tokens, passwords, connection strings, or PII without classification attributes
- **Missing correlation context:** All log entries must be within a scope that includes CorrelationId and TenantId
- **Generic ILogger<T> without typed messages:** Use domain-specific logger classes with [LoggerMessage] methods
- **Inconsistent log levels:** Debug for verbose internals, Info for business events, Warning for recoverable issues, Error for failures, Critical for service-down conditions

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| PII scrubbing | Regex replacement | Microsoft.Extensions.Compliance.Redaction | Type-safe, attribute-based, handles edge cases |
| Log correlation | Custom headers | OpenTelemetry Activity + existing CorrelationMiddleware | Already propagates TraceId/SpanId automatically |
| High-perf logging | Extension methods | [LoggerMessage] source generator | 10-20x faster, no boxing, compile-time validation |
| Tenant isolation | String formatting | ILoggingBuilder BeginScope | Structured logging, automatic inheritance |
| Service context | Manual fields | OpenTelemetry Resource attributes | Standard semantic conventions |

**Key insight:** The codebase already has most of the infrastructure (CorrelationMiddleware, SecurityEventLogger pattern, OpenTelemetry setup). This phase is about standardizing and extending existing patterns, not rebuilding from scratch.

## Common Pitfalls

### Pitfall 1: Logging Exceptions Incorrectly

**What goes wrong:** Exception details lost or logged redundantly
**Why it happens:** Using `ex.ToString()` or `ex.Message` instead of exception parameter
**How to avoid:** Always use the Exception parameter in [LoggerMessage]:
```csharp
[LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Operation failed for {EntityId}")]
public partial void OperationFailed(Guid entityId, Exception? exception = null);
// Called as: OperationFailed(id, ex); // NOT OperationFailed(id + ": " + ex.Message);
```
**Warning signs:** Exception stack traces appearing in Message field, duplicate exception logging

### Pitfall 2: Missing Tenant Context in Background Services

**What goes wrong:** Background tasks (hosted services, message consumers) log without tenant context
**Why it happens:** No HttpContext available, so middleware doesn't run
**How to avoid:** Explicitly create logging scope in background operations:
```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["TenantId"] = message.TenantId.ToString(),
    ["CorrelationId"] = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString()
}))
{
    // Process message
}
```
**Warning signs:** Logs from background services missing TenantId field

### Pitfall 3: Redaction Not Applied to Logged Objects

**What goes wrong:** Objects logged with {Object:json} or ToString() bypass redaction
**Why it happens:** Redaction only applies to parameters with classification attributes
**How to avoid:** Log individual fields, not entire objects; or use [LogProperties] for complex types:
```csharp
// BAD: _logger.LogInformation("User: {User}", userDto);
// GOOD: UserLoggedIn(userDto.Id, userDto.Email); // Email has redaction attribute
```
**Warning signs:** Full JSON objects appearing in logs with unredacted PII

### Pitfall 4: Duplicate OpenTelemetry Configuration

**What goes wrong:** Services configure OTLP twice, logs duplicated or lost
**Why it happens:** Both builder.Logging.AddOpenTelemetry() and separate configuration in services
**How to avoid:** Centralize all OTLP configuration in ServiceDefaults extension method:
```csharp
// In ServiceDefaultsExtensions.cs
public static IHostBuilder AddDhadgarLogging(this IHostBuilder builder)
{
    // Single place for all logging config
}
```
**Warning signs:** Duplicate log entries, inconsistent log formatting between services

### Pitfall 5: MassTransit Consumer Logging Without Correlation

**What goes wrong:** Message processing logs not correlated with original request
**Why it happens:** MassTransit creates new Activity, correlation ID not propagated
**How to avoid:** MassTransit 8.3.6 automatically propagates trace context via headers. Ensure:
- Activity listener is configured: `tracing.AddSource("MassTransit")`
- CorrelationId is set in Activity baggage at publish time
**Warning signs:** Cannot trace request through message queue to consumer

## Code Examples

Verified patterns from official sources and existing codebase:

### Service Registration (ServiceDefaults)
```csharp
// Source: Existing pattern in Dhadgar.ServiceDefaults.ServiceDefaultsExtensions
public static IServiceCollection AddDhadgarLogging(this IServiceCollection services, IConfiguration configuration)
{
    // 1. Add redaction services
    services.AddRedaction(builder =>
    {
        builder.SetRedactor<EmailRedactor>(DhadgarDataClassifications.Email);
        builder.SetRedactor<TokenRedactor>(DhadgarDataClassifications.Token);
        builder.SetRedactor<ErasingRedactor>(DhadgarDataClassifications.Password);
        builder.SetRedactor<ConnectionStringRedactor>(DhadgarDataClassifications.ConnectionString);
    });

    // 2. Add organization context for tenant isolation
    services.AddOrganizationContext();

    // 3. Add shared logger services
    services.AddSingleton<ISecurityEventLogger, SecurityEventLogger>();

    return services;
}
```

### Logging Builder Extension
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator
public static ILoggingBuilder AddDhadgarLogging(this ILoggingBuilder builder, string serviceName, IConfiguration configuration)
{
    // Enable redaction in logging pipeline
    builder.EnableRedaction();

    // Configure OpenTelemetry logging
    var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
    Uri? otlpUri = null;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
    {
        otlpUri = parsed;
    }

    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;  // Critical for correlation/tenant context
        options.ParseStateValues = true;

        if (otlpUri is not null)
        {
            options.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
        }
    });

    return builder;
}
```

### Custom Email Redactor
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction
public sealed class EmailRedactor : Redactor
{
    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        // Format: u***@***.com (constant length for privacy)
        return "***@***.***".Length;
    }

    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        var result = "***@***.***";
        result.CopyTo(destination);
        return result.Length;
    }
}
```

### Existing SecurityEventLogger Pattern (Reference)
```csharp
// Source: src/Shared/Dhadgar.ServiceDefaults/Security/SecurityEventLogger.cs
// This is the pattern already proven in the codebase - follow it for new loggers

public sealed partial class SecurityEventLogger : ISecurityEventLogger
{
    private readonly ILogger<SecurityEventLogger> _logger;

    public SecurityEventLogger(ILogger<SecurityEventLogger> logger)
    {
        _logger = logger;
    }

    // Public method for easy consumption
    public void LogAuthenticationSuccess(Guid userId, string? email, string? clientIp, string? userAgent, string? orgId = null)
    {
        AuthenticationSucceeded(userId, email ?? "unknown", clientIp ?? "unknown", userAgent ?? "unknown", orgId ?? "none");
    }

    // Source-generated private method
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Security: Authentication succeeded for user {UserId} ({Email}) from {ClientIp} using {UserAgent}, org={OrgId}")]
    private partial void AuthenticationSucceeded(Guid userId, string email, string clientIp, string userAgent, string orgId);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| LoggerMessage.Define() | [LoggerMessage] attribute | .NET 6 | Simpler syntax, unlimited parameters |
| Manual string interpolation | Source-generated logging | .NET 6 | 10-20x performance improvement |
| Custom regex scrubbing | Microsoft.Extensions.Compliance.Redaction | .NET 8 | Type-safe, attribute-based redaction |
| Manual correlation headers | OpenTelemetry automatic correlation | .NET 8 | TraceId/SpanId automatically in logs |
| [LoggerMessage] with Define() | Rewritten source generator | .NET 8 | No longer uses LoggerMessage.Define internally |

**Deprecated/outdated:**
- Serilog: Not needed for this codebase; Microsoft.Extensions.Logging + OpenTelemetry covers all requirements
- Manual LoggerMessage.Define(): Use [LoggerMessage] attribute instead
- System.Diagnostics.Trace: Use ILogger with OpenTelemetry

## Open Questions

Things that couldn't be fully resolved:

1. **MassTransit Publish vs Send correlation**
   - What we know: MassTransit 8.3.6 has built-in OpenTelemetry support
   - What's unclear: GitHub discussion mentions correlation "only works for Send, not Publish or Request"
   - Recommendation: Test this explicitly during implementation; may need custom behavior for Publish

2. **Redaction attribute inheritance in DTOs**
   - What we know: Classification attributes work on method parameters
   - What's unclear: Whether they work when logging entire objects via [LogProperties]
   - Recommendation: Test with sample DTO; may need to log individual fields

3. **Background service logging scope lifetime**
   - What we know: BeginScope works but must be manually created
   - What's unclear: Best pattern for long-running background services
   - Recommendation: Research IServiceScope + logging scope management patterns

## Sources

### Primary (HIGH confidence)
- [Microsoft LoggerMessage Generator Documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator) - Core LoggerMessage patterns
- [Microsoft Data Redaction Documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction) - Redaction API and custom redactors
- [OpenTelemetry .NET Log Correlation](https://opentelemetry.io/docs/languages/dotnet/logs/correlation/) - Automatic trace-log correlation
- Existing codebase: `src/Shared/Dhadgar.ServiceDefaults/Security/SecurityEventLogger.cs` - Proven [LoggerMessage] pattern

### Secondary (MEDIUM confidence)
- [MassTransit Observability Documentation](https://masstransit.io/documentation/configuration/observability) - OpenTelemetry integration
- [Andrew Lock: Redacting sensitive data](https://andrewlock.net/redacting-sensitive-data-with-microsoft-extensions-compliance/) - Redaction best practices
- [OpenTelemetry .NET 10 Guide (Medium)](https://vitorafgomes.medium.com/complete-observability-with-opentelemetry-in-net-10-a-practical-and-universal-guide-c9dda9edaace) - .NET 10 patterns

### Tertiary (LOW confidence)
- [MassTransit GitHub Discussion #5418](https://github.com/MassTransit/MassTransit/discussions/5418) - Context propagation edge cases (needs validation)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Official Microsoft libraries, already in use
- Architecture patterns: HIGH - Proven in existing SecurityEventLogger
- Pitfalls: MEDIUM - Based on documentation + community reports
- MassTransit correlation: LOW - Conflicting reports, needs testing

**Research date:** 2026-01-21
**Valid until:** 2026-02-21 (30 days - stable domain)
