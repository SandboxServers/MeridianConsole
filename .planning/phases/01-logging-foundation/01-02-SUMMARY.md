---
phase: 01-logging-foundation
plan: 02
subsystem: observability
tags: [logging, middleware, source-generation, tenant-context, correlation]
dependency-graph:
  requires: [01-01-PLAN]
  provides: [RequestLoggingMessages, TenantEnrichmentMiddleware, UseDhadgarMiddleware]
  affects: [01-03-PLAN, all-services]
tech-stack:
  added: []
  patterns: [source-generated-logging, logging-scope-enrichment, middleware-pipeline]
key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Logging/RequestLoggingMessages.cs
    - src/Shared/Dhadgar.ServiceDefaults/Middleware/TenantEnrichmentMiddleware.cs
  modified:
    - src/Shared/Dhadgar.ServiceDefaults/Middleware/RequestLoggingMiddleware.cs
    - src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs
    - src/Dhadgar.Gateway/Program.cs
    - tests/Dhadgar.Gateway.Tests/MiddlewareUnitTests.cs
decisions:
  - decision: Use EventId range 9100-9199 for HTTP request logging
    rationale: Follows InfraEvents allocation (9000-9999) with HTTP subset
    impact: HttpRequestStarted=9101, HttpRequestCompleted=9102, HttpRequestCompletedWithWarning=9103, HttpRequestCompletedError=9104, HttpRequestFailed=9105
  - decision: Cache ServiceInfo (name, version, hostname) at process startup
    rationale: Avoids reflection overhead on every request
    impact: ServiceInfo accessible via TenantEnrichmentMiddleware.ServiceInfo for background services
  - decision: Use "system" as default TenantId when no organization context available
    rationale: Distinguishes system/infrastructure requests from tenant requests in logs
    impact: All logs include TenantId field, even for unauthenticated requests
metrics:
  duration: 8 minutes
  completed: 2026-01-21
---

# Phase 01 Plan 02: Middleware Updates for Source-Generated Logging Summary

**One-liner:** High-performance source-generated HTTP logging with tenant and service context enrichment middleware

## What Was Built

This plan converted the request logging middleware to use source-generated [LoggerMessage] methods and added tenant/service context enrichment to ensure every log entry includes comprehensive context.

### 1. RequestLoggingMessages (Logging/RequestLoggingMessages.cs)

Source-generated log messages for HTTP request/response logging:
- `LogRequestStarted(method, path)` - Debug level, EventId 9101
- `LogRequestCompleted(method, path, statusCode, elapsedMs)` - Routes to appropriate level:
  - >= 500: Error level (HttpRequestCompletedError, EventId 9104)
  - >= 400: Warning level (HttpRequestCompletedWithWarning, EventId 9103)
  - < 400: Information level (HttpRequestCompleted, EventId 9102)
- `LogRequestFailed(method, path, elapsedMs, exception)` - Error level, EventId 9105

Follows the SecurityEventLogger pattern: public wrapper methods handle conditional logic, calling private partial methods decorated with [LoggerMessage].

### 2. TenantEnrichmentMiddleware (Middleware/TenantEnrichmentMiddleware.cs)

Adds tenant and service context to the logging scope for all downstream operations:
- **TenantId**: From IOrganizationContext (claims or header) or "system" if not available
- **CorrelationId**: From HttpContext.Items (set by CorrelationMiddleware)
- **RequestId**: From HttpContext.Items (set by CorrelationMiddleware)
- **ServiceName**: Cached from entry assembly name
- **ServiceVersion**: Cached from entry assembly version
- **Hostname**: Cached from Environment.MachineName

Service info is computed once at startup and cached for the lifetime of the process.

### 3. Updated RequestLoggingMiddleware

- Now injects `RequestLoggingMessages` instead of `ILogger<RequestLoggingMiddleware>`
- Removed the BeginScope block (TenantEnrichmentMiddleware handles context)
- Uses source-generated logging methods for all HTTP logging
- Simplified code with better performance characteristics

### 4. Updated ServiceDefaultsExtensions

Added two key registrations:
- `AddDhadgarServiceDefaults()` now registers:
  - `IOrganizationContext` via `AddOrganizationContext()`
  - `RequestLoggingMessages` as singleton

Added new extension method:
- `UseDhadgarMiddleware()` registers middleware in correct order:
  1. CorrelationMiddleware (sets IDs)
  2. TenantEnrichmentMiddleware (adds context to scope)
  3. RequestLoggingMiddleware (logs with full context)

## Verification Results

| Check | Result |
|-------|--------|
| Build succeeds | PASS (0 errors, 0 warnings) |
| [LoggerMessage] in RequestLoggingMessages | PASS |
| UseDhadgarMiddleware registers correct order | PASS |
| TenantEnrichmentMiddleware has all scope fields | PASS |
| Gateway tests pass | PASS (117/117) |
| ServiceDefaults tests pass | PASS (3/3) |

## How to Use

### In Program.cs (Simple Path)
```csharp
var builder = WebApplication.CreateBuilder(args);

// Register services (includes RequestLoggingMessages and IOrganizationContext)
builder.Services.AddDhadgarServiceDefaults();

var app = builder.Build();

// Register middleware in correct order
app.UseDhadgarMiddleware();

// Rest of pipeline...
app.Run();
```

### For Custom Middleware Order (like Gateway)
```csharp
// Register RequestLoggingMessages manually
builder.Services.AddSingleton<RequestLoggingMessages>();

// Use middleware individually in custom order
app.UseMiddleware<CorrelationMiddleware>();
// ... other middleware ...
app.UseMiddleware<RequestLoggingMiddleware>();
```

### For Background Services
```csharp
// Create scope manually with same fields
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["TenantId"] = tenantId,
    ["CorrelationId"] = correlationId,
    ["ServiceName"] = TenantEnrichmentMiddleware.ServiceInfo.Name,
    ["ServiceVersion"] = TenantEnrichmentMiddleware.ServiceInfo.Version,
    ["Hostname"] = TenantEnrichmentMiddleware.ServiceInfo.Hostname
}))
{
    // Background work - all logs include context
}
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated Gateway tests to use RequestLoggingMessages**
- **Found during:** Full solution build
- **Issue:** Gateway tests created RequestLoggingMiddleware with ILogger, but signature changed to RequestLoggingMessages
- **Fix:** Updated test file to create RequestLoggingMessages with NullLogger<RequestLoggingMessages>
- **Files modified:** tests/Dhadgar.Gateway.Tests/MiddlewareUnitTests.cs
- **Commit:** Included in final commit

**2. [Rule 3 - Blocking] Registered RequestLoggingMessages in Gateway Program.cs**
- **Found during:** Gateway integration tests
- **Issue:** Gateway uses custom middleware order and doesn't call AddDhadgarServiceDefaults(), so RequestLoggingMessages wasn't registered
- **Fix:** Added explicit registration: `builder.Services.AddSingleton<RequestLoggingMessages>()`
- **Files modified:** src/Dhadgar.Gateway/Program.cs
- **Commit:** Included in final commit

## Next Phase Readiness

This plan provides the foundation for:
- **01-03-PLAN:** Service rollout to pilot services using new logging infrastructure

### Integration Points
Services can now:
1. Call `builder.Services.AddDhadgarServiceDefaults()` for full setup
2. Call `app.UseDhadgarMiddleware()` for standard middleware pipeline
3. All logs automatically include TenantId, CorrelationId, ServiceName, ServiceVersion, Hostname
4. HTTP request logging uses high-performance source-generated methods

### Blockers
None identified. All infrastructure is in place.
