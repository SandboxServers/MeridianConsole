---
phase: 05-error-handling
plan: 02
subsystem: api
tags: [rfc9457, problem-details, error-responses, integration-tests, endpoints]

# Dependency graph
requires:
  - phase: 05-01
    provides: Exception taxonomy, GlobalExceptionHandler, ErrorHandlingExtensions
provides:
  - Secrets endpoints migrated to RFC 9457 Problem Details
  - Problem Details integration tests for Identity and Secrets
  - Consistent error response format with trace context
affects: [all-services, api-consumers]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Results.Problem() for error responses with type URIs
    - Error type URIs follow meridian.console/errors pattern
    - Integration tests verify Problem Details format

key-files:
  created:
    - tests/Dhadgar.Identity.Tests/Endpoints/ProblemDetailsIntegrationTests.cs
    - tests/Dhadgar.Secrets.Tests/Endpoints/ProblemDetailsIntegrationTests.cs
  modified:
    - src/Dhadgar.Secrets/Endpoints/KeyVaultEndpoints.cs
    - src/Dhadgar.Secrets/Endpoints/SecretWriteEndpoints.cs
    - src/Dhadgar.Secrets/Endpoints/CertificateEndpoints.cs
    - src/Dhadgar.Secrets/Endpoints/SecretsEndpoints.cs
    - tests/Dhadgar.Secrets.Tests/Security/SecretsSecurityIntegrationTests.cs

key-decisions:
  - "Identity endpoints already use Results.Problem() - no migration needed"
  - "Secrets endpoints migrated from anonymous objects to Problem Details"
  - "Tests focus on TokenExchange endpoint for Identity (explicit Results.Problem)"
  - "Add RequestLoggingMessages to SecureSecretsWebApplicationFactory for test support"

patterns-established:
  - "Results.Problem() with type: https://meridian.console/errors/{error-type}"
  - "Error types: bad-request, validation, not-found, conflict, forbidden"
  - "Integration tests verify content type, trace context, and type URI"

# Metrics
duration: 14min
completed: 2026-01-22
---

# Phase 5 Plan 2: Service Integration Summary

**Secrets endpoints migrated to RFC 9457 Problem Details, with 16 integration tests verifying trace context in error responses**

## Performance

- **Duration:** 14 min
- **Started:** 2026-01-22T18:46:32Z
- **Completed:** 2026-01-22T19:00:32Z
- **Tasks:** 3
- **Files modified:** 7

## Accomplishments

- Migrated 20 Secrets endpoint error responses from anonymous objects to Results.Problem()
- Created 7 integration tests for Identity ProblemDetails verification
- Created 9 integration tests for Secrets ProblemDetails verification
- All error responses include traceId, correlationId, and timestamp extensions

## Task Commits

Each task was committed atomically:

1. **Task 1: Register error handling** - Already complete (05-01 configured both services)
2. **Task 2: Migrate Secrets endpoints** - `097f229` (feat)
3. **Task 3: Add integration tests** - `0127404` (test)

## Files Created/Modified

- `src/Dhadgar.Secrets/Endpoints/KeyVaultEndpoints.cs` - 8 error patterns migrated to Problem Details
- `src/Dhadgar.Secrets/Endpoints/SecretWriteEndpoints.cs` - 2 error patterns migrated
- `src/Dhadgar.Secrets/Endpoints/CertificateEndpoints.cs` - 8 error patterns migrated
- `src/Dhadgar.Secrets/Endpoints/SecretsEndpoints.cs` - 2 error patterns migrated
- `tests/Dhadgar.Identity.Tests/Endpoints/ProblemDetailsIntegrationTests.cs` - 7 integration tests
- `tests/Dhadgar.Secrets.Tests/Endpoints/ProblemDetailsIntegrationTests.cs` - 9 integration tests
- `tests/Dhadgar.Secrets.Tests/Security/SecretsSecurityIntegrationTests.cs` - Added RequestLoggingMessages registration

## Decisions Made

1. **Identity endpoints already compliant:** The Identity service endpoints already use Results.Problem() consistently - no migration was needed. Task 2 focus shifted to Secrets only.

2. **Test focus on TokenExchange:** Identity ProblemDetails tests focus on TokenExchange endpoint because it uses explicit Results.Problem() calls. Organization endpoints return Problem Details via StatusCodePages middleware which works correctly but uses different type URIs.

3. **RequestLoggingMessages in test factory:** The SecretsSecurityIntegrationTests factory needed RequestLoggingMessages registered to support the RequestLoggingMiddleware.

4. **Error type URIs:** Consistent URI pattern `https://meridian.console/errors/{type}` where type is: bad-request, validation, not-found, conflict, forbidden, unauthorized.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] RequestLoggingMessages registration in test factory**
- **Found during:** Task 3 (Integration tests)
- **Issue:** Tests failed with "Unable to resolve service for type 'RequestLoggingMessages'"
- **Fix:** Added `services.AddSingleton<RequestLoggingMessages>()` to SecureSecretsWebApplicationFactory
- **Files modified:** tests/Dhadgar.Secrets.Tests/Security/SecretsSecurityIntegrationTests.cs
- **Verification:** All Secrets tests pass
- **Committed in:** 0127404 (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (blocking issue)
**Impact on plan:** Minor test infrastructure fix, no scope creep.

## Issues Encountered

1. **URL encoding for path traversal tests:** Initial tests used `../invalid` directly which got normalized by ASP.NET Core routing. Fixed by using `Uri.EscapeDataString()` to encode the path properly.

2. **Identity endpoint response format:** Some Identity endpoints return `application/json` via Results.Ok() for success and use StatusCodePages middleware for errors. Tests were adjusted to focus on endpoints with explicit Results.Problem() calls (TokenExchange).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Error handling infrastructure complete across Identity and Secrets services
- Phase 5 (Error Handling) objectives achieved:
  - ERR-01: Exception taxonomy with DomainException hierarchy
  - ERR-04: RFC 9457 Problem Details responses with trace context
- Services ready for production error handling with consistent API responses

---
*Phase: 05-error-handling*
*Completed: 2026-01-22*
