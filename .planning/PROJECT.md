# Meridian Console

## What This Is

A modern, security-first game server control plane—a multi-tenant SaaS platform that orchestrates game servers on customer-owned hardware via customer-hosted agents. This is the Dhadgar codebase.

## Core Value

Complete observability—debug any request end-to-end, audit any action for compliance, and get alerted proactively before users report issues.

## Current Milestone: v0.1.0 Centralized Logging & Auditing

**Goal:** Build comprehensive observability infrastructure that touches all services—centralized error handling, structured logging with proper levels, audit trails for all API calls, and proactive alerting.

**Target features:**
- Centralized error handling with Problem Details (RFC 7807) and exception classification
- Structured logging with debug-to-fatal levels throughout all modules
- Request correlation (X-Correlation-Id) integrated with OpenTelemetry tracing
- Full log enrichment (request context, service context, operation context)
- Sensitive data scrubbing in logs
- Audit system writing all API calls to database for compliance queries
- Dual log output: files + database for Grafana/Loki integration
- Critical error alerts via Discord/email notifications

## Requirements

### Validated

- ✓ Discord service with bot hosting, slash commands, webhook delivery — existing
- ✓ Notifications service as event orchestrator — existing
- ✓ Admin API key authentication — existing
- ✓ MassTransit retry policies — existing
- ✓ Slash command registration guard — PR commit fec747b
- ✓ Deferred interaction error handling — PR commit fec747b
- ✓ Admin endpoint authorization — PR commit b9a7c24
- ✓ discord-bot-token moved to Infrastructure category — PR commit fec747b
- ✓ Limit capping on logs endpoints — PR commit fec747b
- ✓ **PKG-01**: Discord.Net updated to 3.18.0 — v1.0 Phase 1
- ✓ **DB-01**: Fields truncated to EF max-length in SendDiscordNotificationConsumer — v1.0 Phase 2
- ✓ **DB-02**: Separate databases for Discord and Notifications services — v1.0 Phase 2
- ✓ **DB-03**: DiscordDbContextFactory loads connection string from config — v1.0 Phase 2
- ✓ **DB-04**: Nullable annotations enabled in Discord migrations — v1.0 Phase 2
- ✓ **HTTP-01**: HttpResponseMessage disposed with cancellation handling — v1.0 Phase 3
- ✓ **MSG-01**: MassTransit EF Outbox enabled for atomic operations — v1.0 Phase 3
- ✓ **FE-01**: Node.js 20+ engines field in Dhadgar.Scope package.json — v1.0 Phase 1
- ✓ **DOC-01**: Capitalization fixed (RabbitMQ, MVP) in sections.json — v1.0 Phase 1
- ✓ **API-01**: ActionUrl changed to Uri? in SendPushNotification — v1.0 Phase 4

### Active

- [ ] Centralized error handling in ServiceDefaults
- [ ] Problem Details (RFC 7807) responses with correlation IDs
- [ ] Exception-to-HTTP-status classification
- [ ] Sensitive data scrubbing middleware
- [ ] Structured logging with proper levels (debug to fatal)
- [ ] Log enrichment with full context (request, service, operation)
- [ ] Correlation ID middleware (X-Correlation-Id)
- [ ] OpenTelemetry trace context integration
- [ ] File-based log sink
- [ ] Database log sink for audit queries
- [ ] Audit system for all API calls
- [ ] Critical error alert triggers (Discord/email)
- [ ] Logging instrumentation across all services

### Out of Scope

- New business features — this is infrastructure/observability work
- UI changes — backend logging only
- Performance optimization beyond logging overhead
- Log analytics/ML — just capture and store for now

## Context

**Services affected**: All 13+ microservices will be instrumented
**Shared projects**: ServiceDefaults, Contracts, Messaging
**Existing stack**: OpenTelemetry configured, Grafana/Prometheus/Loki in docker-compose

**Current state**: Logging exists only where debugging was needed. No consistent levels, no audit trail, no correlation across services.

## Constraints

- **Branch**: Must work on `feature/centralized-logging-auditing` branch
- **Compatibility**: Must not break existing functionality
- **Performance**: Logging overhead must be minimal (async sinks, sampling for debug)
- **Compliance**: Audit logs must support retention policies
- **Security**: Sensitive data must never appear in logs

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use separate databases per service | Microservices architecture requires service isolation | ✓ Implemented v1.0 |
| Use MassTransit EF Outbox | Ensures atomic log + publish operations | ✓ Implemented v1.0 |
| Regenerate migrations with nullable enable | Matches project-wide nullable policy | ✓ Implemented v1.0 |
| Correlation ID + OTEL together | Correlation ID for log queries, OTEL for distributed tracing | — Pending |
| Database for audit, Loki for ops | Structured queries vs real-time streaming | — Pending |
| Problem Details RFC 7807 | Industry standard for consistent API error responses | — Pending |

---
*Last updated: 2026-01-20 after v0.1.0 milestone initialization*
