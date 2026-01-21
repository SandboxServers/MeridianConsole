---
phase: 01-logging-foundation
plan: 01
subsystem: observability
tags: [logging, redaction, pii, opentelemetry, compliance]
dependency-graph:
  requires: []
  provides: [DhadgarDataClassifications, LogCategories, EmailRedactor, TokenRedactor, ConnectionStringRedactor, LoggingExtensions]
  affects: [01-02-PLAN, all-services]
tech-stack:
  added: [Microsoft.Extensions.Compliance.Redaction 10.2.0, Microsoft.Extensions.Telemetry 10.2.0]
  patterns: [source-generated-logging, pii-redaction, data-classification-taxonomy]
key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Logging/DhadgarDataClassifications.cs
    - src/Shared/Dhadgar.ServiceDefaults/Logging/LogCategories.cs
    - src/Shared/Dhadgar.ServiceDefaults/Logging/LoggingExtensions.cs
    - src/Shared/Dhadgar.ServiceDefaults/Logging/Redactors/EmailRedactor.cs
    - src/Shared/Dhadgar.ServiceDefaults/Logging/Redactors/TokenRedactor.cs
    - src/Shared/Dhadgar.ServiceDefaults/Logging/Redactors/ConnectionStringRedactor.cs
  modified:
    - Directory.Packages.props
    - src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj
decisions:
  - decision: Use "Events" suffix for LogCategories nested classes
    rationale: Avoids CA1724 warnings about naming conflicts with .NET namespaces
    impact: ServerEvents, AuthEvents, NodeEvents, TaskEvents, SecurityEvents, FileEvents, ModEvents, BillingEvents, InfraEvents
  - decision: Use constant-length email redaction ("***@***.***")
    rationale: Prevents domain inference attacks and length-based identification
    impact: All logged emails show same pattern regardless of actual email
  - decision: Include token length hint in redaction
    rationale: Helps debug truncated/malformed tokens without exposing actual value
    impact: "[REDACTED-TOKEN:len=N]" format
  - decision: Preserve Host/Database in connection string redaction
    rationale: Enables troubleshooting connection issues without credential exposure
    impact: Credentials fully redacted, host info visible
metrics:
  duration: 5 minutes
  completed: 2026-01-21
---

# Phase 01 Plan 01: Core Logging Infrastructure Summary

**One-liner:** PII redaction framework with Email/Token/ConnectionString redactors, data classification taxonomy, and OpenTelemetry logging extensions

## What Was Built

This plan established the foundational logging infrastructure in ServiceDefaults that all Dhadgar services will inherit. The key components are:

### 1. Data Classification Taxonomy (DhadgarDataClassifications.cs)
- Defines six data classifications: Email, Token, Password, ConnectionString, ApiKey, IpAddress
- Each classification has a convenience attribute (e.g., `[EmailData]`) for use with `[LoggerMessage]`
- Uses the standard Microsoft.Extensions.Compliance.Classification pattern

### 2. EventId Conventions (LogCategories.cs)
- Allocates EventId ranges to each service domain to prevent collisions:
  - 1000-1999: ServerEvents (game server lifecycle)
  - 2000-2999: AuthEvents (authentication/identity)
  - 3000-3999: NodeEvents (hardware node management)
  - 4000-4999: TaskEvents (job orchestration)
  - 5000-5999: SecurityEvents (reserved for existing SecurityEventLogger)
  - 6000-6999: FileEvents (file operations)
  - 7000-7999: ModEvents (mod management)
  - 8000-8999: BillingEvents (subscriptions/payments)
  - 9000-9999: InfraEvents (infrastructure/system)

### 3. Custom Redactors (Logging/Redactors/)
- **EmailRedactor**: Outputs constant `***@***.***` for all emails
- **TokenRedactor**: Outputs `[REDACTED-TOKEN:len=N]` with length hint
- **ConnectionStringRedactor**: Preserves Host/Port/Database, redacts credentials

### 4. Logging Extensions (LoggingExtensions.cs)
- `AddDhadgarLogging(IServiceCollection)`: Registers all redaction services and ISecurityEventLogger
- `AddDhadgarLogging(ILoggingBuilder, string, IConfiguration)`: Configures OpenTelemetry logging with:
  - Redaction enabled via `EnableRedaction()`
  - `IncludeScopes = true` for correlation/tenant context
  - OTLP exporter when `OpenTelemetry:OtlpEndpoint` is configured

## Verification Results

| Check | Result |
|-------|--------|
| Build succeeds | PASS (0 errors, 0 warnings) |
| Packages in Directory.Packages.props | PASS |
| File structure correct | PASS |
| Existing tests still pass | PASS (3/3) |

## How to Use

### In Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add redaction and security logging services
builder.Services.AddDhadgarLogging();

// Configure OpenTelemetry logging with redaction
builder.Logging.AddDhadgarLogging("Dhadgar.Servers", builder.Configuration);
```

### In LoggerMessage definitions
```csharp
[LoggerMessage(
    EventId = LogCategories.AuthEvents.LoginSucceeded,
    Level = LogLevel.Information,
    Message = "User {UserId} authenticated from {Email}")]
public partial void UserAuthenticated(
    Guid userId,
    [EmailData] string email);  // Will be redacted to "***@***.***"
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Renamed nested classes in LogCategories**
- **Found during:** Task 1 verification
- **Issue:** CA1724 warnings about naming conflicts with .NET namespaces (Tasks vs System.Threading.Tasks, etc.)
- **Fix:** Added "Events" suffix to all nested classes (e.g., `Tasks` -> `TaskEvents`)
- **Files modified:** LogCategories.cs
- **Commit:** 278effa

## Next Phase Readiness

This plan provides the foundation for:
- **01-02-PLAN:** Middleware updates to use source-generated logging with data classifications
- **01-03-PLAN:** Service rollout to pilot services

### Integration Points
Services can start using these components immediately by:
1. Adding `builder.Services.AddDhadgarLogging()` to Program.cs
2. Adding `builder.Logging.AddDhadgarLogging(serviceName, configuration)` to Program.cs
3. Converting existing logging to `[LoggerMessage]` with data classification attributes

### Blockers
None identified. All infrastructure is in place.
