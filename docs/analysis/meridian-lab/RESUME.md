# RESUME: meridian-lab

Ledger: [README.md](README.md) (D-ML126 to D-ML131, F114 to F116, KG-W5-13 to KG-W5-16). Packets and contract: [work-packets.md](work-packets.md). Latest ruling: [wave5-run9-ruling](worknotes/wave5-run9-ruling.md), which holds the gate table, the L27b and R9-1 specs, and the steps to the PR. Earlier rulings: [run 8 diagnosis](worknotes/wave5-run8-diagnosis.md), [wave5-ruling-2](worknotes/wave5-ruling-2.md).

## State (2026-11-01)

- `campaign/meridian-lab` tip **2bb86769**, worktree `.claude/worktrees/meridian-lab`, **pushed** to origin (backup only, no PR). It is 415 commits ahead of `origin/main` and 13 behind.
- Merged: fix F, G, H and FGH-fix at `f5f8cdc5` (review-3 `ship`, security-2 `ship`); R7-1 to R7-4 at `62ce8be2`; R8-1 to R8-3 at `6899985d` (packet reviews `ship`, **no security pass**).
- Run 9 at `6899985d`: M1, M4 and M5 `detected` (gate MD met); twin passed; chaos ran 152 enroll loops with 0 violations; A3 and A11 passed. The hunt gave `not_reproduced` (F114: a carried I1 and a stale pool after a cut; F116: repros with cuts cannot be replayed). Fuzz gave 28 of 50 500s (KG-W5-1). Soak, coverage and S-L12 were not rerun; they are carried from run 8 with no rerun needed.
- Wave 5 and 5E are **open** (D-ML126). Owed: boundary review pass 4 and a confirmation run. The hunt gate is parked, pending D-ML127. D-ML125's hard stop holds: no more Docker fix waves.
- The worktree has 8 dirty `tests/*/packages.lock.json` files and an untracked `tests/Dhadgar.Lab.Tests/TestResults/`, both from the boundary runs. Neither is committed.

## Next steps (orchestrator, in order)

1. Ask the owner D-ML127, D-ML128 and D-ML131, and to ratify D-ML118 and D-ML125 (both executed under option A).
2. Boundary review pass 4 (D-ML129): R1, R2 and R3, each with a `packet-reviewer` and a `security-advisor`, six in parallel, read-only. Range `08571257..2bb86769`. Reports: `wave5-review-4-R<n>.md` and `wave5-security-4-R<n>.md`.
3. R9-1 (worktree `ml-wf-r9-flake-deps`) together with the review fix notes: packet-coder, then review loop, then local merge by ops. If a loop ends on `decision`, or on `fix` after pass 3, ask the architect for a ruling.
4. Confirmation run (D-ML130), with one packet-coder as boundary runner on a fresh, non-elevated daemon. A red is parked, not fixed.
5. Architect closes waves 5 and 5E. The R7 and R8 report facts are copied into `worknotes/` (they exist only in the `ml-wf-r8-*` worktrees).
6. ops restores the lock files and removes `TestResults/`. ops merges `origin/main` in (merge, never rebase, D-38). A packet-coder resolves conflicts and runs the build and the non-Docker suite. ops pushes.
7. Architect writes the W8 deltas note against the merged `main`, before L40.
8. Follow-ups: L27b (worktree `ml-l27b`, spec ready; before the campaign review), then L05b, L25g, L52b, L28b, L29b, L55b (the architect specs each first).
9. Waves 6 to 10, or the split under D-ML131 option B. Wave 6 waits on L00 and D-ML37/D-ML38.
10. Campaign review, which reruns every Docker test and the live hunt (D-ML74). Then L47. Then ops opens one PR with `security-review`, merged on the owner's yes.

## Owner decisions

- Blocking or next: D-ML127 (hunt gate), D-ML131 (ship cadence), D-ML128 (Nodes DB retry, product PR), D-ML107 (fuzz 500s), ratification of D-ML118 and D-ML125, D-ML37/D-ML38 (L00, wave 6).
- Non-blocking: D-ML120, D-ML31, D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73, OD-W5-1.

## Watch-outs

- Docker classes use `[Collection(LabDockerGate.Name)]`. Never run a bare `publish -r`, and never `git worktree prune`. Teardown is label-only.
- Each boundary segment runs on a fresh daemon, and the lab home stays in the scratchpad.
- The `ml-*` worktrees stay until the campaign PR merges. Most of `.claude/reports/meridian-lab/` is gitignored.
