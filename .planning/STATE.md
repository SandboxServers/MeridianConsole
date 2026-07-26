# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-26)

**Core value:** Demoable full vertical slice — login → Panel → enrolled agent → real game server
**Current focus:** Phase 0 — Unblock (PR #127, deflake, file P0 issues)

## Current Position

Phase: 0 of 4 (Unblock)
Status: Ready to start
Last activity: 2026-07-26 — Project-state audit completed; docs reconciled; ports
standardized; ADR-0008 accepted; roadmap created

Progress: [░░░░░░░░░░] 0%

## Accumulated Context

### Decisions

See Key Decisions table in PROJECT.md (D1–D4, decided 2026-07-26) and
`docs/PROJECT-STATE.md` §2.

### Pending Todos

- Rebase + merge PR #127, then PR #88 (in Phase 3)
- File issues: missing `/refresh` (P0); agent↔Nodes contract mismatch (P0); AppHost
  BetterAuth/wiring gaps; Helm ServiceUrls/firewall/betterauth gaps; Identity
  `/internal` service auth; Nodes missing JWT bearer scheme; flaky
  `StaleNodeDetectionServiceTests`
- Deflake `Dhadgar.Nodes.Tests.StaleNodeDetectionServiceTests.ExecuteAsync_AdvancingTime_TriggersNextIteration`

### Blockers/Concerns

- Agent.Core has ~13 lines of tests over ~4,300 LOC of security-critical code — Phase 2
  must not ship without closing this
- CI logic lives in the external `SandboxServers/Azure-Pipeline-YAML` repo; changes to
  build/test behavior require touching that repo

## Session Continuity

Last session: 2026-07-26 (documentation review & reconciliation)
Stopped at: Roadmap created; Phase 0 not started
