# RESUME: meridian-lab

Ledger: [README.md](README.md) (newest: F132, D-ML159, KG-W7-5; next free F133, D-ML160, KG-W5-24, KG-W7-6; F127 reserved). Packets and contract: [work-packets.md](work-packets.md). Plan of record for the next dispatches: [next-batch plan](worknotes/next-batch-plan.md). Wave 8 names: [W8 deltas](worknotes/W8-sprint2-deltas.md).

## State (2026-11-02)

- `campaign/meridian-lab` tip is **143b5b02**, pushed (worktree `.claude/worktrees/meridian-lab`). Waves 5 and 5E are closed (D-ML142). L21g, L08b, L30, L19n/L19o, L34/L34b/L34c and L36/L36b are merged.
- Tip review 1 closed `ship` on pass 3 (fixes `90efff87`, `143b5b02`).
- Ready: L27d, L29b, L54b, L39 (batch 1); L35m, L35, L37 (serial). L27e waits on L27d.
- F132 (Identity honours the dev-certificate switch in Production) is an Identity backlog item, not a packet.

## Next actions (orchestrator)

1. **Batch 1** (no Docker; at most 4 coders, D-ML153): L27d, L29b, L54b, L39. Each runs its build and its scoped filter only. Security pass for L27d, L54b, L39. Merge L39 → L27d → L29b → L54b.
2. After the merges: one serial full non-Docker suite on the tip (check-only task). A red result gets a fix packet.
3. Serial: L27e (after L27d); L35m (Docker yes), then L35 (Docker and network yes); L37 after L54b (D-ML159).
4. Architect: spec L05b, L25g, L28b, L55b; then L52b; L62 after L27e, L29b, L52b (D-ML158); then L38 and L56.
5. Wave 7 boundary review, then the waves 5-7 Docker tests (D-ML74).
6. Waves 6, 8, 9, 10 wait on D-ML37/D-ML38 (L00). Then the campaign review, L47 and one PR (D-ML131).

## Open owner items (BlockedDecision; questions in the plan, section 3)

- **D-ML37/D-ML38** (Windows containers, separate `dockerd`): recommend yes. This blocks wave 6 and everything after it.
- D-ML133 (Nodes outbox PR): recommend A. D-ML107 (fuzz 500s product PR): recommend yes. D-ML120 (Nodes start without DB): recommend no change. D-ML138: ratify.

## Cleanup (ops; `git worktree remove` only, **never prune**)

- `nr1-nodes-ef-retry` and the `ml-l19n-red` scratch (branch `ml/l19n-red-scratch`).
- Fix-wave worktrees R7-R11: `ml-wf-r7-*` (4), `ml-wf-r8-*` (3), `ml-wf-r9-flake-deps`, `ml-wf-r10-seed-flake`, `ml-wf-r11-boundary-fixes`.
- Packet worktrees: `ml-l21g`, `ml-l08b`, `ml-l30`, `ml-l34`, `ml-l36`, `ml-l19n`; add the batch 1 worktrees after merge.
- About 87 local `ml/*` branches: delete merged ones with `git branch -d` after their worktrees are gone.

## Watch-outs

- The non-Docker filter is `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`. Never run two full Lab suites at once.
- A hunt `diverged` is not evidence until L27d and L27e land. Do not flake-check a detector run until L54b lands (KG-W5-19).
- No `publish -r`, no `restore -r`, no `git worktree prune`. Never print the lab token or a lease id.
