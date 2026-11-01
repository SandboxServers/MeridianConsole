# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md). Wave 5 rulings: [wave5-ruling-1](worknotes/wave5-ruling-1.md) (D-ML95-104, fixes A-D), [wave5-ruling-2](worknotes/wave5-ruling-2.md) (D-ML105-113, fix-H, close bar D-ML108, run order D-ML109).

## State (re-verified 2026-11-01 after a PC crash; last work 2026-10-29 01:27)

- `campaign/meridian-lab` tip **87d63e80** (fix-H merge), worktree `.claude/worktrees/meridian-lab`. Local only: 387 commits ahead and 13 behind `origin/main`. Every `ml/*` branch is merged; `ml/l09f-rabbitmq-port` shows as unmerged, but it is patch-equal to #237 (`git cherry` `-`).
- Wave 5 packets L16g-L55 Integrated. Wave review: pass 1 R1/R2/R3 `decision` + security-1 `fix` → ruling 1 → fix A, B, C, D (merged 47c8c529, a37d293c, f0f8313f, b9e693f0); pass 2 review `fix` + security-2 `fix` → fix-E (08571257); pass 3 `ship` (`wave5-review-3.md`, it also closed security-2's six notes; no separate security-advisor pass 3).
- Boundary run (`worknotes/wave5-docker-run.md`): continuations 1-4 ran Docker part A and every live gate on the b9e693f0 daemon (hunt diverged at `Mint(60)` and replayed; chaos pass; soak weak; M1/M4/M5 `detected_other` on I6; twin failed; fuzz 500s). Continuation 6 at fa57cd19: 3 Docker tests red, part B skipped.
- Fix-F (I6 `LabRestartExcuses`, hunter lease, `IsDaemonEndpoint`) and fix-G (MutationDockerTests `lease_id`, recovery endpoint guard) were dispatched from the diag reports and merged **without any review or security pass**.
- Fix-H merged (87d63e80); its **packet review and security pass have not run**. H-runs: recovery pass (KG-W5-7 not hit), twin latency held (KG-W5-8 not hit), M1 state `succeeded` but verdict **`survived`** (not KG-W5-9; gate MD fails). Non-Docker suite 1415/0/5.
- **Not run:** continuation 7 (D-ML109 part A + part B on a fresh daemon), so gates MD, hunt and coverage are not re-read after fixes F-H; the D-ML108 wave review; ruling 3.
- Campaign worktree has 8 dirty `tests/*/packages.lock.json` (Sqlite CentralTransitive restore artefact) and untracked `tests/Dhadgar.Lab.Tests/TestResults/`. Not committed by anyone.

## Next actions (orchestrator)

1. `packet-reviewer` + `security-advisor` in parallel, read-only in `.claude/worktrees/meridian-lab`, range **08571257..87d63e80** (fix F, G, H). Reports `.claude/reports/meridian-lab/wave5-fixFGH-review-1.md` and `-security-1.md`. Ask them to check judge rule 4 on `LabRestartExcuses` (`stack_up` excuses `*`) and whether F's I6 excuse explains M1 losing its only signature. Fix notes go to a fresh packet-coder in worktree `ml-wf-h2` (branch `ml/wf-h2-tools` from the tip); at most two fix passes, then a ruling (ruling 2).
2. After `ship`: one packet-coder as boundary runner, continuation 7, per D-ML109 (non-elevated, user daemon stopped, no labelled containers, `Lab__RepoRoot` unset). Commits nothing.
3. Architect **ruling 3** (D-ML109 hard stop): M1 `survived` plus anything else from continuation 7. Its options are only park with an owner BlockedDecision, or spec one fix with evidence.
4. Then the wave 5/5E review (D-ML108 item 4). Then follow-ups L05b, L25g, L28b, L29b, L52b, L55b (architect specs each before dispatch) and wave 6/7 planning.

## Owner questions (orchestrator asks)

- Push a backup of `campaign/meridian-lab` (no PR; D-ML16 keeps the single PR after L47)? Merge `origin/main` (Sprint 2, vertical-slice) into the campaign now or at the end?
- Docker is up (29.1.3) for a 1-2 h run, and the run must be non-elevated (D-ML102). Dev stack containers stay untouched.
- The dirty lock files: commit, revert or leave? D-ML107 (fuzz 500 product PR; default: separate PR to `main`), OD-W5-1 (default: accept). O1, O2 are still pending.

## Carry-overs and watch-outs

- Wave 1 security list binds every packet: `PathValidator`, `http_request` only to lab endpoints, redacted logs, no `MERIDIAN_LAB_TOKEN` in child environments. Lab tests never probe the Gateway CA route (D-ML63). Gateway calls go through `GatewayPacer` and never raise limits (D-ML88).
- Docker classes carry `[Collection(LabDockerGate.Name)]` (D-ML103). Never a bare `publish -r`. Teardown is label-only, with no prune. Never `git worktree prune`. `ml-*` worktrees stay until the campaign PR merges.
- **Only on disk:** 100 of the 103 files in `.claude/reports/meridian-lab/` are gitignored. The `ml-*` worktrees also hold 20 untracked worknotes and the coder reports. Copy them to `worknotes/` before removing any worktree.
- Non-blocking owner decisions: D-ML31, D-ML37/38 (parked), D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73.
