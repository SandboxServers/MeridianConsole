# RESUME: meridian-lab

Ledger: [README.md](README.md) (D-ML139 to D-ML141, F123, KG-W5-20 newest). Packets and contract: [work-packets.md](work-packets.md). Latest ruling: [wave5-boundary4-ruling](worknotes/wave5-boundary4-ruling.md) (R11-1 in section 5, confirmation run in section 6, close in section 7). Nodes product packet: [nodes-ef-retry-packet](worknotes/nodes-ef-retry-packet.md) (NR-1, L19n).

## State (2026-11-02)

- `campaign/meridian-lab` tip **0c1900b3** (L27c merged; R10-1 merged `1ce5584a`). Worktree `.claude/worktrees/meridian-lab`.
- Boundary review pass 4 (range `08571257..0c1900b3`): R1 `fix`, R3 `fix`, R2 `ship`, security `ship`. Past the 3-pass limit; the boundary 4 ruling decides every note.
- Wave 5 and 5E are **open**. No further review pass: R11-1's ruling check is the last review on this boundary.
- NR-1 (worktree `nr1-nodes-ef-retry`, off `origin/main`): coded `159a72da` + `f50b01b0`; review, security-1 and boundary security 4 `ship`. Waits on S-L15 green (L19n).

## Next steps (orchestrator, in order)

1. R11-1 (worktree `ml-wf-r11-boundary4`, branch `ml/wf-r11-boundary4-fixes`): one packet-coder, then one fresh packet-reviewer + one security-advisor (ruling check). Not `ship` → BlockedDecision, ask the architect. ops merges.
2. Confirmation run: quote boundary 4 ruling section 6 **verbatim** in the brief (D-ML140). Steps: CLI probe, R9-1 probe on plain `lab-selftest.toml` seed `12499590173714643239`, hunt replay probe on two daemons, plus the non-Docker suite and A1, A4, A5, A6, A10, A11 ×2. Reds are parked.
3. Architect boundary-close ruling (section 7).
4. L19n: S-L15 red on the tip, green in scratch with NR-1's diff. Then ops opens NR-1's PR; the owner merges (D-41); `main` merged in (merge, never rebase, D-38).
5. Follow-ups: L05b, L25g, L52b, L28b, L29b (now with KG-W5-20 and soak `rows`), L55b, L54b (now with H3, H4); architect specs each first. Close-out doc sweep: `wf-r7.md`, `wf-r8.md`, old `Category!=Docker` lines (F123). W8 deltas note before L40.
6. Waves 6 to 10 (wave 6 waits on L00, D-ML37/D-ML38). Campaign review (D-ML74: every Docker test, live hunt, S-L15). L47. One PR (D-ML131); it needs the `security-review` label.

## Owner decisions

- Open: D-ML138 (carried out on option A; ratify), D-ML133 (Nodes outbox), D-ML120 (Nodes start without DB), D-ML107 (fuzz 500s), D-ML37/D-ML38 (wave 6).
- Non-blocking: D-ML31, D-ML40-42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59, D-ML71, D-ML73, OD-W5-1.

## Watch-outs

- Non-Docker suite filter is `RequiresDocker!=true`. `Category!=Docker` excludes nothing (F123).
- Bundle rows are `rows/<section>/<row>.json`, not `rows.jsonl`. Do not flake-check a mutation detector run (KG-W5-19).
- The CLI reaches only port 8789 (D-ML139). Release the lease before OwnLease tools. Never print the lab token or a lease id.
- Docker classes use `[Collection(LabDockerGate.Name)]`. No bare `publish -r`, no `restore -r`, no `git worktree prune`; teardown is label-only. The `ml-*` worktrees stay until the campaign PR merges.
