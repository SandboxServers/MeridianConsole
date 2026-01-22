---
phase: 03-audit-system
plan: 03-01
subsystem: audit
tags: [audit, middleware, background-service, channel, entity-framework]
dependency-graph:
  requires: [01-logging-foundation, 02-distributed-tracing]
  provides: [audit-infrastructure, audit-middleware, audit-queue, audit-cleanup]
  affects: [03-02-audit-integration, future-compliance-queries]
tech-stack:
  added:
    - Microsoft.EntityFrameworkCore (ServiceDefaults)
  patterns:
    - Channel + BackgroundService for non-blocking writes
    - Generic DbContext constraint for database-per-service
    - Source-generated logging for performance
key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Audit/ApiAuditRecord.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditQueue.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditMiddleware.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditWriterService.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditCleanupService.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditExtensions.cs
    - src/Shared/Dhadgar.ServiceDefaults/Audit/AuditMessages.cs
  modified:
    - src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj
decisions:
  - decision: "Use static AuditMessages class instead of injected singleton"
    rationale: "Methods don't access instance state; static allows direct calls without DI registration"
  - decision: "Generic TContext constraint on background services"
    rationale: "Services provide their own DbContext implementing IAuditDbContext, maintaining database-per-service pattern"
  - decision: "Fire-and-forget QueueAsync with CA2012 suppression"
    rationale: "Intentional non-blocking behavior per architecture decision; record loss acceptable tradeoff"
  - decision: "Source-generated regex for resource extraction"
    rationale: "GeneratedRegex provides compile-time optimization for path parsing"
metrics:
  duration: 7 minutes
  completed: 2026-01-22
---

# Phase 3 Plan 1: Core Audit Infrastructure Summary

Channel-based audit queue with middleware capture and background batch writer for 90-day retained compliance logs.

## What Was Built

### 1. ApiAuditRecord Entity
- All required fields: timestamp, user ID, tenant ID, HTTP method, path, resource ID/type, status code, duration, client IP, user agent, correlation ID, trace ID, service name
- Field length constraints matching database column limits (UserAgent: 256, Path: 500, etc.)
- XML documentation distinguishing from Identity's domain-level AuditEvent

### 2. AuditQueue (Channel-based)
- `IAuditQueue` interface with `QueueAsync`, `ReadAllAsync`, `Complete` methods
- `AuditQueue` implementation using `Channel.CreateBounded<ApiAuditRecord>`
- Configured: `BoundedChannelFullMode.Wait`, `SingleReader = true`, `SingleWriter = false`
- Default capacity: 10,000 records with backpressure

### 3. AuditMiddleware
- Captures authenticated requests only (skips unauthenticated and health endpoints)
- Extracts UserId from `sub` claim, TenantId from `org_id`/`tenant_id` claims
- Extracts ResourceType and ResourceId from path using source-generated regex
- Fire-and-forget queue call (non-blocking, ~0ms latency impact)
- Uses cached ServiceName from `TenantEnrichmentMiddleware.ServiceInfo`

### 4. AuditWriterService<TContext>
- Generic background service constrained to `DbContext, IAuditDbContext`
- Batch writes (default 100 records) from queue to database
- Drains remaining records on shutdown with `CancellationToken.None`
- Error handling: logs failure, loses records (acceptable per design)

### 5. AuditCleanupService<TContext>
- Background service for 90-day retention policy (AUDIT-04)
- Batch deletion using `ExecuteDeleteAsync` (server-side, no entity loading)
- Initial 5-minute delay, then 24-hour interval
- Configurable: interval, retention period, batch size, enabled flag

### 6. IAuditDbContext Interface
- `DbSet<ApiAuditRecord> ApiAuditRecords` property
- `Task<int> SaveChangesAsync(CancellationToken)` method
- Services implement on their DbContext to enable audit

### 7. AuditExtensions
- `AddAuditInfrastructure<TContext>()` - Registers queue, writer, cleanup
- `UseAuditMiddleware()` - Registers middleware after auth
- Combined `AuditOptions` class for easy configuration

### 8. AuditMessages
- Static source-generated logging methods (EventIds 9200-9229)
- Batch written (9200), batch failed (9201), cleanup completed (9210), cleanup failed (9211), queue full (9220)

## Key Implementation Details

**Middleware Pipeline Order:**
```
CorrelationMiddleware -> TenantEnrichmentMiddleware -> RequestLoggingMiddleware
-> UseAuthentication -> UseAuthorization
-> AuditMiddleware (NEW - after auth)
```

**Service Registration:**
```csharp
// In service's Program.cs
builder.Services.AddAuditInfrastructure<MyDbContext>(options =>
{
    options.Queue.Capacity = 20_000;
    options.Writer.BatchSize = 200;
    options.Cleanup.RetentionPeriod = TimeSpan.FromDays(60);
});

// In pipeline
app.UseAuthentication();
app.UseAuthorization();
app.UseAuditMiddleware();
```

**DbContext Implementation:**
```csharp
public class MyDbContext : DbContext, IAuditDbContext
{
    public DbSet<ApiAuditRecord> ApiAuditRecords { get; set; } = null!;

    Task<int> IAuditDbContext.SaveChangesAsync(CancellationToken ct)
        => SaveChangesAsync(ct);
}
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added Microsoft.EntityFrameworkCore package reference**
- **Found during:** Task 3 compilation
- **Issue:** DbContext, DbSet types not found
- **Fix:** Added `<PackageReference Include="Microsoft.EntityFrameworkCore" />` to ServiceDefaults.csproj
- **Commit:** e04d033

**2. [Rule 1 - Bug] Changed AuditMessages from instance to static class**
- **Found during:** Task 3 compilation
- **Issue:** CA1822 warnings - methods don't access instance state
- **Fix:** Made AuditMessages a static class with static methods accepting ILogger parameter
- **Rationale:** Static methods avoid unnecessary DI registration, follow source-gen logger pattern
- **Commit:** e04d033

## Commits

| Hash | Type | Description |
|------|------|-------------|
| b4db1f1 | feat | Create audit entity and channel-based queue |
| 7c8969c | feat | Create audit middleware for authenticated requests |
| e04d033 | feat | Add background services and DI extension |

## Verification Results

All verification patterns confirmed:

- `Channel<ApiAuditRecord>` - Found in AuditQueue.cs
- `_auditQueue.QueueAsync` - Found in AuditMiddleware.cs (fire-and-forget call)
- `_queue.ReadAllAsync` - Found in AuditWriterService.cs (batch drain)
- `BackgroundService` - Found in AuditWriterService.cs and AuditCleanupService.cs
- `RetentionPeriod` - Found with 90-day default in AuditCleanupOptions

Build: `dotnet build src/Shared/Dhadgar.ServiceDefaults` - Success (1 unrelated NuGet warning)

## Next Phase Readiness

### Ready for 03-02
- [ ] Wire Identity service to use audit infrastructure
- [ ] Add EF Core configuration for ApiAuditRecord (indexes for query patterns)
- [ ] Create migration for api_audit_records table
- [ ] Update Identity's Program.cs with AddAuditInfrastructure<IdentityDbContext>

### Integration Notes
1. Services need to add `ApiAuditRecord` to their DbContext and implement `IAuditDbContext`
2. EF Core configuration should add indexes per research: user+time, tenant+time, timestamp (cleanup), resource+time, service+time
3. Middleware order is critical: must be AFTER UseAuthentication/UseAuthorization
