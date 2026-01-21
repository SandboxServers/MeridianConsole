# Roadmap: v0.1.0 Centralized Logging & Auditing

## Overview

This milestone establishes comprehensive observability infrastructure across all 13+ microservices. Starting with logging foundation (standardized levels, source generation, context enrichment), we add distributed tracing for database and cache operations, implement a queryable audit system for compliance, configure health checks and alerting for proactive monitoring, and unify error handling with RFC 9457 Problem Details.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Logging Foundation** - Standardized logging with source generation and context enrichment
- [x] **Phase 2: Distributed Tracing** - EF Core, Redis, and custom operation spans
- [ ] **Phase 3: Audit System** - Database-backed audit trail for compliance queries
- [ ] **Phase 4: Health & Alerting** - Health checks and proactive Discord/email alerts
- [ ] **Phase 5: Error Handling** - RFC 9457 Problem Details with trace context

**Note:** Phases 3 and 4 can execute in parallel after Phase 2 completes.

## Phase Details

### Phase 1: Logging Foundation
**Goal**: All services produce consistent, enriched, secure logs that can be traced across service boundaries
**Depends on**: Nothing (first phase)
**Requirements**: LOG-01, LOG-02, LOG-03, LOG-04, LOG-05, LOG-06
**Success Criteria** (what must be TRUE):
  1. Logs from any service use consistent Debug/Info/Warning/Error/Critical levels
  2. All logging calls use source-generated [LoggerMessage] attributes (no runtime string interpolation)
  3. Logs containing test PII (emails, tokens) show scrubbed values (e.g., "***@***.com")
  4. Every log entry includes tenant ID, correlation ID, service name, version, and hostname
  5. Logs from a single request across multiple services can be filtered by correlation ID
**Plans**: 3 plans

Plans:
- [x] 01-01-PLAN.md — Core logging infrastructure with PII redaction and data classifications
- [x] 01-02-PLAN.md — Middleware updates for source-generated logging and context enrichment
- [x] 01-03-PLAN.md — Service rollout to pilot services and verification tests

### Phase 2: Distributed Tracing
**Goal**: Database queries, cache operations, and business operations appear as spans in distributed traces
**Depends on**: Phase 1 (needs correlation context)
**Requirements**: TRACE-01, TRACE-02, TRACE-03, TRACE-04
**Success Criteria** (what must be TRUE):
  1. EF Core queries appear as child spans in Jaeger/Grafana traces with SQL statement and duration
  2. Redis operations appear as child spans with command name and duration
  3. Developers can wrap business logic in custom spans using a simple API
  4. Error responses include TraceId that links to the distributed trace
**Plans**: 3 plans

Plans:
- [x] 02-01-PLAN.md — Core tracing infrastructure with EF Core and Redis packages
- [x] 02-02-PLAN.md — Wire instrumentation to Servers and Identity services
- [x] 02-03-PLAN.md — Custom spans via ActivitySource and TraceId in Problem Details

### Phase 3: Audit System
**Goal**: All authenticated API calls are recorded to a queryable database for compliance
**Depends on**: Phase 2 (needs trace context)
**Requirements**: AUDIT-01, AUDIT-02, AUDIT-03, AUDIT-04
**Success Criteria** (what must be TRUE):
  1. Authenticated API requests create audit records in PostgreSQL database
  2. Audit records contain: timestamp, user ID, tenant ID, action (HTTP method + path), resource ID, outcome (status code)
  3. SQL queries like "show all actions by user X in last 7 days" return correct results
  4. Records older than 90 days are automatically deleted by background job
**Plans**: TBD

Plans:
- [ ] 03-01: TBD
- [ ] 03-02: TBD

### Phase 4: Health & Alerting
**Goal**: Services expose health endpoints and critical errors trigger proactive notifications
**Depends on**: Phase 2 (can run parallel with Phase 3)
**Requirements**: HEALTH-01, HEALTH-02, HEALTH-03, HEALTH-04, ALERT-01, ALERT-02, ALERT-03
**Success Criteria** (what must be TRUE):
  1. Database-backed services expose /healthz endpoint that fails if PostgreSQL is unreachable
  2. Cache-using services expose /healthz endpoint that fails if Redis is unreachable
  3. Message-using services expose /healthz endpoint that fails if RabbitMQ is unreachable
  4. Kubernetes can use liveness (/healthz/live) and readiness (/healthz/ready) probes
  5. Critical errors (Error/Critical level) trigger Discord webhook within 60 seconds
  6. Critical errors trigger email notification within 60 seconds
  7. Grafana dashboard includes pre-configured alerting rules for error rate spikes
**Plans**: TBD

Plans:
- [ ] 04-01: TBD
- [ ] 04-02: TBD
- [ ] 04-03: TBD

### Phase 5: Error Handling
**Goal**: All API errors return consistent RFC 9457 Problem Details with debugging context
**Depends on**: Phase 3, Phase 4 (needs audit and alerting in place)
**Requirements**: ERR-01, ERR-02, ERR-03, ERR-04
**Success Criteria** (what must be TRUE):
  1. All error responses return RFC 9457 Problem Details JSON with type, title, status, detail, instance
  2. Exceptions map to appropriate HTTP status codes (ValidationException -> 400, NotFoundException -> 404, etc.)
  3. Error responses in production never contain stack traces, connection strings, or internal paths
  4. Error responses include correlationId and traceId fields for cross-referencing logs and traces
**Plans**: TBD

Plans:
- [ ] 05-01: TBD
- [ ] 05-02: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 (parallel with 4) -> 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Logging Foundation | 3/3 | ✓ Complete | 2026-01-21 |
| 2. Distributed Tracing | 3/3 | ✓ Complete | 2026-01-21 |
| 3. Audit System | 0/2 | Not started | - |
| 4. Health & Alerting | 0/3 | Not started | - |
| 5. Error Handling | 0/2 | Not started | - |

---
*Roadmap created: 2026-01-21*
*Last updated: 2026-01-21 after Phase 2 execution*
