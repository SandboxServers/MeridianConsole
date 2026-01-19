# Requirements: PR #39 Feedback Resolution

**Defined:** 2026-01-19
**Core Value:** All CodeRabbit review feedback addressed with no regressions

## v1 Requirements

Requirements for this work. Each maps to roadmap phases.

### Package Updates

- [ ] **PKG-01**: Update Discord.Net from 3.13.0 to 3.18.0 in Directory.Packages.props

### Database Improvements

- [ ] **DB-01**: Truncate fields to EF max-length constraints in SendDiscordNotificationConsumer (EventType: 100, Channel: 100, Title: 500, ErrorMessage: 1000)
- [ ] **DB-02**: Configure separate databases for Discord and Notifications services (dhadgar_discord, dhadgar_notifications)
- [ ] **DB-03**: Remove hardcoded connection string from DiscordDbContextFactory, load from configuration
- [ ] **DB-04**: Enable nullable annotations in Discord migration files (replace #nullable disable with #nullable enable)

### HTTP & Resource Management

- [ ] **HTTP-01**: Dispose HttpResponseMessage in PlatformHealthService.CheckServiceAsync using `using` statement
- [ ] **HTTP-02**: Handle cancellation vs timeout properly (TaskCanceledException when !ct.IsCancellationRequested for timeout, rethrow OperationCanceledException when cancelled)

### Messaging Infrastructure

- [ ] **MSG-01**: Enable MassTransit Entity Framework Outbox for atomic log persistence and message publishing in NotificationDispatcher

### Frontend

- [ ] **FE-01**: Add Node.js 20+ engines field to Dhadgar.Scope/package.json

### Documentation & Cleanup

- [ ] **DOC-01**: Fix capitalization in sections.json (Rabbitmq → RabbitMQ, Mvp → MVP)

### API Improvements

- [ ] **API-01**: Change ActionUrl from string? to Uri? in SendPushNotification record for type safety

## v2 Requirements

None — this is a focused PR feedback resolution.

## Out of Scope

| Feature | Reason |
|---------|--------|
| New features | PR feedback resolution only |
| Refactoring unrelated code | Keep changes focused |
| Additional test coverage | PR already has tests |
| Performance optimizations | Not in review scope |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PKG-01 | Phase 1 | Pending |
| DB-01 | Phase 2 | Pending |
| DB-02 | Phase 2 | Pending |
| DB-03 | Phase 2 | Pending |
| DB-04 | Phase 2 | Pending |
| HTTP-01 | Phase 3 | Pending |
| HTTP-02 | Phase 3 | Pending |
| MSG-01 | Phase 3 | Pending |
| FE-01 | Phase 4 | Pending |
| DOC-01 | Phase 4 | Pending |
| API-01 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 11 total
- Mapped to phases: 11
- Unmapped: 0 ✓

---
*Requirements defined: 2026-01-19*
*Last updated: 2026-01-19 after initialization*
