# Beta: Full Vertical Slice

## What This Is

Bring Meridian Console to a demoable beta: **login → Panel → enroll a real Windows
agent → create/start/stop a real game server on that node**, deployable via Docker
Compose plus a Windows agent package.

> The previous contents of this directory described the "PR #39 Feedback Resolution"
> task (completed 2026-01-19) and were stale. Replaced 2026-07-26 after a full
> project-state review — see `docs/PROJECT-STATE.md` for the audit this plan is based on.

## Core Value

A user can operate a real game server on their own hardware entirely through the
platform, end to end, with sessions that don't expire out from under them.

## Context

- Four production-grade services (Gateway, Identity, Nodes, Secrets) + implemented
  Notifications/Discord/CLI/BetterAuth.
- The three broken seams: agent ↔ Nodes contract mismatch (no agent call succeeds),
  missing `/refresh` endpoint (silent logout at 15 min), Servers/Console/Mods stubs on
  main (implementations parked in PR #88).
- Decisions D1–D4 recorded in `docs/PROJECT-STATE.md` §2 (full slice; canonical 50x0
  ports; revive PRs #127 → #88; SignalR hub transport per ADR-0008).

## Requirements

### Active (phases in ROADMAP.md)

- [ ] P0: merge PR #127; deflake `StaleNodeDetectionServiceTests`; file P0 issues
- [ ] P1: Identity `/refresh` endpoint; honest login UI; localhost dev login; ShoppingCart redirect fix
- [ ] P2: `/hubs/agent` in Nodes; reconciled enrollment contract; agent bootstrap;
      command handlers (#118); command signing (#94); NodeId persistence (#101);
      Agent.Core tests; agent API versioning (#116)
- [ ] P3: rebase+merge PR #88; Servers→Nodes→agent lifecycle; service-level auth on
      promoted services; console streaming
- [ ] P4: Panel pages (/servers, /nodes, /settings); compose-based deployment + runbook;
      CI integration tests (#121); demo script

### Out of Scope (this beta)

- Kubernetes/Helm (chart is non-functional; compose is the target)
- Billing, Tasks features (parked stubs)
- Linux agent (Windows-only beta)
- Files service (removal tracked in #115)
- Gaming-provider login UI (Steam/BattleNet/Epic/Xbox — backend exists, UI later)

## Constraints

- Security items from PR #127 must land before anything else
- Agent-facing APIs must be versioned before an agent binary ships to real hardware
- Services promoted out of stub status must add their own authn/authz (currently the
  Gateway is their only protection)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Full vertical slice beta (D1) | Only a real agent demo proves the product | Decided 2026-07-26 |
| Canonical 50x0 ports (D2) | Gateway/docs/Helm already agree; only launchSettings diverged | Fixed 2026-07-26 |
| Revive PR #127 then #88 (D3) | Months of reviewed work; rewriting is waste | Decided 2026-07-26 |
| SignalR hub transport (D4) | Matches built agent client; push semantics; console streaming later | ADR-0008 |

---
*Last updated: 2026-07-26 after project-state review*
