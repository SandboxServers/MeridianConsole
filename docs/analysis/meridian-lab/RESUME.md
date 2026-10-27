# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract: [work-packets.md](work-packets.md).

## State (2026-10-27)

- Design done. 28 packets (L01-L28) in 6 waves. Nothing built; no branch yet.
- L01 is **Ready**. L19-L21, L25 and L27 are **BlockedDecision**; the rest are BlockedDependency.
- Sprint 2 is not on `main` (local branch `campaign/sprint2-agent-nodes-w1`). Waves 1-4 do not need it; wave 5 does.
- Supersedes Sprint 2's wave 2U (P18-P23) as the lasting UAT apparatus (D-ML17); the orchestrator reconciles the Sprint 2 ledger.

## Next actions (orchestrator)

1. ops: worktree `.claude/worktrees/meridian-lab` on new branch `campaign/meridian-lab` from `main`.
2. Wave 1: L01 → L02 → L03 → L04 → L05 → L06 (L03, L04 may run beside L02). Per packet: `packet-coder`, then `packet-reviewer` + `security-advisor`; at `ship` a Sonnet `general-purpose` agent merges locally and reports the tip.
3. Ask the user D-ML12 and D-ML13 before wave 4; D-ML14 and D-ML15 with Sprint 2's D-S222/D-S223 before wave 5.

## Open decisions

| ID | Question | Blocks |
| --- | --- | --- |
| D-ML12 | UAC-launched elevated helper (recommended) vs standing elevated service | L19-L21 |
| D-ML13 | Which machine may run the MACHINE tier (marker on workstation, recommended, vs VM only) | live L21, L27 |
| D-ML14 | Agent trust of the lab Gateway; follows D-S222 | L25, connected-agent rows of L26 |
| D-ML15 | MSI leg in UAT; follows D-S223 | nothing |

## Gates

- Wave 1: S-L1 green; user installs via `tools\lab\install.ps1`, `claude mcp list` shows `meridian-lab`.
- Wave 2: S-L2 green (Docker run recorded). Wave 3: S-L5, S-L6 green. Wave 4: S-L4 green plus the user's elevated test run.
- Wave 5: Sprint 2 merged to `main`, `main` merged into the campaign branch; S-L3 green; L27 ledgers in `worknotes/`.
- Then campaign review `ship` → L28 → ops pushes once, one PR with `security-review`, merged after the user's yes.

## Watch-outs

- Red seams are excluded by `--filter "Seam!=S-Lx"` until their green packet; Docker and elevation tests by trait.
- Lab ports (15000/15040/15432/15673/16379) never collide with dev compose or Sprint 2's 5672 UAT broker.
