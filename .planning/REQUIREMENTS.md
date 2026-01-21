# Requirements: v0.1.0 Centralized Logging & Auditing

**Defined:** 2026-01-20
**Core Value:** Complete observability—debug any request end-to-end, audit any action for compliance, get alerted proactively

## v1 Requirements

Requirements for this milestone. Each maps to roadmap phases.

### Logging

- [ ] **LOG-01**: All services use standardized log levels (Debug, Info, Warning, Error, Critical)
- [ ] **LOG-02**: All services use source-generated `[LoggerMessage]` for high-performance logging
- [ ] **LOG-03**: PII and sensitive data (tokens, passwords, connection strings) are scrubbed from logs
- [ ] **LOG-04**: All log entries include tenant ID for multi-tenant isolation
- [ ] **LOG-05**: All log entries include correlation ID for request tracing
- [ ] **LOG-06**: All log entries include service context (name, version, environment, hostname)

### Tracing

- [ ] **TRACE-01**: Entity Framework Core queries appear as spans in distributed traces
- [ ] **TRACE-02**: Redis operations appear as spans in distributed traces
- [ ] **TRACE-03**: Custom business operation spans can be added for important operations
- [ ] **TRACE-04**: TraceId is included in all Problem Details error responses

### Audit

- [ ] **AUDIT-01**: All authenticated API calls are recorded to audit database
- [ ] **AUDIT-02**: Audit records include: timestamp, user ID, tenant ID, action, resource, outcome
- [ ] **AUDIT-03**: Audit database supports SQL queries ("who did what when")
- [ ] **AUDIT-04**: Audit records older than 90 days are automatically cleaned up

### Health Checks

- [ ] **HEALTH-01**: PostgreSQL health check endpoint for each database-backed service
- [ ] **HEALTH-02**: Redis health check endpoint for cache-using services
- [ ] **HEALTH-03**: RabbitMQ health check endpoint for message-using services
- [ ] **HEALTH-04**: Liveness and readiness probes are configured for Kubernetes

### Alerting

- [ ] **ALERT-01**: Critical errors trigger Discord notifications
- [ ] **ALERT-02**: Critical errors trigger email notifications
- [ ] **ALERT-03**: Grafana alerting rules are pre-configured for common failure scenarios

### Error Handling

- [ ] **ERR-01**: All API errors return RFC 9457 Problem Details format
- [ ] **ERR-02**: Exceptions are classified and mapped to appropriate HTTP status codes
- [ ] **ERR-03**: Error responses never contain sensitive data (connection strings, stack traces in production)
- [ ] **ERR-04**: Error responses include correlation ID and trace ID for debugging

## v2 Requirements

Deferred to future milestone. Tracked but not in current roadmap.

### Advanced Observability

- **OBS-01**: Adaptive sampling for production trace collection
- **OBS-02**: AI-powered anomaly detection for log patterns
- **OBS-03**: End-to-end request tracing UI (custom dashboard)

### Advanced Audit

- **AUDIT-05**: Immutable audit logs (append-only with cryptographic verification)
- **AUDIT-06**: Compliance report generation (SOC2, GDPR)

### Advanced Error Handling

- **ERR-05**: FluentValidation integration with Problem Details
- **ERR-06**: Automatic retry suggestions in error responses

## Out of Scope

| Feature | Reason |
|---------|--------|
| Serilog migration | Microsoft.Extensions.Logging with source generators is already best-in-class |
| Custom log aggregation | Loki already handles this, no need to build custom |
| OpenTelemetry.Instrumentation.MassTransit | Deprecated package, MassTransit 8+ has native support |
| Full request/response body logging | Security risk, performance impact, compliance concerns |
| Log analytics/ML | Capture and store first, analyze later |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| LOG-01 | TBD | Pending |
| LOG-02 | TBD | Pending |
| LOG-03 | TBD | Pending |
| LOG-04 | TBD | Pending |
| LOG-05 | TBD | Pending |
| LOG-06 | TBD | Pending |
| TRACE-01 | TBD | Pending |
| TRACE-02 | TBD | Pending |
| TRACE-03 | TBD | Pending |
| TRACE-04 | TBD | Pending |
| AUDIT-01 | TBD | Pending |
| AUDIT-02 | TBD | Pending |
| AUDIT-03 | TBD | Pending |
| AUDIT-04 | TBD | Pending |
| HEALTH-01 | TBD | Pending |
| HEALTH-02 | TBD | Pending |
| HEALTH-03 | TBD | Pending |
| HEALTH-04 | TBD | Pending |
| ALERT-01 | TBD | Pending |
| ALERT-02 | TBD | Pending |
| ALERT-03 | TBD | Pending |
| ERR-01 | TBD | Pending |
| ERR-02 | TBD | Pending |
| ERR-03 | TBD | Pending |
| ERR-04 | TBD | Pending |

**Coverage:**
- v1 requirements: 25 total
- Mapped to phases: 0
- Unmapped: 25

---
*Requirements defined: 2026-01-20*
*Last updated: 2026-01-20 after initial definition*
