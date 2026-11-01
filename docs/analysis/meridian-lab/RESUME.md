# RESUME: meridian-lab

Ledger: [README.md](README.md) (D-ML126 to D-ML133, F114 to F117, KG-W5-13 to KG-W5-17). Packets and contract: [work-packets.md](work-packets.md). Latest ruling: [wave5-run9-ruling](worknotes/wave5-run9-ruling.md), whose section 8 holds the 2026-11-01 owner rulings and the steps to the PR. Nodes product packet: [nodes-ef-retry-packet](worknotes/nodes-ef-retry-packet.md) (NR-1, L19n).

## State (2026-11-01)

- `campaign/meridian-lab` tip **4fe0b32a**, worktree `.claude/worktrees/meridian-lab`, **pushed** to origin (backup only, no PR). `origin/main` is `37560f0d`.
- Run 9 at `6899985d`: gate MD met; twin, chaos, A3 and A11 passed. The hunt gave `not_reproduced` (F114, F116), and fuzz gave 500s (KG-W5-1).
- Wave 5 and 5E are **open** (D-ML126). Owed: boundary review pass 4 and a confirmation run. D-ML125's hard stop holds: no more Docker fix waves.
- Owner rulings on 2026-11-01:
  - D-ML127: the hunt gate is parked as KG-W5-13, gated on L27b.
  - D-ML128: Nodes EF retry ships as a separate product PR, NR-1.
  - D-ML131: one PR after the last wave and L47.
  - D-ML118 and D-ML125 are ratified by action.
- The worktree has 8 dirty `tests/*/packages.lock.json` files and an untracked `tests/Dhadgar.Lab.Tests/TestResults/`. Neither is committed.

## Next steps (orchestrator, in order)

1. Boundary review pass 4 (D-ML129): R1 to R3, each a `packet-reviewer` plus a `security-advisor`, read-only. Range `08571257..2bb86769`.
2. **NR-1, in parallel with step 1.** ops creates worktree `nodes-ef-retry` on `fix/nodes-ef-retry` off **`origin/main`**, not off the lab branch. Then the packet-coder, the review loop and one `security-advisor` pass. Stop at the PR's "ready to merge".
3. R9-1 together with the review fix notes: packet-coder, then review loop, then local merge by ops. On `decision`, or on `fix` after pass 3, ask the architect for a ruling.
4. Confirmation run (D-ML130) on a fresh daemon. A red is parked, not fixed.
5. L19n (worktree `ml-l19n`): S-L15 recorded red. Then NR-1's scratch green check (packet section 4.6). Then ops opens NR-1's PR, which the owner merges (D-41).
6. Architect closes waves 5 and 5E and copies the R7 and R8 report facts into `worknotes/`.
7. ops restores the lock files and removes `TestResults/`, then merges `origin/main` in (merge, never rebase, D-38). A packet-coder fixes any conflicts and runs the build and the non-Docker suite. ops pushes.
8. Architect writes the W8 deltas note before L40.
9. Follow-ups: L27b (spec ready, before the campaign review), then L05b, L25g, L52b, L28b, L29b and L55b. The architect specs each one first.
10. Waves 6 to 10. Wave 6 waits on L00 and D-ML37/D-ML38.
11. Campaign review (D-ML74). It reruns every Docker test, the live hunt (KG-W5-13) and S-L15 (KG-W5-17). Then L47. Then ops opens **one** PR with `security-review`, merged on the owner's yes.

## Owner decisions

- Open, blocking or next: D-ML37/D-ML38 (L00, wave 6), D-ML107 (fuzz 500s) and D-ML133 (Nodes `UseBusOutbox`; blocks no step).
- Non-blocking: D-ML120, D-ML31, D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73 and OD-W5-1.

## Watch-outs

- Docker classes use `[Collection(LabDockerGate.Name)]`. Never run a bare `publish -r`, and never `git worktree prune`. Teardown is label-only.
- Each boundary segment runs on a fresh daemon; the lab home stays in the scratchpad. The `ml-*` worktrees stay until the campaign PR merges.
