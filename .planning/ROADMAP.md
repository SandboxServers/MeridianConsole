# Roadmap: Beta — Full Vertical Slice

## Overview

Five phases to a demoable beta. Phase 2 (agent vertical) is the critical path; Phases 1
and 3 can run in parallel with it. Full detail, estimates and rationale:
`docs/PROJECT-STATE.md` §6.

## Phases

- [ ] **Phase 0: Unblock** — merge PR #127 (P0 security), deflake the one failing Nodes
      test, file the P0 issues discovered in the 2026-07 audit (missing `/refresh`,
      agent↔Nodes contract, AppHost/Helm gaps, Identity `/internal` auth, Nodes JWT scheme)
- [ ] **Phase 1: Sessions + honest login** — implement `POST /refresh` in Identity;
      config-driven provider list in login UI; localhost login (cookie domain, ES256
      keygen script); ShoppingCart redirect fix
- [ ] **Phase 2: Agent vertical** — `/hubs/agent` SignalR hub in Nodes (ADR-0008);
      reconciled enrollment contracts in `Dhadgar.Contracts`; Gateway route transform;
      agent bootstrap hosted service; NodeId persistence (#101); command handlers (#118:
      Ping/Start/Stop/Restart/Status); command signing (#94); Agent.Core test coverage;
      agent API versioning (#116)
- [ ] **Phase 3: Server lifecycle** — rebase + merge PR #88 (Servers/Console/Mods);
      wire Servers → Nodes → agent (reserve capacity, dispatch StartServer, track state);
      service-level auth on promoted services; minimal console streaming
- [ ] **Phase 4: Panel + deploy + demo** — Panel `/servers`, `/nodes`, `/settings`
      pages wired to the existing API client; compose-based beta deployment verified +
      runbook; BetterAuth/Panel images added to container CI; #121 P0 integration tests;
      demo script + seed data

## Success Criteria (what must be TRUE at beta)

1. A fresh user can log in (social OAuth), stay logged in past 15 minutes, and see a
   live dashboard.
2. A Windows machine running the agent installer enrolls with a token and appears
   Online in Panel within a minute.
3. Creating a server in Panel results in a real game-server process running on that
   node; stop/restart work; state is reflected in Panel.
4. The whole control plane starts from `docker-compose.services.yml` with documented
   configuration; the demo is reproducible from the runbook by someone who didn't
   build it.

## Progress

| Phase | Status | Completed |
|-------|--------|-----------|
| 0. Unblock | Not started | — |
| 1. Sessions + login | Not started | — |
| 2. Agent vertical | Not started | — |
| 3. Server lifecycle | Not started | — |
| 4. Panel + deploy + demo | Not started | — |

---
*Roadmap created: 2026-07-26 (replaces completed PR #39 roadmap)*
