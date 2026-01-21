# Feature Landscape: .NET Logging, Auditing, and Observability

**Domain:** Enterprise logging, audit trail, and observability for multi-tenant SaaS (game server control plane)
**Researched:** 2026-01-20
**Context:** Meridian Console - 13+ microservices, multi-tenant, compliance-aware (SOC 2 trajectory)

## Executive Summary

Enterprise logging/auditing systems have three distinct layers: **operational observability** (debugging, performance), **security auditing** (compliance, forensics), and **alerting** (proactive incident response). The codebase already has foundational pieces (correlation middleware, Problem Details, security event logger, secrets audit logger). The gap is primarily in **centralized alerting**, **tenant-aware logging**, **PII redaction**, and **structured audit persistence**.

---

## Table Stakes

Features users expect. Missing = product feels incomplete or non-compliant.

| Feature | Why Expected | Complexity | Status | Notes |
|---------|--------------|------------|--------|-------|
| **Correlation ID propagation** | Debug any request across services | Medium | DONE | `CorrelationMiddleware` in ServiceDefaults |
| **Distributed tracing (OpenTelemetry)** | Trace requests through microservices | Medium | PARTIAL | OTel configured, needs full adoption |
| **Structured logging (JSON)** | Queryable logs, SIEM integration | Low | PARTIAL | Using ILogger, needs consistent structure |
| **RFC 9457 Problem Details** | Standardized API error responses | Low | DONE | `ProblemDetailsMiddleware` implemented |
| **Request/response logging** | Debug API issues | Low | DONE | `RequestLoggingMiddleware` exists |
| **Health checks (liveness/readiness)** | Kubernetes probes, uptime monitoring | Low | PARTIAL | Basic `/healthz`, needs K8s-specific probes |
| **Log levels (Debug/Info/Warn/Error)** | Filter noise, focus on issues | Low | DONE | Standard ILogger |
| **Audit trail for auth events** | Compliance requirement (SOC 2) | Medium | DONE | `SecurityEventLogger` comprehensive |
| **Audit trail for data access** | Compliance requirement | Medium | PARTIAL | Secrets has it, other services need it |
| **Centralized log aggregation** | Single pane of glass | Medium | INFRA | Grafana/Loki in docker-compose |
| **Log retention policy** | Compliance (1-7 years for audit) | Low | TODO | Policy defined, not enforced |

### Table Stakes Details

#### Correlation ID Propagation (DONE)
The codebase has `CorrelationMiddleware` that:
- Generates/propagates X-Correlation-Id, X-Request-Id, X-Trace-Id
- Integrates with OpenTelemetry Activity
- Sets baggage for cross-service propagation
- Adds IDs to response headers

**Gap:** Needs consistent usage across all service-to-service calls (HttpClient, MassTransit).

#### RFC 9457 Problem Details (DONE)
`ProblemDetailsMiddleware` returns RFC 7807/9457 compliant responses:
- `type`, `title`, `status`, `detail`, `instance`, `traceId`
- Stack traces only in Development
- Consistent error format across all services

**Gap:** Need custom Problem Detail types for domain errors (not just 500s).

#### Security Audit Logging (DONE)
`SecurityEventLogger` covers 17 event types:
- Authentication success/failure
- Authorization denied
- Role assignment/revocation
- Privilege escalation attempts
- OAuth account linking
- Rate limit exceeded
- API key usage
- Suspicious activity

Uses source-generated `[LoggerMessage]` for performance.

---

## Differentiators

Features that set product apart. Not expected, but highly valued by enterprise customers.

