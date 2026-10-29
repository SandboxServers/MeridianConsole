# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md); wave 5 order, steps and Docker split in its "Wave 5 plan".

## State (2026-10-28, wave 5 ALL MERGED, boundary next)

- Campaign branch `campaign/meridian-lab`, worktree `.claude/worktrees/meridian-lab` (local only). Waves 1-4 and 4H Integrated and reviewed. Wave 5: L16g, L25, L26, L27, L52, L53, L28, L29, L25e, L54, L25f, L55 all merged; tip 3b24617f (L55 merge; see `.claude/reports/meridian-lab/merge-L55.md`). `.gitattributes` LF rule for Twins tests (eda18720). Known flake for R3: `HistoryRunJobTests` heal-once tests fail under load. No BlockedDecision.
- L25 rulings D-ML91-94 (`worknotes/L25-ruling-1.md`; new packets L25e, L25f) and D-ML95 (orchestrator, not yet in README: `UatRunTool` keeps `GetRequiredService<ResultsService>()`; test providers register `ResultsService`, additive setup only).
- Wave 4 rulings: D-ML70/72 (S-L7), D-ML74 (Docker at the boundary), D-ML75/76 (R2; the orchestrator's L22 ruling, first numbered D-ML73, is D-ML75), D-ML80-82 (R1), D-ML83-85 (Docker). D-ML73 is still the open broker-readiness question (non-blocking). Rulings in `worknotes/wave4-*-ruling-1.md`.
- Wave 5 plan rulings: F82-F86, D-ML86 (SQLite package, CsCheck reference), D-ML87 (KI-F67, KI-F69), D-ML88 (`GatewayPacer`, no rate-limit raise), D-ML89 (one `RunSummary`; OD-1 ruled at its default as new packet L16g), D-ML90 (self-check recovery mutex and token).
- Not run yet: RequiresDocker tests in Data, Services, Stack (earlier waves); run them at the campaign-end review.
- Non-blocking owner decisions touching wave 5 (defaults in the plan): D-ML42, D-ML49, D-ML71, D-ML73.
- Stray scratch worktree `base-l22` (Temp scratchpad) needs manual removal ("Filename too long").
- D-ML39 ruled: one PR at the end (D-ML16). Reviews at wave boundaries and campaign end only. L00 parked, with L31-L33 and the container parts of wave 8.

## Next actions (orchestrator; wave 5 plan)

1. Steps A-D done (all merged). Next: boundary Docker/live run, then reviews.
2. Each packet runs its Steps; step checks are non-Docker only (D-ML74).
5. Boundary run (one coder in the campaign worktree): the plan's Docker table, the wave 4 Docker set, the live hunt, fuzz, chaos and `coverage_report` gates; report `worknotes/wave5-docker-run.md`.
6. Boundary review in slices R1 (L16g, L25, L26), R2 (L27-L29, L52), R3 (L53-L55): reviewer plus security-advisor each; then the ruling check.
7. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`. Campaign end: campaign review, one PR with `security-review`, user yes before merge.

## Carry-overs

- Wave 1 security "Not fixed" list binds every packet: `PathValidator` on every path, `http_request` only to `LabHosts.IsLabEndpoint`, redacted logs, no `MERIDIAN_LAB_TOKEN` in child environments.
- Wave 4 rules for wave 5 (plan "Carry-overs"): DbGate three outcomes plus an anti-always-skip test; no `/readyz` Postgres claim (F67); Gateway calls via `LabHttp` with the probe Host; heal in `finally` with its own 30 s token; label-only Docker cleanup.
- Lab tests never probe the Gateway CA route (D-ML63). Enroll polling uses `poll_ms = 5000` (F54). 28 IL2026 warnings come from Agent.Core (#186). L36 repeats D-ML69 for `LabPorts.Sink`.
- Owner action **O1**: add the D-ML33 promotion-rule line to CLAUDE.md. **O2** (D-ML67): add `mcp__meridian-lab__uat_attest` to `permissions.ask`; then drop "pending" at lab `README.md:178,203` and `SKILL.md:48`.

## Owner decisions open (non-blocking)

D-ML31, D-ML37/D-ML38 (parked), D-ML40-D-ML42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73. Recommendations are in the ledger README.

## Watch-outs

- Faults and the peer hit only lab targets. Never the dev stack. Never a bare `publish -r` (D-S241). S-L6 never removes containers without `MERIDIAN_LAB_S6_WIPE=1`. Lab jobs never raise Gateway limits (D-ML88).
- Mutant patches are never committed patched. No tool calls GitHub (D-ML28). Never `git worktree prune`. `ml-*` worktrees stay until the campaign PR merges.
