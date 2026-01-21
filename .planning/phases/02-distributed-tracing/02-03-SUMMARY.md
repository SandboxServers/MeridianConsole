---
phase: 02-distributed-tracing
plan: 03
subsystem: tracing
tags: [opentelemetry, activitysource, traceid, problem-details, error-handling]

# Dependency graph
requires:
  - phase: 02-distributed-tracing
    provides: "TracingExtensions.AddDhadgarTracing() for OTEL configuration"
provides:
  - DhadgarActivitySource for custom business spans
  - TraceId in Problem Details error responses
  - Integration tests for TraceId correlation
affects: [all-services, error-handling, debugging]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DhadgarActivitySource.StartActivity() for custom spans"
    - "Activity.Current?.TraceId for error correlation"

key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Tracing/DhadgarActivitySource.cs
    - tests/Dhadgar.ServiceDefaults.Tests/Middleware/ProblemDetailsMiddlewareTests.cs
  modified:
    - src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingExtensions.cs
    - src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs

key-decisions:
  - "Use 'Dhadgar.Operations' as shared ActivitySource name for business spans"
  - "TraceId fallback chain: Activity.TraceId -> CorrelationId -> TraceIdentifier -> 'unknown'"

patterns-established:
  - "DhadgarActivitySource.StartActivity() for custom business spans with using statement"
  - "Activity.Current?.TraceId for correlating errors with traces"

# Metrics
duration: 3min
completed: 2026-01-21
---

# Phase 2 Plan 3: Custom Spans and Error Correlation Summary

**DhadgarActivitySource for custom business spans with TraceId propagation to Problem Details error responses for trace correlation**

## Performance

- **Duration:** 3 min
- **Started:** 2026-01-21T17:51:08Z
- **Completed:** 2026-01-21T17:54:15Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments
- Created DhadgarActivitySource providing static ActivitySource 'Dhadgar.Operations' for custom spans
- Updated ProblemDetailsMiddleware to use Activity.Current?.TraceId for error correlation
- Added comprehensive integration tests verifying TraceId appears in error responses
- Registered DhadgarActivitySource in AddDhadgarTracing() for automatic OTEL export

## Task Commits

Each task was committed atomically:

1. **Task 1: Create shared ActivitySource for custom spans** - `d58f50b` (feat)
2. **Task 2: Update ProblemDetailsMiddleware with TraceId** - `6a244fc` (feat)
3. **Task 3: Add integration tests for TraceId in error responses** - `eeb99de` (test)

## Files Created/Modified

- `src/Shared/Dhadgar.ServiceDefaults/Tracing/DhadgarActivitySource.cs` - Static ActivitySource with StartActivity() methods for custom business spans
- `src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingExtensions.cs` - Added DhadgarActivitySource registration in AddDhadgarTracing()
- `src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs` - Uses Activity.Current?.TraceId with fallback chain
- `tests/Dhadgar.ServiceDefaults.Tests/Middleware/ProblemDetailsMiddlewareTests.cs` - 5 integration tests for TraceId in error responses

## Decisions Made

1. **ActivitySource naming**: Used "Dhadgar.Operations" as the shared ActivitySource name, following the existing pattern from TracingConstants.ActivitySourcePrefix

2. **TraceId fallback chain**: Implemented priority order Activity.TraceId -> CorrelationId -> TraceIdentifier -> "unknown" to ensure error responses always include a correlation identifier, with preference for OTEL trace IDs when available

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 2 (Distributed Tracing) is now complete with all requirements met:
- TRACE-01: Centralized tracing configuration (02-01)
- TRACE-02: Pilot service integration with Redis (02-02)
- TRACE-03: Custom span support via DhadgarActivitySource (02-03)
- TRACE-04: TraceId in error responses (02-03)

Ready for Phase 3 (Audit Logging) or Phase 4 (Health & Alerting) - these can proceed in parallel per research recommendations.

---
*Phase: 02-distributed-tracing*
*Completed: 2026-01-21*
