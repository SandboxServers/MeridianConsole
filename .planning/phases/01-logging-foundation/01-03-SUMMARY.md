---
phase: 01-logging-foundation
plan: 03
subsystem: observability
tags: [logging, testing, pilot-services, integration-tests, pii-redaction]
dependency-graph:
  requires: [01-01-PLAN, 01-02-PLAN]
  provides: [pilot-service-logging, redaction-tests, logging-integration-tests]
  affects: [remaining-services-rollout]
tech-stack:
  added: []
  patterns: [integration-testing, in-memory-logger-provider, test-host-pattern]
key-files:
  created:
    - tests/Dhadgar.ServiceDefaults.Tests/Logging/RedactionTests.cs
    - tests/Dhadgar.ServiceDefaults.Tests/Logging/LoggingIntegrationTests.cs
  modified:
    - src/Dhadgar.Gateway/Program.cs
    - src/Dhadgar.Identity/Program.cs
    - src/Dhadgar.Servers/Program.cs
    - tests/Dhadgar.ServiceDefaults.Tests/Dhadgar.ServiceDefaults.Tests.csproj
decisions:
  - decision: Gateway uses individual middleware registration instead of UseDhadgarMiddleware
    rationale: Gateway has complex middleware order with custom middleware (CORS preflight, security headers, etc.)
    impact: Gateway manually registers CorrelationMiddleware, TenantEnrichmentMiddleware, RequestLoggingMiddleware
  - decision: Servers uses UseDhadgarMiddleware extension
    rationale: Servers has standard middleware pipeline, benefits from simplified setup
    impact: Cleaner Program.cs, consistent middleware order enforced
  - decision: Identity uses individual middleware registration
    rationale: Identity has custom request limits middleware and authentication middleware order requirements
    impact: Manual middleware registration allows precise ordering
metrics:
  duration: 7 minutes
  completed: 2026-01-21
---

# Phase 01 Plan 03: Service Rollout and Verification Tests Summary

**One-liner:** Pilot services (Gateway, Identity, Servers) wired to logging infrastructure with 47 new tests verifying PII redaction and context enrichment

## What Was Built

This plan rolled out the logging infrastructure from plans 01-01 and 01-02 to three pilot services and created comprehensive tests to verify the logging foundation works correctly.

### 1. Pilot Service Wiring

**Gateway (src/Dhadgar.Gateway/Program.cs):**
- Added `builder.Services.AddDhadgarLogging()` for redaction services
- Added `builder.Services.AddOrganizationContext()` for tenant context
- Added `builder.Logging.AddDhadgarLogging("Dhadgar.Gateway", builder.Configuration)`
- Inserted `TenantEnrichmentMiddleware` after `CorrelationMiddleware`
- Preserves custom middleware order for Gateway's complex pipeline

**Identity (src/Dhadgar.Identity/Program.cs):**
- Added `builder.Services.AddDhadgarLogging()` for redaction services
- Added `builder.Services.AddOrganizationContext()` for tenant context
- Added `builder.Services.AddSingleton<RequestLoggingMessages>()`
- Added `builder.Logging.AddDhadgarLogging("Dhadgar.Identity", builder.Configuration)`
- Inserted `TenantEnrichmentMiddleware` after `CorrelationMiddleware`

**Servers (src/Dhadgar.Servers/Program.cs):**
- Added `builder.Services.AddDhadgarLogging()` for redaction services
- Added `builder.Logging.AddDhadgarLogging("Dhadgar.Servers", builder.Configuration)`
- Replaced individual middleware calls with `app.UseDhadgarMiddleware()`
- Already had `AddDhadgarServiceDefaults()` which registers `IOrganizationContext`

### 2. RedactionTests (tests/Dhadgar.ServiceDefaults.Tests/Logging/RedactionTests.cs)

20 unit tests covering all three custom redactors:

