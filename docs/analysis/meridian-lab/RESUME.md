# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md).

## State (2026-10-27, wave 2 closed)

- Campaign branch `campaign/meridian-lab`, worktree `.claude/worktrees/meridian-lab` (local only, not pushed). Tip **`310887b`**. Base `origin/main` `c0a7ee9`; `main` has moved on (L09f #237 `c5ca3c4` is already merged into the branch).
- **Waves 1 and 2 (L01-L12) are Integrated.** Wave 2 review pass 3 `ship`, security pass 2 `ship`. Lab tests: 308 pass, 0 fail, 4 skipped; `CI=true` build clean. L09f (`RabbitMq:Port`, D-ML62) is Done as its own PR (#237). S-L1, S-L2 and S-L3 are green.
- Wave 2 rulings are in [worknotes/W2-ruling-3.md](worknotes/W2-ruling-3.md) (D-ML63 probes, D-ML64 doctor, D-ML65 registration areas, F54 rate limits). No owner decision needed.
- Open for the architect before L49/L36: the doctor `ports` check flags the lab's own in-daemon listeners (barrier 25080, sink 18090) as foreign (wave2-review-2 D4).
- **L00 is parked** (Windows containers off; owner, 2026-10-27). Parked with it: L31, L32, L33 and the container parts of wave 8 (L43; container pieces of L41, L59, L60).
- **Owner decision 2026-10-27: reviews at wave boundaries and campaign end only.** Each wave gets a wave reviewer plus a `security-advisor`; fix notes go to fresh coders per packet.

## Next actions (orchestrator)

1. Wave 3 begins. Coders context-stop often on big packets: tell them to commit after each step and keep tool output short.
2. L12a (wave 3, first): registration areas and the policy-derived tool set. Every later tool packet carries `R-REG: area <Area>`.
3. Then L13 → L14 → L15 → L16 → L48 → L17 → L18.
4. Ask the user D-ML39 (one PR vs one per milestone) before wave 3 ends.
5. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`.

## Carry-overs

- The wave 1 security pass's "Not fixed" list binds later packets: `PathValidator` on every tool or config path (symlinks and junctions); `http_request` only to `LabHosts.IsLabEndpoint`, no redirects; redact logged values; no `MERIDIAN_LAB_TOKEN` in child environments.
- Lab tests never probe the Gateway CA route (VS-07R, D-ML63). Enroll-route polling uses `poll_ms = 5000` (F54).
- 28 IL2026 warnings come from `Dhadgar.Agent.Core` (exempt, issue #186).
- Owner action **O1**: add the D-ML33 promotion-rule line to CLAUDE.md.

## Owner decisions open (non-blocking unless noted)

D-ML39 (before wave 3 ends), D-ML31, D-ML37/D-ML38 (parked), D-ML40-D-ML42, D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59. Recommendations are in the ledger README.

## Watch-outs

- Faults and the peer hit only lab targets (`FaultTargetGuard`, `PeerTargetGuard`). Never the dev stack. Never a bare `publish -r` (D-S241).
- Mutant patches are never committed in patched form. No tool calls GitHub (D-ML28). Never `git worktree prune`.
- Packet and fix worktrees under `.claude/worktrees/ml-*` stay until the campaign PR merges.
