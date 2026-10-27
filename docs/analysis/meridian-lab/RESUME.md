# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C17): [work-packets.md](work-packets.md).

## State (2026-10-27)

- Reworked 2026-10-27 for the owner's WTT-grade goal: 48 packets (L00-L47), 10 waves. Nothing built yet, and there is no branch. PR #227 (the plan) is not merged unless the user says so.
- **Ready:** L00 (Windows-container spike, worknote only) and L01. Everything else is BlockedDependency. No decision blocks waves 0-5.
- The MACHINE tier and the elevated helper are gone. Windows rows run in container pool members (D-ML29). The `Machine` gate is reserved (D-ML43). D-ML12 and D-ML13 are superseded.
- Sprint 2 is not on `main`. Waves 0-7 build against `main`; wave 8 needs Sprint 2 merged.

## Next actions (orchestrator)

1. ops: create worktree `.claude/worktrees/meridian-lab` on a new branch `campaign/meridian-lab` from `main`.
2. Dispatch L00 (spike on the workstation, no code) beside L01. Then L02, L03 and L04 in parallel → L05 → L06. Per packet: `packet-coder`, then `packet-reviewer` + `security-advisor`.
3. When L00's worknote lands, ask an architect ruling to fill L31's "Settings from L00" table. Raise D-ML37 or D-ML38 only if the outcome needs it.
4. Raise the non-blocking decisions with the owner whenever convenient. Rule D-ML39 before wave 3 ends (it decides whether M1 ships as a PR).

## Owner decisions (recommendation)

| ID | Question | Blocks |
| --- | --- | --- |
| D-ML31 | VM tier: follow-up campaign after wave 9 | nothing |
| D-ML37 | Enable Hyper-V and Containers if L00 says outcome B: yes | wave 6 under B |
| D-ML38 | Separate Windows `dockerd` service if engines can't coexist: yes | wave 6 if needed |
| D-ML39 | One PR per milestone M1-M6: yes (D-ML16 holds until ruled) | ship cadence |
| D-ML40 | Default-off SMTP switch and BetterAuth cookie-domain env: allow | email and login rows |
| D-ML41 | Discord bot coverage: out | nothing |
| D-ML42 | Filing drafts: owner runs `gh issue create`, or ops gains that verb | nothing |

Owner action **O1**: add the promotion-rule line (D-ML33) to CLAUDE.md.

## Watch-outs

- Faults hit only lab targets. `FaultTargetGuard` checks the registry, a live inspect and the tripwire, and BR tests prove it. Never touch the dev Aspire stack or its volumes.
- Observation paths bypass Toxiproxy (D-ML34). An invariant check error grades the row UNVERIFIED, never PASS.
- Red seams are excluded by `Seam!=S-Lx`; Docker, Windows-container and network tests by trait. CI is ubuntu-24.04.
- No tool files issues or calls GitHub (D-ML28, `NoGitHubCallsTests`). Never a bare `publish -r` (D-S241).
- Waves 5-7 may run in parallel but merge one at a time (shared `ToolPolicy.cs`, C4, `LabServices.cs`).