| Feature | Value Proposition | Complexity | Priority | Notes |
|---------|-------------------|------------|----------|-------|
| **Tenant-aware log isolation** | Multi-tenant SaaS compliance | Medium | HIGH | Log tenant context on every entry |
| **PII/sensitive data redaction** | GDPR/CCPA compliance | Medium | HIGH | .NET has `Microsoft.Extensions.Compliance.Redaction` |
| **Real-time critical error alerting** | Proactive incident response | Medium | HIGH | PagerDuty/Slack/Discord webhooks |
| **End-to-end request tracing UI** | Debug any customer issue | High | MEDIUM | Grafana Tempo or Jaeger integration |
| **Custom business metrics** | Operational insights | Medium | MEDIUM | Prometheus counters/histograms |
| **Adaptive sampling** | Cost control at scale | Medium | MEDIUM | High-volume trace sampling |
| **AI-powered anomaly detection** | Automated issue detection | High | LOW | ML on log patterns (future) |
| **Immutable audit logs** | Tamper-proof compliance | High | LOW | Write-once storage, digital signatures |
| **Break-glass audit trail** | Emergency access tracking | Low | DONE | Secrets service has this |

### Differentiator Details

#### Tenant-Aware Log Isolation (HIGH PRIORITY)
Every log entry should include tenant context (`TenantId`, `OrganizationId`).

**Implementation approach:**
1. Extract tenant from JWT/context early in pipeline
2. Add to logging scope (already have `OrganizationContext.cs`)
3. Use Serilog enrichers or ILogger scope
4. Filter logs by tenant in aggregation layer

**Value:**
- Debug tenant-specific issues without log pollution
- Compliance: demonstrate tenant data isolation
- Security: detect cross-tenant access attempts

#### PII/Sensitive Data Redaction (HIGH PRIORITY)
Never log passwords, tokens, credit cards, etc.

**Implementation approach:**
1. Use `Microsoft.Extensions.Compliance.Redaction` (built into .NET)
2. Configure `ErasingRedactor` for passwords, `HmacRedactor` for trackable PII
3. Mark sensitive properties with `[Sensitive]` attribute
4. Integrate with Serilog destructuring policies

**Compliance impact:**
- GDPR: Right to erasure impossible if PII in logs
- SOC 2: Demonstrate data protection controls
- CCPA: Consumer data must be redactable

#### Real-Time Critical Error Alerting (HIGH PRIORITY)
Notify on-call when critical errors occur.

**Implementation approach:**
1. Custom Serilog sink or ILogger provider for critical events
2. Webhook integration: PagerDuty Events API v2, Slack Incoming Webhooks, Discord webhooks
3. Severity-based routing: Critical -> PagerDuty + Slack, Warning -> Slack only
4. Include correlation ID, service name, stack trace summary
5. Rate limiting to prevent alert storms

**Value:**
- MTTR reduction: Know about issues before customers report
- SLA compliance: Demonstrate proactive monitoring
- Customer trust: "We knew and were already working on it"

#### Custom Business Metrics (MEDIUM PRIORITY)
Beyond default HTTP metrics.

**Examples for Meridian Console:**
- `game_servers_provisioned_total` (counter)
- `game_server_provision_duration_seconds` (histogram)
- `active_game_sessions` (gauge)
- `file_transfer_bytes_total` (counter)
- `agent_heartbeat_latency_seconds` (histogram)

**Implementation:**
- prometheus-net library or OpenTelemetry Metrics API
- Expose via `/metrics` endpoint
- Visualize in Grafana

---

## Anti-Features

Features to explicitly NOT build. Common mistakes in this domain.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Log request/response bodies by default** | Performance hit, PII exposure risk | Log bodies only in debug mode, with redaction |
| **Synchronous log writes** | Blocks request processing | Use async logging (Serilog async sink) |
| **Single monolithic log file** | Impossible to query, single point of failure | Structured logs to aggregation system |
| **Logs as the audit system** | Logs can be modified, deleted | Separate audit store with immutability guarantees |
| **Over-logging (DEBUG in prod)** | Storage costs, noise, performance | Use log levels appropriately; INFO default |
| **Alerting on every error** | Alert fatigue, ignored alerts | Alert only on actionable, critical issues |
| **Building custom log aggregation** | Wheel reinvention | Use Grafana Loki, Elasticsearch, or Seq |
| **Tenant ID in log message text** | Hard to query/filter | Use structured properties: `{TenantId}` |
| **Logging sensitive headers** | Authorization tokens leaked | Allowlist headers to log |
| **Custom correlation ID format** | Breaks OpenTelemetry integration | Use W3C Trace Context standard |

