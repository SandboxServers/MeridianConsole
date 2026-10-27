---
title: Plans
doc_id: plan.index
type: index
audience: [engineers, agents, reviewers]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index, adr.index]
---

# Plans

Work whose path is decided but whose code isn't written. Each plan splits what's decided (and where it was decided) from what's still open. When the code lands, the plan becomes a reference chapter and the plan moves to [archive/](../archive/README.md).

A plan isn't a wish list. If nobody has decided the path, the item goes under [Unplanned](#unplanned) instead. Template: [meta/templates/plan.md](../meta/templates/plan.md).

[PROJECT-STATE.md](../PROJECT-STATE.md) remains the plan of record for order and priority; plans here hold the design detail.

## Plans

None yet.

## Planned

| Plan | Decided by | Backlog |
| --- | --- | --- |
| `agent-hub.md` | ADR-0008, D4, D-S21–D-S29, D-S217 | DL-501 |
| `certificate-renewal.md` | D-S212 | DL-502 |
| `agent-commands.md` | ADR-0007, #94, #118 | DL-503 |
| `server-lifecycle.md` | D1, D3 | DL-504 |
| `console-streaming.md` | PROJECT-STATE Phase 3 | DL-505 |
| `panel-beta.md` | PROJECT-STATE Phase 4 | DL-507 |
| `api-versioning.md` | #116, D-S22 | DL-508 |
| `integration-tests.md` | #121 | DL-510 |
| `kubernetes.md` | DV-7 | DL-513 |
| `aspire.md` | DV-6 | DL-514 |
| `lint-and-sast.md` | GitHub Actions decision | DL-516 |

## Unplanned

Known future work with no decided path yet. Each moves up to a plan once someone decides how it'll be built.

| Item | Where the ideas are today | Backlog |
| --- | --- | --- |
| Mods | `src/Dhadgar.Mods/README.md` | DL-506 |
| Linux agent | `docs/implementation-plans/agent-linux.md` | DL-509 (deferred by D-S218) |
| Identity MFA | Identity docs | DL-511 |
| Tasks, Billing | their stub READMEs | DL-512 |
| Self-hosted edition (KiP) | [docs/kip/](../kip/README.md) | DL-515 |
