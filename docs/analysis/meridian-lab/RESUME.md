# RESUME: meridian-lab

Ledger: [README.md](README.md) (D-ML134 to D-ML138, F118 to F122, KG-W5-13 to KG-W5-19). Packets and contract: [work-packets.md](work-packets.md). Latest ruling: [wave5-run10-ruling](worknotes/wave5-run10-ruling.md) (packets in section 7, confirmation run in section 6, steps in section 10). Nodes product packet: [nodes-ef-retry-packet](worknotes/nodes-ef-retry-packet.md) (NR-1, L19n).

## State (2026-11-02)

- `campaign/meridian-lab` tip **4b508757** (run 10 record), one commit ahead of origin (`4aeb6b71`, L27b merged; R9-1 merged at `e40e0ae5`). Worktree `.claude/worktrees/meridian-lab`. Lock files are clean; only `tests/Dhadgar.Lab.Tests/TestResults/` is untracked.
- Run 10 at `4aeb6b71` was **not** the confirmation run (F118): it repeated run 9's set. Gate MD, twin, chaos, A3 and A11 (once) met again. Hunt diverged on F114's product 500, and its replay was `not_reproduced` (lab defect F120). The R9-1 flake check was refused on a seed above 2^63 (F119). Fuzz 28/50 500 (KG-W5-1).
- Wave 5 and 5E are **open** (D-ML134). Boundary review pass 4 has not run. No security pass yet on R8, R9-1 or L27b.
- NR-1 (worktree `nr1-nodes-ef-retry`, off `origin/main`): coded `159a72da` + CI fix `f50b01b0`; review `ship`, security `ship`. It waits on S-L15 green (L19n) before its PR.

## Next steps (orchestrator, in order)

1. Raise D-ML138 with the owner (default A); proceed on A meanwhile.
2. R10-1 (worktree `ml-wf-r10-seed-u64`) and L27c (worktree `ml-l27c`) in parallel: packet-coder, then packet-reviewer + security-advisor each. ops merges R10-1, then L27c.
3. Boundary review pass 4 (D-ML137): slices R1-R3, range `08571257..<tip after L27c>`, reviewer + security-advisor per slice.
4. Review fix notes, if any (not a fix wave). On `decision`, or `fix` after pass 3, ask the architect.
5. Confirmation run, run 10 ruling section 6: non-Docker suite, A1, A4, A5, A6, A10, A11 ×2, the R9-1 probe on a plain `lab-selftest.toml` run, the hunt consistency probe. Fresh daemon per segment. Reds are parked.
6. Architect boundary-close ruling.
7. L19n: S-L15 red on the tip, green in scratch with NR-1's diff. Then ops opens NR-1's PR; the owner merges (D-41). Then `main` merged in (merge, never rebase, D-38).
8. Follow-ups: L05b, L25g, L52b, L28b, L29b, L55b, L54b (architect specs each first). W8 deltas note before L40.
9. Waves 6 to 10 (wave 6 waits on L00, D-ML37/D-ML38). Campaign review (D-ML74: every Docker test, live hunt, S-L15). L47. One PR (D-ML131).

## Owner decisions

- Open: D-ML138 (R10-1, L27c before confirmation; default A), D-ML133 (Nodes outbox), D-ML120 (Nodes start without DB), D-ML107 (fuzz 500s), D-ML37/D-ML38 (wave 6).
- Non-blocking: D-ML31, D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73, OD-W5-1.

## Watch-outs

- Do not flake-check a mutation detector run (KG-W5-19). Run briefs must quote the ruling's procedure, not the previous run's.
- Docker classes use `[Collection(LabDockerGate.Name)]`. No bare `publish -r`, no `git worktree prune`; teardown is label-only. The `ml-*` worktrees stay until the campaign PR merges.
