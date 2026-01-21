---
phase: 02-distributed-tracing
plan: 01
subsystem: observability
tags: [opentelemetry, tracing, efcore, redis, instrumentation]

# Dependency graph
requires:
  - phase: 01-logging-foundation
    provides: LoggingExtensions pattern, ServiceDefaults structure
provides:
  - OpenTelemetry EF Core and Redis instrumentation packages
  - AddDhadgarTracing() centralized extension method
  - TracingConstants with semantic conventions
affects: [02-02, 02-03, 03-audit-logging, 04-health-alerting]

# Tech tracking
tech-stack:
  added:
    - OpenTelemetry.Instrumentation.EntityFrameworkCore 1.0.0-beta.12
    - OpenTelemetry.Instrumentation.StackExchangeRedis 1.0.0-rc9.14
  patterns:
    - Centralized tracing configuration via extension method
    - Optional callback parameter for service-specific instrumentation

key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingConstants.cs
    - src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingExtensions.cs
  modified:
    - Directory.Packages.props
    - src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj

key-decisions:
  - "Use callback parameter for service-specific tracing (Redis, custom sources)"
  - "Set db.system tag via EnrichWithIDbCommand for PostgreSQL identification"
  - "Follow LoggingExtensions pattern for consistency"

patterns-established:
  - "AddDhadgar{Feature}() pattern for centralized service configuration"
  - "Callback parameter for optional service-specific extensions"

# Metrics
duration: 2min
completed: 2026-01-21
---

# Phase 2 Plan 1: Core Tracing Infrastructure Summary

**OpenTelemetry tracing infrastructure with AddDhadgarTracing() extension method configuring ASP.NET Core, HTTP client, and EF Core instrumentation**

## Performance

- **Duration:** 2 min
- **Started:** 2026-01-21T17:40:07Z
- **Completed:** 2026-01-21T17:42:01Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Added EF Core and Redis instrumentation packages to central package management
- Created TracingConstants with OpenTelemetry semantic conventions
- Created AddDhadgarTracing() extension method for centralized tracing setup
- Configured db.system tag enrichment for PostgreSQL spans

## Task Commits

Each task was committed atomically:

1. **Task 1: Add OpenTelemetry instrumentation packages** - `695bcd5` (chore)
2. **Task 2: Create tracing constants and extension method** - `98b87b8` (feat)

## Files Created/Modified
- `Directory.Packages.props` - Added EF Core and Redis instrumentation package versions
- `src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj` - Added package references for tracing
- `src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingConstants.cs` - Span naming conventions and semantic attributes
- `src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingExtensions.cs` - AddDhadgarTracing() extension method

## Decisions Made
- **Callback parameter for extensibility:** Using `Action<TracerProviderBuilder>? configureTracing` allows services to add Redis instrumentation or custom activity sources without modifying the base method
- **db.system enrichment:** Added via `EnrichWithIDbCommand` callback to tag all EF Core spans with "postgresql" for proper database identification in trace viewers
- **SetDbStatementForText = true:** Enabled so actual SQL queries appear in spans for debugging (should be disabled in production for sensitive data)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - all packages resolved correctly and build succeeded.

## User Setup Required

None - no external service configuration required. Services can optionally configure `OpenTelemetry:OtlpEndpoint` in user secrets to enable OTLP export.

## Next Phase Readiness
- Tracing infrastructure ready for service integration (Plan 02-02)
- Services can now call `builder.Services.AddDhadgarTracing()` to get automatic tracing
- Redis instrumentation available via callback for services that use caching

---
*Phase: 02-distributed-tracing*
*Completed: 2026-01-21*
