# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md).

## State (2026-10-28, wave 3 built, review pass 1 done)

- Campaign branch `campaign/meridian-lab`, worktree `.claude/worktrees/meridian-lab` (local only, not pushed). Campaign tip **`e996406`** (before this ruling's commit). Lab tests: 552 pass, 0 fail, 4 skip (non-Docker).
- **Waves 1 and 2 are Integrated. Wave 3 (L12a, L13-L18, L48, L05f) is built and merged locally.** Wave 3 review pass 1 returned `fix` and security pass 1 returned `decision`; both are ruled in [worknotes/W3-ruling-1.md](worknotes/W3-ruling-1.md) (D-ML67 attestation, D-ML68 C9 scope). S-L6 has not run yet.
- **D-ML39 ruled (owner, 2026-10-28): one PR at the end.** D-ML16 stands, so ops pushes once after L47.
- **L00 is parked** (Windows containers off). Parked with it: L31-L33 and the container parts of wave 8.
- Reviews happen at wave boundaries and at campaign end only (owner, 2026-10-27).

## Next actions (orchestrator)

1. Dispatch W3-fix-A, W3-fix-B and W3-fix-C in parallel (worktrees `ml-w3fa`, `ml-w3fb`, `ml-w3fc`). Their files do not overlap. Merge them locally in any order.
2. Run the non-Docker suite on the merged tip, then S-L6 on the Docker host with `MERIDIAN_LAB_S6_WIPE=1`. Every row must PASS, `nodes-log` included.
3. Run wave 3 review pass 2 and security pass 2 over the fix range.
4. Wave 4 starts with L16f (D-ML68). Ask the architect for its packet text.
5. Before L36 or L49: ask the architect to rule on the doctor `ports` check, which flags the lab's own listeners (wave2-review-2 D4).
6. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`.

## Carry-overs

- The wave 1 security pass's "Not fixed" list binds later packets: `PathValidator` on every path, `http_request` only to `LabHosts.IsLabEndpoint`, redacted logs, and no `MERIDIAN_LAB_TOKEN` in child environments.
- Lab tests never probe the Gateway CA route (D-ML63). Enroll polling uses `poll_ms = 5000` (F54). 28 IL2026 warnings come from Agent.Core (#186).
- Owner action **O1**: add the D-ML33 promotion-rule line to CLAUDE.md.
- Owner action **O2** (D-ML67): add `mcp__meridian-lab__uat_attest` to `permissions.ask` in `.claude/settings.json`.

## Owner decisions open (non-blocking unless noted)

D-ML31, D-ML37/D-ML38 (parked), D-ML40-D-ML42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59. D-ML39 is decided. Recommendations are in the ledger README.

## Watch-outs

- Faults and the peer hit only lab targets. Never the dev stack. Never a bare `publish -r` (D-S241). S-L6 never removes containers without `MERIDIAN_LAB_S6_WIPE=1`.
- Mutant patches are never committed in patched form. No tool calls GitHub (D-ML28). Never `git worktree prune`. Worktrees `ml-*` stay until the campaign PR merges.
