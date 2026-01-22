# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-01-21)

**Core value:** Complete observability—debug any request end-to-end, audit any action for compliance, get alerted proactively
**Current focus:** Phase 5 - Error Handling (Wave 1)

## Current Position

Phase: 5 of 5 (Error Handling) - IN PROGRESS
Plan: 1 of 2 in current phase
Status: In progress
Last activity: 2026-01-22 - Completed 05-01-PLAN.md (Error Handling Infrastructure)

Progress: [█████████████] 93% (13/14 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 13
- Average duration: 9.4 minutes
- Total execution time: 122 minutes

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1. Logging Foundation | 3/3 | 20 min | 6.7 min |
| 2. Distributed Tracing | 3/3 | 11 min | 3.7 min |
| 3. Audit Logging | 3/3 | 56 min | 18.7 min |
| 4. Health & Alerting | 3/3 | 23 min | 7.7 min |
| 5. Error Handling | 1/2 | 12 min | 12.0 min |

**Recent Trend:**
- Last 5 plans: 04-01 (6 min), 04-02 (12 min), 04-03 (5 min), 05-01 (12 min)
- Trend: Consistent execution times across infrastructure plans

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Research recommends keeping Microsoft.Extensions.Logging (no Serilog migration)
- [Roadmap]: MassTransit 8.3.6 has native OTEL support, deprecated instrumentation package not needed
- [Roadmap]: Phases 3 (Audit) and 4 (Health/Alerting) can run in parallel
- [01-01]: Use "Events" suffix for LogCategories nested classes to avoid CA1724 warnings
- [01-01]: Use constant-length email redaction ("***@***.***") to prevent inference attacks
- [01-01]: Include token length hint in redaction for debugging truncated tokens
- [01-02]: Use EventId range 9100-9199 for HTTP request logging (InfraEvents subset)
- [01-02]: Cache ServiceInfo at process startup to avoid reflection overhead
- [01-02]: Use "system" as default TenantId when no organization context available
- [01-03]: Gateway uses individual middleware registration (complex pipeline)
- [01-03]: Servers uses UseDhadgarMiddleware extension (standard pipeline)
- [02-01]: Use callback parameter for service-specific tracing (Redis, custom sources)
- [02-01]: Set db.system tag via EnrichWithIDbCommand for PostgreSQL identification
- [02-02]: Services using Redis need explicit OpenTelemetry.Instrumentation.StackExchangeRedis package reference
- [02-02]: Redis instrumentation extension is in OpenTelemetry.Trace namespace
- [02-03]: Use 'Dhadgar.Operations' as shared ActivitySource name for business spans
- [02-03]: TraceId fallback chain: Activity.TraceId -> CorrelationId -> TraceIdentifier -> 'unknown'
- [03-01]: Use static AuditMessages class (no instance state, direct calls without DI)
- [03-01]: Generic TContext constraint maintains database-per-service pattern
- [03-01]: EventId range 9200-9229 for audit logging
- [03-02]: Identical ApiAuditRecordConfiguration in both services (consistent indexing)
- [03-02]: Identity has two audit tables: audit_events (domain) and api_audit_records (HTTP)
- [03-03]: SQLite for cleanup tests (InMemory doesn't support ExecuteDeleteAsync)
- [03-03]: Direct algorithm testing for sealed AuditCleanupService (can't subclass)
- [03-03]: Skip auth-dependent integration tests until UseAuthentication added to services
- [04-01]: RabbitMQ health check uses async factory (RabbitMQ.Client 7.x API change)
- [04-01]: Gateway keeps existing YARP readiness check alongside Redis health check
- [04-01]: Liveness probes include only self check (no external dependencies)
- [04-01]: Readiness probes have 2-3 second timeouts to fail fast
- [04-02]: Use StringBuilder for HTML email body (raw string literals with CSS braces problematic)
- [04-02]: Alert throttle key: ServiceName + Title + ExceptionType (deduplication granularity)
- [04-02]: Dispatch to Discord and email in parallel (no dependency between channels)
- [04-02]: Graceful degradation when Discord/email not configured (log debug, no exception)
- [04-03]: Use Loki log queries for Grafana alert rules (logs already available)
- [04-03]: Critical alerts 0s delay, warnings 2m pending period
- [04-03]: Severity-based routing: critical 1h repeat, warnings 4h repeat
- [05-01]: Use TryAddSingleton for TimeProvider to allow test overrides
- [05-01]: Use JsonSerializer.Serialize instead of WriteAsJsonAsync to preserve content type
- [05-01]: EventId 9300 for exception logging (avoids conflict with audit 9200-9229)

### Pending Todos

None yet.

### Blockers/Concerns

- [Research]: Avoid synchronous audit writes at scale - RESOLVED with Channel + BackgroundService pattern in 03-01

## Session Continuity

Last session: 2026-01-22
Stopped at: Completed 05-01-PLAN.md (Error Handling Infrastructure)
Resume file: .planning/phases/05-error-handling/05-02-PLAN.md (next plan)