### Anti-Feature Details

#### Logs as the Audit System
**Trap:** "We have logs, that's our audit trail."

**Problems:**
- Logs can be modified (not immutable)
- Log retention may be short (30-90 days operational)
- Logs lack proper schema for compliance queries
- "Show me all access to customer X data" is hard

**Instead:**
- Dedicated audit table/service with:
  - Write-once semantics
  - Long retention (1-7 years per SOC 2/HIPAA)
  - Queryable by user, resource, action, time
  - Tamper detection (checksums, blockchain-style linking)

#### Alert Fatigue
**Trap:** Alert on every 500 error.

**Problems:**
- 100+ alerts/day = alerts ignored
- Critical issues lost in noise
- On-call burnout

**Instead:**
- Alert on patterns: 5+ errors in 1 minute
- Alert on business impact: payment failures, auth outage
- Severity tiers: P1 (page), P2 (Slack), P3 (queue)
- Alert routing by service ownership

---

## Feature Dependencies

```
                     Correlation ID Propagation
                              |
              +---------------+---------------+
              |               |               |
    Request Logging    Distributed Tracing   Audit Logging
              |               |               |
              +-------+-------+               |
                      |                       |
               Log Aggregation          Audit Store
                      |                       |
              +-------+-------+---------------+
              |               |
        Alerting        Dashboards
              |
    +----+----+----+
    |    |    |    |
PagerDuty Slack Discord Email


Tenant Context (parallel concern)
       |
       +---> Inject into all logging scopes
       +---> Filter in aggregation layer
       +---> Compliance reporting

PII Redaction (parallel concern)
       |
       +---> Apply before any log write
       +---> Configure per data type
```

### Dependency Notes

1. **Correlation ID is foundational** - All other features depend on it
2. **Tenant context is orthogonal** - Can be added to any layer
3. **Alerting depends on aggregation** - Alerts query aggregated logs/metrics
4. **Audit store is separate from logs** - Different retention, different guarantees

---

## MVP Recommendation

For v0.1.0, prioritize in this order:

### Phase 1: Foundation (Already mostly done)
1. Correlation ID propagation (DONE)
2. RFC 9457 Problem Details (DONE)
3. Security event logging (DONE)
4. Request logging (DONE)

### Phase 2: Multi-Tenancy & Compliance
1. **Tenant context injection** - Add `TenantId`/`OrgId` to all log scopes
2. **PII redaction** - Configure redactors for passwords, tokens, emails
3. **Consistent audit events** - Extend `SecurityEventLogger` pattern to all services

### Phase 3: Operational Excellence
1. **Health check enhancement** - Separate liveness/readiness with dependency checks
2. **Real-time alerting** - Critical errors to PagerDuty/Slack
3. **Custom metrics** - Key business metrics for game server operations

### Defer to Post-MVP
- AI anomaly detection
- Immutable audit logs (write-once storage)
- Full distributed tracing UI
- Adaptive sampling

---

## Existing Implementation Assessment

### What's Already Good

| Component | Location | Quality |
|-----------|----------|---------|
| CorrelationMiddleware | ServiceDefaults/Middleware | Production-ready |
| ProblemDetailsMiddleware | ServiceDefaults/Middleware | Good, needs custom types |
| RequestLoggingMiddleware | ServiceDefaults/Middleware | Good, needs tenant context |
| SecurityEventLogger | ServiceDefaults/Security | Comprehensive, 17 event types |
| SecretsAuditLogger | Dhadgar.Secrets/Audit | Production-ready with break-glass |

### What's Missing

| Gap | Priority | Effort |
|-----|----------|--------|
| Tenant context in logs | HIGH | Medium |
| PII redaction | HIGH | Medium |
| Critical error alerting | HIGH | Medium |
| Health check separation (live/ready) | MEDIUM | Low |
| Custom business metrics | MEDIUM | Medium |
| Audit persistence (not just logs) | MEDIUM | High |
| Consistent audit pattern across all services | MEDIUM | Medium |

