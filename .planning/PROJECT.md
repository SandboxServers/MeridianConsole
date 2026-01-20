# PR #39 Feedback Resolution

## What This Is

Code quality improvements for the Discord and Notifications services based on CodeRabbit review feedback from PR #39. This is maintenance work to resolve all remaining review comments so the feature branch can be merged cleanly.

## Core Value

All CodeRabbit review feedback addressed with no regressions—the PR passes review and is merge-ready.

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

### Active

- [ ] **PKG-01**: Update Discord.Net from 3.13.0 to 3.18.0
- [ ] **DB-01**: Truncate fields to EF max-length constraints in SendDiscordNotificationConsumer
- [ ] **DB-02**: Separate databases for Discord and Notifications services
- [ ] **DB-03**: Remove hardcoded connection string from DiscordDbContextFactory
- [ ] **DB-04**: Enable nullable annotations in Discord migration files
- [ ] **HTTP-01**: Dispose HttpResponseMessage and handle cancellation in PlatformHealthService
- [ ] **MSG-01**: Enable MassTransit Entity Framework Outbox for atomic operations
- [ ] **FE-01**: Add Node.js 20+ engines field to Dhadgar.Scope package.json
- [ ] **DOC-01**: Fix capitalization (RabbitMQ, MVP) in sections.json
- [ ] **API-01**: Change ActionUrl from string? to Uri? in SendPushNotification

### Out of Scope

- New features beyond PR #39 scope — this is review feedback resolution only
- Refactoring unrelated code — keep changes focused on review comments
- Additional test coverage beyond what's needed — PR already has tests

## Context

**PR #39 Summary**: Implements Discord service with bot hosting, slash commands, and webhook delivery. Implements Notifications service as event orchestrator with Office 365 email support.

**Services affected**:
- `Dhadgar.Discord` - Discord bot, slash commands, webhook delivery
- `Dhadgar.Notifications` - Event routing, email dispatch
- `Dhadgar.Contracts` - Shared message contracts
- `Dhadgar.Scope` - Documentation site (package.json update)

**Review source**: CodeRabbit automated review on GitHub PR #39

## Constraints

- **Branch**: Must work on `feature/discord-notifications-services` branch
- **Compatibility**: Changes must not break existing functionality
- **Testing**: All existing tests must continue to pass
- **Migrations**: Database changes require new EF Core migrations

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use separate databases per service | Microservices architecture requires service isolation | — Pending |
| Use MassTransit EF Outbox | Ensures atomic log + publish operations | — Pending |
| Regenerate migrations with nullable enable | Matches project-wide nullable policy | — Pending |

---
*Last updated: 2026-01-19 after initialization*
