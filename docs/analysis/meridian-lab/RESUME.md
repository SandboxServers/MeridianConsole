# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md).

## State (2026-10-27, wave 1 closed)

- Campaign branch `campaign/meridian-lab`, worktree `.claude/worktrees/meridian-lab`, tip **`ac6f959`** (local only, not pushed). Base `origin/main` `c0a7ee9`.
- **Wave 1 (L01-L06) is Integrated.** Wave review pass 3 `ship`; wave security pass 2 `ship`. Lab tests: 169 pass, 1 red by design (S-L2, `NodesEnrollSeamTests`, green in L11). S-L1 is green.
- **L00 is parked** (blocked on `docker`): the Windows engine never started because the `Containers` and Hyper-V features are off. Worknote `worknotes/L00-windows-containers-spike.md` is on the branch.
- **Owner decision 2026-10-27: skip Windows containers for now** (D-ML37/D-ML38 unruled). Parked: L31, L32, L33 and the container parts of wave 8 (L43, and the container-dependent pieces of L41, L59, L60). Waves 2-5 and 7 proceed.
- **Owner decision 2026-10-27: reviews at wave boundaries and campaign end only.** No per-packet review loops. Each wave gets a wave reviewer plus a `security-advisor` (security-review areas); fix notes go to fresh coders per packet.

## Next actions (orchestrator)

1. Wave 2: L07, L08, L12 (all depend only on L05) are dispatched from worktrees `ml-l07`, `ml-l08`, `ml-l12` (branches `ml/l07-supervisor`, `ml/l08-docker`, `ml/l12-probe`). Then L09 (L07+L08), L10 (L08), L11 (L09+L10; turns S-L2 green).
2. Merge each packet locally into the campaign branch (Sonnet general-purpose, one at a time); build and run the lab tests after each.
3. Ask the user D-ML39 (one PR vs one per milestone) before wave 3 ends.
4. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`.

## Carry-overs from wave 1 reviews

- The security pass's "Not fixed" list is binding for later packets: validate every path from a tool or config with `PathValidator` (symlinks and junctions) from L07 onward; `http_request` must use `LabHosts.IsLabEndpoint`, refuse redirects; packets that log values must redact; remove `MERIDIAN_LAB_TOKEN` from child environments in the L07 process-start helper.
- 28 IL2026 warnings come from `Dhadgar.Agent.Core` (exempt, issue #186); not a lab defect.
- Owner action **O1**: add the D-ML33 promotion-rule line to CLAUDE.md.

## Owner decisions open (non-blocking unless noted)

D-ML39 (before wave 3 ends), D-ML31, D-ML37/D-ML38 (parked), D-ML40-D-ML42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59. Recommendations are in the ledger README.

## Watch-outs

- Faults and the peer hit only lab targets (`FaultTargetGuard`, `PeerTargetGuard`). Never the dev stack. Never a bare `publish -r` (D-S241).
- Mutant patches are never committed in patched form. No tool calls GitHub (D-ML28). Never `git worktree prune`.
- Packet and fix worktrees under `.claude/worktrees/ml-*` stay until the campaign PR merges.
