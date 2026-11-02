# RESUME: meridian-lab

Ledger: [README.md](README.md) (newest: F132, D-ML152, KG-W7-5; next free F133, D-ML153, KG-W5-24, KG-W7-6; F127 reserved). Packets and contract: [work-packets.md](work-packets.md). Latest rulings: [l34 ruling 2](worknotes/l34-ruling-2.md), [docker-run-12](worknotes/docker-run-12-ruling.md), [l36 ruling 1](worknotes/l36-ruling-1.md), [l19n ruling 1](worknotes/l19n-ruling-1.md). Run record: [wave5-docker-run](worknotes/wave5-docker-run.md) (runs 13, 14).

## State (2026-11-02, wave boundary)

- `campaign/meridian-lab` tip is **fdebb5e1** (worktree `.claude/worktrees/meridian-lab`). Waves 5 and 5E closed (D-ML142); `main` with NR-1 (#256) merged at `8191eb8f`.
- Merged since: L21g `a0935402` (KG-W5-22 closed), L08b `a49c1786`, L30 `cdc8c73b`, L19n with L19o and the fix pass `ab7a0116` (review 3 `ship`), L34 with L34b and L34c `a4a69e6a` (reviews and security `ship`), L36 with L36b `fdebb5e1` (review 2 `ship`).
- Run 13: S-L15 red on scratch accepted (500, Npgsql failure), green genuine (6 × 401, EF retry); S-L7 passed in-network; Notify passed; AuthStack red (F131, fixed by L34c). Run 14: AuthStack passed at `f599f371`. KG-W5-17, KG-W5-23, KG-W7-4 closed; D-ML144 record done.
- Tip review 1 (`.claude/reports/meridian-lab/wave5-tip-review-1.md`) is **in review**.
- F132 (Identity honours the dev-certificate switch in Production) is an Identity backlog item, not a campaign packet.

## Next actions (orchestrator)

1. Read tip review 1. On `fix`, a packet-coder applies its notes on the tip; on `decision`, ask the architect for a ruling.
2. Follow-ups: **L27d**, then **L27e** (before the campaign review); ask the architect to spec **L29b** and **L54b** (before the wave 10 gates).
3. Ask the architect for `worknotes/W8-sprint2-deltas.md` before L40.
4. Waves 6-10: wave 6 (L31-L33) waits on L00 and D-ML37/D-ML38; wave 7 continues with L35, L37, L38, L56; then 8, 9, 10.
5. Campaign review (D-ML74: every Docker test, the live hunt, S-L15).
6. L47 close-out.
7. ops pushes once and opens one PR with `security-review`, merged on the user's yes (D-ML131).

## Open owner items

- D-ML133 (Nodes outbox), D-ML107 (fuzz 500s), D-ML120 (Nodes start without its DB). Also open, non-blocking: D-ML138 (ratify by action), D-ML37/D-ML38.

## Cleanup (ops; `git worktree remove` only, **never prune**)

- `nr1-nodes-ef-retry` and the `ml-l19n-red` scratch (branch `ml/l19n-red-scratch`).
- Merged fix-wave worktrees R7-R11: `ml-wf-r7-*` (4), `ml-wf-r8-*` (3), `ml-wf-r9-flake-deps`, `ml-wf-r10-seed-flake`, `ml-wf-r11-boundary-fixes`.
- Merged packet worktrees: `ml-l21g`, `ml-l08b`, `ml-l30`, `ml-l34`, `ml-l36`, `ml-l19n`.
- About 87 local `ml/*` branches: delete merged ones with `git branch -d` after their worktrees are gone.

## Watch-outs

- Non-Docker filter is `RequiresDocker!=true`. Proxy listeners are probed in-network only (D-ML149).
- A hunt `diverged` is not evidence until L27d and L27e land. Do not flake-check a detector run (KG-W5-19).
- No `publish -r`, no `restore -r`, no `git worktree prune`. Teardown is label-only. Never print the lab token or a lease id.
