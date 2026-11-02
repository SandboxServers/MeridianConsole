# RESUME: meridian-lab

Ledger: [README.md](README.md) (newest: F125, D-ML143, KG-W5-21). Packets and contract: [work-packets.md](work-packets.md). Latest ruling: [wave5-close-ruling](worknotes/wave5-close-ruling.md), with L27d and L27e in section 6 and the next steps in section 7. Nodes product packet: [nodes-ef-retry-packet](worknotes/nodes-ef-retry-packet.md) (NR-1, L19n).

## State (2026-11-02)

- `campaign/meridian-lab` tip is **99bb31e3** (the run 11 record), on top of `7351c6f7` (R11-1 merged). The worktree is `.claude/worktrees/meridian-lab`.
- **Waves 5 and 5E are closed (D-ML142).** Run 11 passed every section 6 clause.
  - KG-W5-16, -15 and -7 (for 5E) are closed. KG-W5-18 is closed for new ingests.
  - Two hunter lab defects are parked: F124 (the seed fixes only the first case; a flaky test) as KG-W5-21, and F125 (the cut-window `EnrollAgain` I1 is not excused) inside KG-W5-13.
- NR-1 (worktree `nr1-nodes-ef-retry`, off `origin/main`) is coded at `159a72da` + `f50b01b0`, with review and security `ship`. It waits on S-L15 green.

## Next steps (orchestrator)

1. Have ops delete `tests/Dhadgar.Lab.Tests/TestResults/` in the campaign worktree (explicit path). It is untracked and is the likely cause of the `-dirty` build ids. No lock file is dirty.
2. **L19n** (worktree `ml-l19n`), on a fresh daemon: S-L15 red on the tip, then green in scratch with NR-1's diff. If the first probe answers 401 before NR-1, the packet's hard stop applies. Then ops opens NR-1's PR to `main`, and the owner merges it (D-41).
3. Merge `origin/main` into the campaign branch: merge, never rebase (D-38). Then rerun `RequiresDocker!=true`, S-L15 (KG-W5-17), A6 and A11 on a fresh daemon.
4. In parallel with steps 2 and 3:
   - **L27d**, then **L27e** (close ruling 6.1 and 6.2). Each gets a coder, a reviewer and a security-advisor. Both must land before the campaign review.
   - **L30 ‖ L34 ‖ L36** are Ready. They merge one at a time, rebasing on the tip each time.
5. Ask the architect for the W8 deltas note (needed before L39 and L40) and for specs of L29b, L54b, L55b, L05b, L25g, L28b and L52b.
6. Run the close-out doc sweep (`wf-r7.md`, `wf-r8.md`, the old `Category!=Docker` lines). Then L31-L33 once L00 unparks, waves 7 to 10, the campaign review (D-ML74: every Docker test, the live hunt, S-L15), L47, and one PR with `security-review` (D-ML131).

## Owner decisions

- Open, none blocking: D-ML138 (carried out on option A; ratify), D-ML133 (Nodes outbox), D-ML120 (Nodes start without DB), D-ML107 (fuzz 500s), D-ML37/D-ML38 (only if Windows containers turn on).
- Non-blocking: D-ML31, D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73, OD-W5-1.

## Watch-outs

- The non-Docker filter is `RequiresDocker!=true`. Run briefs list "lab network absent" and "no TestResults folder" as step 0 preconditions.
- A hunt `diverged` result is not evidence until L27d and L27e land. Quote the job payload, not only the audit.
- Bundle rows are at `rows/<section>/<row>.json`. Do not flake-check a detector run (KG-W5-19).
- The CLI reaches port 8789 only (D-ML139). Never print the lab token or a lease id.
- No `publish -r`, no `restore -r` and no `git worktree prune`. Teardown is label-only. The `ml-*` worktrees stay until the campaign PR merges.
