# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md); wave 4 order in its "Wave 4 plan".

## State (2026-10-28, wave 4 planned)

- Campaign branch `campaign/meridian-lab`, worktree `.claude/worktrees/meridian-lab` (local only). Waves 1-3 Integrated; lab tests 595 pass, 0 fail, 4 skip (non-Docker).
- Wave 4 plan written: L16f block (D-ML68 plus W3-ruling-2 carry-overs, F64), F63-F66, D-ML69 (doctor ports, D4 ruled). No BlockedDecision.
- D-ML39 ruled: one PR at the end (D-ML16). Reviews at wave boundaries and campaign end only. L00 parked, with L31-L33 and the container parts of wave 8.

## Next actions (orchestrator)

1. Step A, parallel worktrees `ml-l16f`, `ml-l19`, `ml-l49`, `ml-l50` from the tip.
2. Merge order: L16f → L19 → L50 → L49 → L20 → L21 → L22 → L23 → L51 → L24.
3. Step B: L20 after L19; L21 after L16f and L19. Step C: L22. Step D: L23 ‖ L51. Step E: L24.
4. Sliced packets commit per step; a coder out of context returns `blocked <sha> context: next step N`, and a fresh coder resumes at step N in the same worktree.
5. Boundary review after L24 and L51: R1 L16f, L19-L21; R2 L22-L24; R3 L49-L51; each with security-advisor; then the ruling check.
6. Wave 5 (L25 first) starts after that review ships. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`.

## Carry-overs

- Wave 1 security "Not fixed" list binds every packet: `PathValidator` on every path, `http_request` only to `LabHosts.IsLabEndpoint`, redacted logs, no `MERIDIAN_LAB_TOKEN` in child environments.
- Lab tests never probe the Gateway CA route (D-ML63). Enroll polling uses `poll_ms = 5000` (F54). 28 IL2026 warnings come from Agent.Core (#186).
- L36 repeats D-ML69 for `LabPorts.Sink`. L50 pins CsCheck (needs network for the restore).
- Owner action **O1**: add the D-ML33 promotion-rule line to CLAUDE.md.
- Owner action **O2** (D-ML67): add `mcp__meridian-lab__uat_attest` to `permissions.ask` in `.claude/settings.json` (agents never edit it). When it lands, drop "pending" at lab `README.md:178,203` and the "until then" clause at `SKILL.md:48`.

## Owner decisions open (non-blocking)

D-ML31, D-ML37/D-ML38 (parked), D-ML40-D-ML42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59. Recommendations are in the ledger README.

## Watch-outs

- Faults and the peer hit only lab targets. Never the dev stack. Never a bare `publish -r` (D-S241). S-L6 never removes containers without `MERIDIAN_LAB_S6_WIPE=1`.
- Mutant patches are never committed patched. No tool calls GitHub (D-ML28). Never `git worktree prune`. `ml-*` worktrees stay until the campaign PR merges.
