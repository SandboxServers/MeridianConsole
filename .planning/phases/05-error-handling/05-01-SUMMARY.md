---
phase: 05-error-handling
plan: 01
subsystem: api
tags: [rfc9457, problem-details, exception-handling, error-handling, asp.net-core]

# Dependency graph
requires:
  - phase: 01-logging-foundation
    provides: Correlation middleware, trace context infrastructure
provides:
  - Exception taxonomy (DomainException, ValidationException, NotFoundException, etc.)
  - GlobalExceptionHandler for centralized exception classification
  - AddDhadgarErrorHandling() and UseDhadgarErrorHandling() extensions
  - RFC 9457 Problem Details responses with trace context
affects: [05-02-service-integration, all-services]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - IExceptionHandler pattern for exception classification
    - Domain exceptions with StatusCode and ErrorType properties
    - Production-safe error responses (5xx hide internal details)

key-files:
  created:
    - src/Shared/Dhadgar.ServiceDefaults/Errors/DomainExceptions.cs
    - src/Shared/Dhadgar.ServiceDefaults/Errors/GlobalExceptionHandler.cs
    - src/Shared/Dhadgar.ServiceDefaults/Errors/ErrorHandlingExtensions.cs
    - tests/Dhadgar.ServiceDefaults.Tests/Errors/DomainExceptionTests.cs
    - tests/Dhadgar.ServiceDefaults.Tests/Errors/GlobalExceptionHandlerTests.cs
  modified:
    - src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs

key-decisions:
  - "Use TryAddSingleton for TimeProvider to allow test overrides"
  - "Use JsonSerializer.Serialize instead of WriteAsJsonAsync to preserve content type"
  - "EventId 9300 for exception logging (avoids conflict with audit 9200-9229)"

patterns-established:
  - "Domain exceptions extend DomainException with StatusCode and ErrorType"
  - "All error responses include traceId, correlationId, timestamp extensions"
  - "5xx errors hide exception.Message in production"
  - "4xx errors always include exception.Message"

# Metrics
duration: 12min
completed: 2026-01-22
---

# Phase 5 Plan 1: Error Handling Infrastructure Summary

**RFC 9457 Problem Details with exception taxonomy, trace context enrichment, and production-safe error handling for all services**

## Performance

- **Duration:** 12 min
- **Started:** 2026-01-22T18:00:00Z
- **Completed:** 2026-01-22T18:12:00Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Created exception taxonomy with 5 domain exception types mapping to HTTP status codes
- Implemented GlobalExceptionHandler with comprehensive exception classification
- Added registration extensions for easy service integration
- All error responses include traceId, correlationId, and timestamp
- Production mode hides internal error details for 5xx responses
- 41 unit tests covering exception handling infrastructure

## Task Commits

Each task was committed atomically:

1. **Task 1: Create exception taxonomy and GlobalExceptionHandler** - `8df1da1` (feat)
2. **Task 2: Create registration extensions and update middleware** - `f4d9ece` (feat)
3. **Task 3: Add unit tests for error handling infrastructure** - `3d1240d` (test)

## Files Created/Modified

- `src/Shared/Dhadgar.ServiceDefaults/Errors/DomainExceptions.cs` - Exception taxonomy (ValidationException, NotFoundException, etc.)
- `src/Shared/Dhadgar.ServiceDefaults/Errors/GlobalExceptionHandler.cs` - IExceptionHandler implementation
- `src/Shared/Dhadgar.ServiceDefaults/Errors/ErrorHandlingExtensions.cs` - AddDhadgarErrorHandling() and UseDhadgarErrorHandling()
- `src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs` - Updated with correlationId, timestamp, ProblemDetails class
- `tests/Dhadgar.ServiceDefaults.Tests/Errors/DomainExceptionTests.cs` - 17 tests for exception taxonomy
- `tests/Dhadgar.ServiceDefaults.Tests/Errors/GlobalExceptionHandlerTests.cs` - 24 tests for exception handler

## Decisions Made

1. **Use TryAddSingleton for TimeProvider:** Allows tests to inject FakeTimeProvider before AddDhadgarErrorHandling() is called
2. **JsonSerializer instead of WriteAsJsonAsync:** WriteAsJsonAsync overrides content type to application/json; using JsonSerializer.Serialize preserves application/problem+json
3. **EventId 9300 for exception logging:** Avoids conflict with audit logging range (9200-9229) established in Phase 3

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

1. **TimeProvider not registered:** GlobalExceptionHandler depends on TimeProvider which wasn't being registered by AddDhadgarErrorHandling(). Fixed by adding TryAddSingleton<TimeProvider>(TimeProvider.System).

2. **Content-Type override:** WriteAsJsonAsync sets content type to application/json, overriding our explicit setting. Fixed by using JsonSerializer.Serialize with explicit WriteAsync.

3. **Activity scope in tests:** Activity was being disposed before exception handler ran because the test middleware used `using var activity`. Fixed by moving Activity disposal to finally block.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Error handling infrastructure complete and ready for service integration
- Plan 05-02 can integrate these extensions into Gateway and Servers services
- All services will automatically get consistent error responses

---
*Phase: 05-error-handling*
*Completed: 2026-01-22*