---

## Complexity Estimates

| Feature | Effort | Risk | Notes |
|---------|--------|------|-------|
| Tenant context injection | 2-3 days | Low | Extend existing middleware |
| PII redaction setup | 2-3 days | Low | Use built-in .NET redaction |
| PagerDuty/Slack alerting | 3-5 days | Low | HTTP webhook, well-documented |
| Health check enhancement | 1-2 days | Low | ASP.NET Core built-in |
| Custom metrics | 3-5 days | Low | prometheus-net or OTel |
| Audit persistence layer | 5-10 days | Medium | New service/table design |
| Distributed tracing UI | 3-5 days | Low | Configure Tempo/Jaeger |

---

## Sources

### Audit Trail & Compliance
- [Audit Trail Requirements: Guidelines for Compliance and Best Practices](https://www.inscopehq.com/post/audit-trail-requirements-guidelines-for-compliance-and-best-practices)
- [Audit Trail Checklist for 2025](https://sprinto.com/blog/audit-trail/)
- [SOC 2 Compliance Requirements](https://secureframe.com/hub/soc-2/requirements)
- [SOC 2 Data Security and Retention Requirements](https://www.bytebase.com/blog/soc2-data-security-and-retention-requirements/)

### OpenTelemetry & Distributed Tracing
- [OpenTelemetry trends 2025](https://www.dynatrace.com/news/blog/opentelemetry-trends-2025/)
- [OpenTelemetry Blog 2025](https://opentelemetry.io/blog/2025/)
- [Distributed Tracing Best Practices](https://www.atatus.com/blog/distributed-tracing-best-practices-for-microservices/)
- [Correlation ID vs Trace ID](https://last9.io/blog/correlation-id-vs-trace-id/)

### Structured Logging
- [Serilog Documentation](https://serilog.net/)
- [Structured Logging in ASP.NET Core with Serilog](https://www.milanjovanovic.tech/blog/structured-logging-in-asp-net-core-with-serilog)
- [5 Serilog Best Practices](https://www.milanjovanovic.tech/blog/5-serilog-best-practices-for-better-structured-logging)

### Problem Details
- [RFC 9457: Better information for bad situations](https://redocly.com/blog/problem-details-9457)
- [Problem Details for ASP.NET Core APIs](https://www.milanjovanovic.tech/blog/problem-details-for-aspnetcore-apis)
- [Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-9.0)

### PII Redaction
- [Data redaction in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction)
- [OpenTelemetry Log Redaction](https://opentelemetry.io/docs/languages/dotnet/logs/redaction/)
- [Best Logging Practices for Safeguarding Sensitive Data](https://betterstack.com/community/guides/logging/sensitive-data/)

### Alerting & Incident Response
- [PagerDuty Slack Integration](https://www.pagerduty.com/integrations/slack/)
- [Routing alerts by severity](https://medium.com/dataops-tech/routing-alerts-in-slack-pagerduty-by-severity-so-noise-doesnt-kill-you-874060ef2996)

### Health Checks
- [Health Checks in Microservices with C#](https://engineering87.github.io/2025/06/15/health-checks.html)
- [Kubernetes Health Checks](https://betterstack.com/community/guides/monitoring/kubernetes-health-checks/)

### Multi-Tenant Logging
- [Best Practices for Multi-Tenant Data Segregation](https://logcentral.io/blog/best-practices-for-multi-tenant-data-segregation)
- [Tenant Isolation in Multi-Tenant Systems](https://securityboulevard.com/2025/12/tenant-isolation-in-multi-tenant-systems-architecture-identity-and-security/)

### Log Retention
- [Log Retention Policies Explained](https://www.groundcover.com/logging/log-retention-policies)
- [Security log retention best practices](https://auditboard.com/blog/security-log-retention-best-practices-guide)

### Metrics
- [prometheus-net GitHub](https://github.com/prometheus-net/prometheus-net)
- [ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0)