**EmailRedactor Tests (5 tests):**
- Standard email returns constant `***@***.***` pattern
- Subdomain emails return same constant pattern
- Empty strings handled gracefully
- Long emails return same constant pattern
- GetRedactedLength always returns 11 (constant length)

**TokenRedactor Tests (6 tests):**
- Short tokens include length hint: `[REDACTED-TOKEN:len=6]`
- JWT-like tokens include correct length
- Empty strings return `[REDACTED-TOKEN:len=0]`
- Single character tokens handled
- Theory tests for various lengths (1, 10, 100, 1000)

**ConnectionStringRedactor Tests (9 tests):**
- PostgreSQL format: preserves Host/Port/Database, redacts credentials
- SQL Server format: extracts Server as Host, preserves Database
- Missing password: still appends `[CREDENTIALS-REDACTED]`
- Malformed strings: returns `[CONNECTION-STRING-REDACTED]`
- Empty/whitespace strings: returns fully redacted fallback
- Data Source format: correctly extracts host

### 3. LoggingIntegrationTests (tests/Dhadgar.ServiceDefaults.Tests/Logging/LoggingIntegrationTests.cs)

27 integration tests verifying end-to-end logging behavior:

**TenantEnrichmentMiddleware Tests (2 tests):**
- X-Organization-Id header is captured
- Missing header defaults to "system" TenantId

**CorrelationId Tests (3 tests):**
- Provided X-Correlation-Id is preserved in response
- Missing header generates new correlation ID
- Generated correlation ID is valid GUID

**RequestLoggingMessages Tests (4 tests):**
- 200 responses log at Information level
- 404 responses log at Warning level
- 500 responses log at Error level
- Elapsed time is included in log message

**LOG-01 Verification Tests (13 tests):**
- Theory: 200, 201, 204, 301, 302 -> Information level
- Theory: 400, 401, 403, 404, 429 -> Warning level
- Theory: 500, 502, 503, 504 -> Error level

**ServiceInfo Tests (2 tests):**
- ServiceInfo contains Name, Version, Hostname
- ServiceInfo is cached (same instance returned)

**Multiple Requests Tests (3 tests):**
- Each request gets unique X-Request-Id
- Same correlation ID preserved across requests

## Verification Results

| Check | Result |
|-------|--------|
| Gateway builds | PASS |
| Identity builds | PASS |
| Servers builds | PASS |
| Full solution builds | PASS (0 errors, 0 warnings) |
| RedactionTests (20) | PASS |
| LoggingIntegrationTests (27) | PASS |
| All ServiceDefaults tests (50) | PASS |

## Test Infrastructure

Created reusable test infrastructure in LoggingIntegrationTests:

**InMemoryLoggerProvider:**
- Implements ILoggerProvider
- Captures LogEntry records with Level, Message, Exception, Scopes
- Thread-safe using ConcurrentBag
- Exposes Entries collection for assertions

**CreateTestHostAsync helper:**
- Creates test host with TestServer
- Configures logging with InMemoryLoggerProvider
- Registers RequestLoggingMessages and IOrganizationContext
- Sets up standard middleware pipeline
- Accepts custom request handler for test scenarios

## Deviations from Plan

None - plan executed exactly as written.

## Next Phase Readiness

This plan completes Phase 1 (Logging Foundation). The logging infrastructure is now:
1. Built and tested (plans 01-01, 01-02)
2. Rolled out to pilot services (plan 01-03)
3. Verified with 47 new tests

### Ready for Next Steps
- Roll out to remaining 10 services (Tasks, Nodes, Files, Mods, Console, Billing, Notifications, Firewall, Secrets, Discord)
- Each service follows same pattern as Servers: `AddDhadgarLogging()` + `UseDhadgarMiddleware()`
- Or for services with custom middleware order: individual middleware registration like Gateway/Identity

### Test Coverage
Total ServiceDefaults tests: 50
- Existing tests: 3
- New RedactionTests: 20
- New LoggingIntegrationTests: 27

### Blockers
None identified. Logging foundation is ready for production use.
