# Roadmap: PR #39 Feedback Resolution

## Overview

Address all CodeRabbit review feedback for the Discord and Notifications services PR. This is focused maintenance work: update packages, fix database configuration issues, improve resource handling, and clean up minor issues. Four phases deliver incrementally from quick wins to service code changes.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3, 4): Planned milestone work
- Decimal phases (e.g., 2.1): Urgent insertions if needed

- [x] **Phase 1: Quick Wins** - Package update, Node.js engines, documentation fixes
- [x] **Phase 2: Database Configuration** - Separate DBs, factory cleanup, nullable annotations, field truncation
- [x] **Phase 3: Service Code** - HTTP disposal, cancellation handling, MassTransit outbox
- [x] **Phase 4: API Contract** - Change ActionUrl to Uri type

## Phase Details

### Phase 1: Quick Wins
**Goal**: Simple fixes that don't affect runtime behavior or other code
**Depends on**: Nothing (first phase)
**Requirements**: PKG-01, FE-01, DOC-01
**Success Criteria** (what must be TRUE):
  1. Discord.Net package version is 3.18.0 in Directory.Packages.props
  2. Dhadgar.Scope package.json has engines.node field requiring 20+
  3. sections.json shows "RabbitMQ" and "MVP" with correct capitalization
**Plans**: TBD

Plans:
- [x] 01-01: Package and configuration updates

### Phase 2: Database Configuration
**Goal**: Services use separate databases with proper configuration
**Depends on**: Phase 1
**Requirements**: DB-01, DB-02, DB-03, DB-04
**Success Criteria** (what must be TRUE):
  1. Discord service connects to dhadgar_discord database
  2. Notifications service connects to dhadgar_notifications database
  3. DiscordDbContextFactory loads connection string from configuration (no hardcoded strings)
  4. Discord migration files have nullable annotations enabled
  5. SendDiscordNotificationConsumer truncates fields to EF max-length constraints before save
**Plans**: TBD

Plans:
- [x] 02-01: Separate database configuration
- [x] 02-02: Migration regeneration and field truncation

### Phase 3: Service Code
**Goal**: Proper resource disposal and messaging patterns
**Depends on**: Phase 2
**Requirements**: HTTP-01, HTTP-02, MSG-01
**Success Criteria** (what must be TRUE):
  1. HttpResponseMessage is disposed after use in PlatformHealthService
  2. TaskCanceledException when !ct.IsCancellationRequested returns timeout error (not cancelled)
  3. OperationCanceledException rethrown when cancellation was requested
  4. MassTransit Entity Framework Outbox is enabled for NotificationDispatcher
**Plans**: TBD

Plans:
- [x] 03-01: HTTP handling and MassTransit outbox

### Phase 4: API Contract
**Goal**: Type-safe URL handling in message contracts
**Depends on**: Phase 3
**Requirements**: API-01
**Success Criteria** (what must be TRUE):
  1. SendPushNotification.ActionUrl property is Uri? type (not string?)
  2. All usages of ActionUrl compile and work with Uri type
**Plans**: TBD

Plans:
- [x] 04-01: ActionUrl type change

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Quick Wins | 1/1 | Complete | 2026-01-19 |
| 2. Database Configuration | 2/2 | Complete | 2026-01-19 |
| 3. Service Code | 1/1 | Complete | 2026-01-19 |
| 4. API Contract | 1/1 | Complete | 2026-01-19 |

---
*Roadmap created: 2025-01-19*
*Completed: 2026-01-19*
