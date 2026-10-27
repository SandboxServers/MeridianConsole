---
title: Where Things Live
doc_id: agents.domain
type: reference
audience: [agents, engineers]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index, meta.conventions, agents.ai-sdlc]
---

# Where Things Live

A map of the repo for Claude sessions and other agents: where each kind of fact lives, which source to trust when two disagree, and the traps that have caught sessions before. Read it after the root [CLAUDE.md](../../CLAUDE.md) and before you search the code.

## Which source wins

For "what does it do", trust sources in this order. The full rule, and what to do about a conflict, is in [meta/conventions.md](../meta/conventions.md#when-sources-disagree).

1. The code on `main`, together with its passing tests.
2. `src/Shared/Dhadgar.Contracts` for any shape that crosses a service or agent boundary.
3. [PROJECT-STATE.md](../PROJECT-STATE.md) for what's confirmed working.
4. ADRs and recorded decisions (`D1`–`D4`, `D-S2x`) for why.
5. Library docs: reference chapters, then explanations.
6. Plans: intent, not fact.
7. `src/**/README.md`. Many are long, older than the code, and wrong about status.
8. `docs/archive/`, `docs/implementation-plans/`, `.planning/`: history only.

For "what should I do next", the order is different: the user, then a Directive on the agent board, then the active campaign's `RESUME.md`, then PROJECT-STATE's roadmap.

## Where each kind of fact lives

| You need | Look in |
| --- | --- |
| Rules for every change | Root [CLAUDE.md](../../CLAUDE.md), then `src/<Project>/CLAUDE.md` if there is one |
| What works today | [PROJECT-STATE.md](../PROJECT-STATE.md) status table, then the reference chapter's "Evidence and status" |
| Why something is built this way | [adr/](../adr/README.md); PROJECT-STATE §4 (`D1`–`D4`); campaign ledgers' decision tables (`D-S2x` in `docs/analysis/sprint2-agent-nodes/README.md`) |
| Known differences between design and code | PROJECT-STATE's divergence register (`DV-1`…`DV-14`); each chapter's "Gaps and divergences" |
| A shared type | `src/Shared/Dhadgar.Contracts`, by area folder |
| A route | The service's `Program.cs` or endpoint files for the service form; `src/Dhadgar.Gateway/appsettings.json` (`ReverseProxy`) for the Gateway form |
| A port | PROJECT-STATE `D2`; each service's `Properties/launchSettings.json`; `deploy/compose/` |
| Configuration keys | `*Options.cs` in each service; `appsettings*.json`; `deploy/compose/*.env*` for compose |
| Error codes | `src/Shared/Dhadgar.ServiceDefaults` (`Problems/`) |
| Package versions | `Directory.Packages.props` only |
| EF migrations | `src/<Service>/Data/Migrations` (the `Migrations/README.md` files elsewhere are wrong; DL-608) |
| Tests | `tests/Dhadgar.<Project>.Tests`, one project per source project; seam tests sit with the service that owns the seam |
| CI | `.github/workflows/` (`ci.yml` calls `_dotnet-build-test.yml`). The `azure-pipelines*.yml` files are manual-only. |
| Deploy | `deploy/compose/` for beta. `deploy/kubernetes/` (Helm) is non-functional and deferred. |
| Campaign state | `docs/analysis/<campaign>/RESUME.md`, then its `README.md` and `work-packets.md` |
| How Claude sessions work here | [ai-sdlc.md](ai-sdlc.md) and `.claude/agents/` |

## Repo map

| Path | What it is |
| --- | --- |
| `src/Dhadgar.Gateway` | YARP gateway: the only public entry point for HTTP |
| `src/Dhadgar.Identity` | Users, organizations, roles; issues platform tokens (OpenIddict) |
| `src/Dhadgar.BetterAuth` | Node service for social login; exchanges into Identity tokens |
| `src/Dhadgar.Nodes` | Node inventory, agent enrollment, the agent CA; the agent hub (in progress) |
| `src/Dhadgar.Secrets` | Secrets over Azure Key Vault |
| `src/Dhadgar.Notifications`, `src/Dhadgar.Discord` | Outbound notifications |
| `src/Dhadgar.Servers`, `Console`, `Mods`, `Tasks`, `Files`, `Billing` | Stubs. Their READMEs describe intent, not code. |
| `src/Dhadgar.Cli` | The `dhadgar` CLI |
| `src/Dhadgar.AppHost` | Aspire AppHost: dev only, partly broken (DV-6) |
| `src/Dhadgar.Panel`, `Scope`, `ShoppingCart` | Astro frontends |
| `src/Dhadgar.SharedAuth` | TypeScript auth client the frontends share |
| `src/Agents/Dhadgar.Agent.Core` | Platform-neutral agent: enrollment, connection, commands, processes |
| `src/Agents/Dhadgar.Agent.Windows` | Windows service host, Job Objects, ACLs, certificate store, WiX installer under `Installer/` |
| `src/Agents/Dhadgar.Agent.Linux` | Stub |
| `src/Agents/Dhadgar.Agent.GameServerWrapper` | Wraps a game-server process for the agent |
| `src/Shared/*` | Contracts, Messaging, ServiceDefaults, Shared (`Result<T>`), Testing |
| `deploy/` | Compose, Helm, certificates, deploy scripts |
| `scripts/` | Dev bootstrap and environment checks |
| `docs/` | This library; see [docs/README.md](../README.md) |

## Traps

- **"Agent" means two things.** The product's agent runs on customer hardware (`src/Agents`). Claude's agents live in `.claude/agents/`. Say which one you mean.
- **READMEs overstate.** Several `src` READMEs describe planned features as done, and some badges say "Stub" for working services. Check the code and PROJECT-STATE before repeating a README's claim.
- **Two route forms.** The Gateway path (`/api/v1/nodes/...`) and the service path (`/nodes/...`) differ. A route quoted without saying which form is a bug waiting to happen (DV-1).
- **"Code exists" isn't "works".** Only a passing seam test or a recorded manual check makes something `confirmed-working`.
- **Not every validator the docs name exists.** CLAUDE.md names `WindowsPathValidator` and `LinuxPathValidator`; check the code before citing them (DL-402).
- **`.planning/` and `docs/implementation-plans/` are history.** Don't plan from them without checking PROJECT-STATE.
- **Worktrees.** Sprint campaigns keep worktrees under `.claude/worktrees/`. Search results from there are other branches, not `main`; exclude them when you mean the current code.
