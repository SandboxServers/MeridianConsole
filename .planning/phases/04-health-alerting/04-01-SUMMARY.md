---
phase: 04-health-alerting
plan: 01
subsystem: infra
tags: [health-checks, kubernetes, postgres, redis, rabbitmq, readiness, liveness]

# Dependency graph
requires:
  - phase: 01-logging-foundation
    provides: ServiceDefaults infrastructure
provides:
  - HealthCheckDependencies flags enum for service dependency declaration
  - AddDhadgarServiceDefaults overload with health check registration
  - Kubernetes-compatible /livez and /readyz endpoint separation
affects: [04-02, 04-03, all-services]

# Tech tracking
tech-stack:
  added:
    - AspNetCore.HealthChecks.NpgSql 9.0.0
    - AspNetCore.HealthChecks.Redis 9.0.0
    - AspNetCore.HealthChecks.Rabbitmq 9.0.0
  patterns:
    - Flags enum for declaring service dependencies
    - Liveness vs readiness probe separation

key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Health/HealthCheckDependencies.cs
  modified:
    - Directory.Packages.props
    - src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj
    - src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs
    - src/Dhadgar.Servers/Program.cs
    - src/Dhadgar.Notifications/Program.cs
    - src/Dhadgar.Gateway/Program.cs
    - src/Dhadgar.Tasks/Program.cs

key-decisions:
  - "RabbitMQ health check uses async factory (RabbitMQ.Client 7.x API change)"
  - "Gateway keeps existing YARP readiness check alongside Redis health check"
  - "Liveness probes include only self check (no external dependencies)"
  - "Readiness probes have 2-3 second timeouts to fail fast"

patterns-established:
  - "HealthCheckDependencies flags: declare service dependencies via enum flags"
  - "Liveness/readiness separation: /livez fast, /readyz checks dependencies"

# Metrics
duration: 6min
completed: 2026-01-22
---

# Phase 04 Plan 01: Health Check Infrastructure Summary

**Kubernetes-ready health checks with PostgreSQL, Redis, and RabbitMQ dependency monitoring via flags-based declaration**

## Performance

- **Duration:** 6 min
- **Started:** 2026-01-22T16:31:58Z
- **Completed:** 2026-01-22T16:38:00Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments

- Health check packages added to central package management (NpgSql, Redis, RabbitMQ)
- HealthCheckDependencies flags enum created for service dependency declaration
- AddDhadgarServiceDefaults overload with automatic health check registration
- Servers, Notifications, Gateway, and Tasks services wired with appropriate checks
- Liveness endpoints fast (no external checks), readiness endpoints verify dependencies

## Task Commits

Each task was committed atomically:

1. **Task 1: Add health check packages** - `705d140` (chore)
2. **Task 2: Create HealthCheckDependencies and extend ServiceDefaultsExtensions** - `3ccd7c4` (feat)
3. **Task 3: Wire health checks to services** - `28e96b1` (feat)

## Files Created/Modified

- `src/Shared/Dhadgar.ServiceDefaults/Health/HealthCheckDependencies.cs` - Flags enum (None, Postgres, Redis, RabbitMq)
- `Directory.Packages.props` - Added 3 health check package versions
- `src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj` - Package references
- `src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs` - New overload with dependency registration
- `src/Dhadgar.Servers/Program.cs` - Postgres health check
- `src/Dhadgar.Notifications/Program.cs` - Postgres + RabbitMQ health checks
- `src/Dhadgar.Gateway/Program.cs` - Redis health check (with existing YARP check)
- `src/Dhadgar.Tasks/Program.cs` - Postgres + RabbitMQ health checks

## Decisions Made

1. **RabbitMQ.Client 7.x API**: The AspNetCore.HealthChecks.Rabbitmq 9.0.0 package requires async connection factory due to RabbitMQ.Client 7.x changes. Used `factory: async _ => { ... }` pattern instead of deprecated connection string parameter.

2. **Gateway health check approach**: Gateway already had manual health check registration with `YarpReadinessCheck`. Added Redis check alongside existing setup rather than converting to `AddDhadgarServiceDefaults` to preserve YARP check and avoid disrupting the complex Gateway pipeline.

3. **Health check timeouts**: PostgreSQL and RabbitMQ use 3-second timeout, Redis uses 2-second timeout. Fast enough for K8s probes, slow enough for realistic connection checks.

4. **Liveness isolation**: Only "self" check tagged with "live" - external dependency failures should NOT cause pod restarts, only mark as unready.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] RabbitMQ health check API change**
- **Found during:** Task 2 (ServiceDefaultsExtensions implementation)
- **Issue:** Plan specified `rabbitConnectionString: connectionUri` parameter, but AspNetCore.HealthChecks.Rabbitmq 9.0.0 changed API - no longer accepts connection string directly
- **Fix:** Used async factory pattern: `factory: async _ => { var connectionFactory = new ConnectionFactory {...}; return await connectionFactory.CreateConnectionAsync(); }`
- **Files modified:** ServiceDefaultsExtensions.cs
- **Verification:** Build succeeds, RabbitMQ.Client 7.x compatible
- **Committed in:** 3ccd7c4 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** API change was necessary for library compatibility. No scope creep.

## Issues Encountered

None beyond the RabbitMQ API change documented above.

## User Setup Required

None - no external service configuration required. Health checks use existing connection strings from appsettings.json.

## Next Phase Readiness

- Health check infrastructure complete and tested
- Services now expose Kubernetes-compatible /livez and /readyz endpoints
- Ready for: Alert processing and notification routing (04-02)
- Ready for: Dashboard alerting (04-03)

---
*Phase: 04-health-alerting*
*Completed: 2026-01-22*
