---
phase: 04-health-alerting
plan: 03
subsystem: observability
tags: [grafana, alerting, loki, discord, testing]
status: complete
dependency-graph:
  requires: ["04-01", "04-02"]
  provides: ["grafana-alerting-rules", "notification-routing", "alerting-tests"]
  affects: ["05-*"]
tech-stack:
  added: []
  patterns: ["grafana-provisioning", "loki-log-queries", "severity-based-routing"]
key-files:
  created:
    - deploy/compose/grafana/provisioning/alerting/alert-rules.yaml
    - deploy/compose/grafana/provisioning/alerting/contact-points.yaml
    - deploy/compose/grafana/provisioning/alerting/notification-policies.yaml
    - tests/Dhadgar.Notifications.Tests/Alerting/AlertThrottlerTests.cs
    - tests/Dhadgar.Notifications.Tests/Alerting/AlertDispatcherTests.cs
  modified:
    - deploy/compose/grafana/provisioning/datasources/datasources.yml
    - deploy/compose/docker-compose.dev.yml
    - tests/Dhadgar.Notifications.Tests/Dhadgar.Notifications.Tests.csproj
decisions:
  - decision: "Use Loki log queries for alert rules (not Prometheus metrics)"
    rationale: "All services already ship structured logs to Loki; querying logs for error patterns is simpler than instrumenting metrics for each error type"
  - decision: "Critical alerts have 0s for delay, warnings have 2m pending period"
    rationale: "Critical errors require immediate attention; warnings should fire only after sustained error rate to avoid false positives"
  - decision: "Severity-based routing with different repeat intervals"
    rationale: "Critical alerts repeat hourly for visibility; warnings repeat 4-hourly to reduce noise while maintaining awareness"
metrics:
  duration: 5 minutes
  completed: 2026-01-22
---

# Phase 4 Plan 3: Grafana Alerting Configuration Summary

**One-liner:** Grafana alerting provisioned with 3 Loki-based alert rules (error-rate-spike, critical-error, health-check-failure), Discord contact point, and severity-based notification routing, plus 16 unit tests for application-level alerting.

## What Was Built

### Grafana Alert Rules (alert-rules.yaml)

Three provisioned alert rules that query Loki for error patterns:

| Rule | Query | Threshold | Pending | Severity |
|------|-------|-----------|---------|----------|
| High Error Rate | count_over_time errors in 5m | >10 | 2m | warning |
| Critical Error | count_over_time critical in 1m | >0 | 0s | critical |
| Health Check Failure | health/readiness failures in 5m | >3 | 1m | warning |

All rules use LogQL queries against the `loki` datasource with regex matching for log level patterns.

### Contact Points (contact-points.yaml)

Configures `dhadgar-alerts` receiver with two channels:
- **Discord webhook**: Real-time alerts with formatted message template
- **Email**: Audit trail for compliance (requires SMTP configuration)

Both support environment variable configuration:
- `DISCORD_WEBHOOK_URL`: Discord webhook endpoint
- `ALERT_EMAIL_ADDRESSES`: Comma-separated email recipients

### Notification Policies (notification-policies.yaml)

Severity-based routing:
- **Critical alerts**: 10s group wait, 1h repeat interval
- **Warning alerts**: 30s group wait, 4h repeat interval
- **Default grouping**: By alertname and severity

### Docker Compose Integration

Updated `docker-compose.dev.yml`:
- Mount alerting provisioning directory
- Add `DISCORD_WEBHOOK_URL` and `ALERT_EMAIL_ADDRESSES` environment variables
- Enable unified alerting (`GF_UNIFIED_ALERTING_ENABLED=true`)
- Disable legacy alerting

### Datasource UIDs

Added explicit UIDs to datasources (`prometheus`, `loki`) to enable stable references from alert rules.

### Unit Tests

16 new tests across 2 test classes:

**AlertThrottlerTests (9 tests):**
- First alert returns true
- Duplicate within window returns false
- Different alerts both return true
- Same service different title both dispatched
- Different exception types not throttled
- After window expires returns true
- All severities supported (Theory with 3 InlineData)

**AlertDispatcherTests (7 tests):**
- Sends to Discord and email in parallel
- Throttled alert does not send
- Different alerts all dispatched
- Discord failure still sends email (graceful degradation)
- Trace context passed to channels
- Multiple distinct alerts all dispatched
- Exception type variations dispatched

## Key Patterns

### Grafana Provisioning Structure
```
grafana/provisioning/
├── datasources/
│   └── datasources.yml       # With explicit UIDs
└── alerting/
    ├── alert-rules.yaml      # LogQL queries
    ├── contact-points.yaml   # Discord + email receivers
    └── notification-policies.yaml  # Routing by severity
```

### LogQL Pattern for Error Detection
```
sum(count_over_time({service_name=~"Dhadgar.*"} |~ "(?i)level.*error|level.*critical" [5m]))
```

### Docker Compose Environment Pattern
```yaml
environment:
  DISCORD_WEBHOOK_URL: ${DISCORD_WEBHOOK_URL:-}  # Empty default = graceful skip
  GF_UNIFIED_ALERTING_ENABLED: "true"
```

## Deviations from Plan

None - plan executed exactly as written.

## Decisions Made

1. **Use Loki log queries for alert rules**: All services already ship structured logs to Loki; querying logs for error patterns is simpler than instrumenting metrics for each error type.

2. **Critical alerts have 0s delay**: Critical errors require immediate attention without pending period.

3. **Warning alerts have 2m pending period**: Prevents false positives from transient errors.

4. **Severity-based routing**: Critical alerts repeat hourly; warnings repeat 4-hourly to balance visibility with noise reduction.

## Files Changed

| File | Change |
|------|--------|
| `deploy/compose/grafana/provisioning/alerting/alert-rules.yaml` | Created - 3 alert rules |
| `deploy/compose/grafana/provisioning/alerting/contact-points.yaml` | Created - Discord + email |
| `deploy/compose/grafana/provisioning/alerting/notification-policies.yaml` | Created - Routing |
| `deploy/compose/grafana/provisioning/datasources/datasources.yml` | Added UIDs |
| `deploy/compose/docker-compose.dev.yml` | Alerting config |
| `tests/Dhadgar.Notifications.Tests/Dhadgar.Notifications.Tests.csproj` | Added packages |
| `tests/Dhadgar.Notifications.Tests/Alerting/AlertThrottlerTests.cs` | Created - 9 tests |
| `tests/Dhadgar.Notifications.Tests/Alerting/AlertDispatcherTests.cs` | Created - 7 tests |

## Commits

| Hash | Message |
|------|---------|
| f1698d9 | feat(04-03): add Grafana alerting provisioning configuration |
| 2d96726 | feat(04-03): configure docker-compose for Grafana alerting |
| dde1876 | test(04-03): add unit tests for alerting infrastructure |

## Next Phase Readiness

Phase 4 (Health & Alerting) is now complete:
- 04-01: Health check infrastructure
- 04-02: Application alerting infrastructure (Discord, email, throttling)
- 04-03: Grafana alerting rules and tests

Ready for Phase 5 (Error Handling & Resilience).

No blockers identified. All success criteria met.
