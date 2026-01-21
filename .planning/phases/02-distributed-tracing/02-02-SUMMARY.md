---
phase: 02-distributed-tracing
plan: 02
subsystem: observability
tags: [opentelemetry, tracing, efcore, redis, instrumentation, postgresql]

# Dependency graph
requires:
  - phase: 02-01
    provides: AddDhadgarTracing() extension method, TracingConstants
provides:
  - EF Core instrumented Servers service with db.system tags
  - EF Core + Redis instrumented Identity service
  - Pattern for instrumenting database-backed services
affects: [02-03, 03-audit-logging, 04-health-alerting]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Service-specific tracing via callback parameter
    - Redis instrumentation via AddRedisInstrumentation() in callback

key-files:
  created: []
  modified:
    - src/Dhadgar.Servers/Program.cs
    - src/Dhadgar.Identity/Program.cs
    - src/Dhadgar.Identity/Dhadgar.Identity.csproj

key-decisions:
  - "Identity requires explicit OpenTelemetry.Instrumentation.StackExchangeRedis package reference"
  - "Redis instrumentation uses OpenTelemetry.Trace namespace (not separate Redis namespace)"
  - "Keep metrics configuration intact while replacing tracing block"

patterns-established:
  - "AddDhadgarTracing() call replaces inline WithTracing() block"
  - "Redis-using services add AddRedisInstrumentation() via callback"

# Metrics
duration: 6min
completed: 2026-01-21
---

# Phase 2 Plan 2: Pilot Service Integration Summary

**EF Core and Redis tracing wired to Servers and Identity services using centralized AddDhadgarTracing() with db.system tags for PostgreSQL identification**

## Performance

- **Duration:** 6 min
- **Started:** 2026-01-21T17:43:16Z
- **Completed:** 2026-01-21T17:49:39Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Servers service now uses AddDhadgarTracing() for centralized EF Core instrumentation
- Identity service uses AddDhadgarTracing() with Redis callback for cache operation spans
- Both services maintain existing metrics configuration
- Pattern established for remaining service rollout in Plan 02-03

## Task Commits

Each task was committed atomically:

1. **Task 1: Wire tracing to Servers service** - `5aad767` (feat)
2. **Task 2: Wire tracing with Redis to Identity service** - `1ced6ce` (feat)

## Files Created/Modified
- `src/Dhadgar.Servers/Program.cs` - Replaced inline tracing with AddDhadgarTracing()
- `src/Dhadgar.Identity/Program.cs` - Added AddDhadgarTracing() with Redis instrumentation callback
- `src/Dhadgar.Identity/Dhadgar.Identity.csproj` - Added OpenTelemetry.Instrumentation.StackExchangeRedis package

## Decisions Made
- **Package reference required:** Identity service needed explicit OpenTelemetry.Instrumentation.StackExchangeRedis package reference since NuGet packages are not transitively available from ServiceDefaults
- **Namespace for Redis:** The AddRedisInstrumentation() extension is in `OpenTelemetry.Trace` namespace, not a separate Redis namespace
- **Preserved metrics:** Both services keep their existing `.WithMetrics()` blocks unchanged as metrics are not yet centralized

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added missing package reference for Redis instrumentation**
- **Found during:** Task 2 (Identity Redis tracing)
- **Issue:** Build failed - AddRedisInstrumentation() not accessible despite ServiceDefaults having the package
- **Fix:** Added explicit PackageReference for OpenTelemetry.Instrumentation.StackExchangeRedis to Identity.csproj
- **Files modified:** src/Dhadgar.Identity/Dhadgar.Identity.csproj
- **Verification:** Build succeeds after adding package reference
- **Committed in:** 1ced6ce (Task 2 commit)

**2. [Rule 3 - Blocking] Corrected namespace for Redis instrumentation**
- **Found during:** Task 2 (Identity Redis tracing)
- **Issue:** Initially tried `OpenTelemetry.Instrumentation.StackExchangeRedis` namespace which doesn't exist
- **Fix:** Changed to `OpenTelemetry.Trace` which contains the extension method
- **Files modified:** src/Dhadgar.Identity/Program.cs
- **Verification:** Build succeeds with correct namespace
- **Committed in:** 1ced6ce (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both auto-fixes necessary to complete Task 2. Package transitivity and namespace discovery issues are common when wiring instrumentation. No scope creep.

## Issues Encountered
None - deviations were handled automatically as blocking issues.

## User Setup Required
None - no external service configuration required. Services can optionally configure `OpenTelemetry:OtlpEndpoint` in user secrets to enable OTLP export.

## Next Phase Readiness
- Pilot services (Servers, Identity) successfully instrumented
- Ready for remaining service rollout in Plan 02-03
- Pattern established: replace WithTracing() with AddDhadgarTracing(), add Redis callback if needed
- Services requiring Redis instrumentation will need the package reference added to their csproj

---
*Phase: 02-distributed-tracing*
*Completed: 2026-01-21*
