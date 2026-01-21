# Research Summary: .NET Observability, Logging & Auditing Stack

**Domain:** Centralized observability and auditing for .NET 10 microservices
**Researched:** 2026-01-20
**Overall Confidence:** HIGH

## Executive Summary

Meridian Console already has a solid observability foundation with OpenTelemetry 1.14.0, Grafana/Prometheus/Loki stack, and well-designed audit logging patterns (`ISecurityEventLogger`, `ISecretsAuditLogger`). The path forward is **evolution, not revolution**:

1. **Keep Microsoft.Extensions.Logging** - Your source-generated `[LoggerMessage]` pattern is best-in-class
2. **Extend OpenTelemetry instrumentation** - Add EF Core and Redis tracing (MassTransit already has native support)
3. **Expand audit logging to database** - Build on existing `ISecurityEventLogger` pattern
4. **Add comprehensive health checks** - AspNetCore.Diagnostics.HealthChecks for all dependencies
5. **Enhance error handling** - RFC 9457 Problem Details + FluentValidation

The critical insight is that **your existing codebase is already well-architected for observability**. The middleware pipeline (Correlation -> ProblemDetails -> RequestLogging) is correct. The work ahead is filling gaps, not rearchitecting.

## Key Findings

**Stack:** Keep Microsoft.Extensions.Logging + OpenTelemetry (no Serilog needed). Add EF Core/Redis instrumentation, health check packages, and FluentValidation.

**Architecture:** Existing middleware pipeline is correctly ordered. Extend `ISecurityEventLogger` pattern for database-backed audit trails.

**Critical insight:** MassTransit 8.3.6 has **native OpenTelemetry support** - don't install `OpenTelemetry.Instrumentation.MassTransit` (it's deprecated and would cause duplicate spans).

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Structured Logging Enhancement
**Rationale:** Foundation for all other observability work. Low risk, high value.

- Standardize log levels across all services
- Add `[LoggerMessage]` source generators where missing
- Ensure correlation IDs flow through all log entries
- Existing middleware already handles this for HTTP - extend to MassTransit consumers

**Complexity:** Low
**Dependencies:** None

### Phase 2: Distributed Tracing Completion
**Rationale:** Builds on Phase 1 correlation IDs. Enables end-to-end request debugging.

- Add OpenTelemetry.Instrumentation.EntityFrameworkCore
- Add OpenTelemetry.Instrumentation.StackExchangeRedis
- Verify MassTransit native tracing is active
- Configure span sampling for production

**Complexity:** Low-Medium
**Dependencies:** Phase 1 (correlation)

### Phase 3: Database Audit Trail
**Rationale:** Compliance requirement. Needs design work before implementation.

- Design `AuditEvent` entity and schema
- Create shared audit abstraction in ServiceDefaults
- Implement audit interceptors for high-value operations
- Configure retention policies (90 days recommended)

**Complexity:** Medium
**Dependencies:** Phase 1 (structured logging patterns)

### Phase 4: Health Checks & Alerting
**Rationale:** Operational necessity. Can run in parallel with Phase 3.

- Add AspNetCore.HealthChecks.* packages
- Configure liveness vs readiness probes
- Create Grafana alerting rules
- Document runbook for health check failures

**Complexity:** Low-Medium
**Dependencies:** None (can parallel with Phase 3)

### Phase 5: Error Handling Standardization
**Rationale:** Improves API consistency and debugging. Benefits from tracing being in place.

- Upgrade ProblemDetailsMiddleware to RFC 9457
- Add FluentValidation with manual validation pattern
- Standardize error codes across services
- Add traceId to all error responses

**Complexity:** Medium
**Dependencies:** Phase 2 (tracing for traceId)

**Phase ordering rationale:**
- Logging (Phase 1) is foundational - everything else depends on it
- Tracing (Phase 2) enables debugging of audit/health issues
- Audit (Phase 3) and Health (Phase 4) can run in parallel
- Error handling (Phase 5) benefits from tracing being complete

**Research flags for phases:**
- Phase 3 (Audit): May need deeper research into compliance requirements (SOC2, GDPR)
- Phase 4: Health checks are straightforward - unlikely to need additional research
- Phase 5: ProblemDetails RFC 9457 is well-documented in ASP.NET Core 10

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Core Logging Stack | HIGH | Verified versions, existing codebase is well-designed |
| OpenTelemetry Instrumentation | HIGH | Verified 1.14.0 is current, MassTransit native support confirmed |
| Health Checks | HIGH | Verified 9.0.0 versions available, standard patterns |
| Audit Approach | MEDIUM | Custom approach is sound but needs compliance validation |
| FluentValidation | HIGH | Verified 12.1.1, deprecation of auto-validation confirmed |

## Gaps to Address

1. **Compliance Requirements** - Need to validate audit retention and immutability requirements against actual compliance frameworks (SOC2, GDPR, etc.)

2. **Log Retention Policy** - Current Loki setup may not have retention configured. Need to verify 90-day retention for audit logs.

3. **Sampling Strategy** - For production, need to decide on trace sampling rate to balance cost vs visibility.

4. **Alert Thresholds** - Health check alerting thresholds not defined. Need operational input.

## Files Created

| File | Purpose |
|------|---------|
| `.planning/research/STACK.md` | Technology recommendations with versions and rationale |
| `.planning/research/SUMMARY.md` | This file - executive summary with roadmap implications |

## Next Steps

Research complete. Ready for roadmap creation with the following inputs:
- 5 phases identified with clear dependencies
- Technology stack verified with current versions
- What NOT to use documented (Serilog, MassTransit OTEL package, FluentValidation.AspNetCore)
- Risk areas flagged (audit compliance needs validation)
