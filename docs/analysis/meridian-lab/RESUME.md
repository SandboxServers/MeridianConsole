# RESUME: meridian-lab

Ledger: [README.md](README.md). Packets and contract (C1-C20): [work-packets.md](work-packets.md).

## State (2026-10-27)

- Plan only, second pass: 63 packets (L00-L62) in 13 waves (0-3, 4, 4H, 5, 5E, 6-10). Nothing built yet, no campaign branch. PR #227 (the plan) is not merged unless the user says so.
- **Ready:** L00 (Windows-container spike, worknote only) and L01. Everything else is BlockedDependency. No decision blocks any wave; the open ones only keep their rows `required = false`.
- Sprint 2 is on `main` (`6df3338`, D-ML60). Wave 8 needs the architect's W8 ruling note, not a merge.
- The four first-pass defects are fixed in the contract (F50, D-ML46) and each has a lab mutant the self-check must catch (gate LS).

## Next actions (orchestrator)

1. ops: worktree `.claude/worktrees/meridian-lab` on a new branch `campaign/meridian-lab` from `main`.
2. Dispatch L00 beside L01, then L02, L03, L04 → L05 → L06. Per packet: `packet-coder`, then `packet-reviewer` + `security-advisor`.
3. When L00's worknote lands, ask for an architect ruling that fills L31's "Settings from L00". Raise D-ML37 or D-ML38 only if the outcome needs them.
4. Before L40: ask the architect for `worknotes/W8-sprint2-deltas.md`.
5. Raise the owner decisions below when convenient. Rule D-ML39 before wave 3 ends.

## Owner decisions (recommendation)

| ID | Question and recommendation | Until ruled |
| --- | --- | --- |
| D-ML31 | VM tier (clock skew, reboots, client SKUs): follow-up after wave 10 | out of scope |
| D-ML37 / D-ML38 | Hyper-V + Containers; separate Windows `dockerd`: yes, if L00 needs them | wave 6 waits |
| D-ML39 | One PR per milestone M1-M7: yes | D-ML16 (one PR) |
| D-ML40 / D-ML41 / D-ML42 | SMTP and cookie-domain switches: allow; Discord bot: out; drafts filed by the owner | as written |
| D-ML49 | Idempotent enroll retry (same token and key → same node): yes | E5 not required |
| D-ML53 | Clone: the newer hub connection replaces the older | observed only |
| D-ML54 | Drop hub connections within 10 s of revoke or decommission | idle variant observed |
| D-ML56 | Games survive control-plane outage and agent restart | continuity rows observed |
| D-ML58 | Resurrected identity refused; fresh enrollment required | row observed |
| D-ML59 | Rollback (old Nodes, new schema) unsupported, documented | row observed |

Owner action **O1**: add the promotion-rule line (D-ML33) to CLAUDE.md.

## Watch-outs

- Faults and the peer hit only lab targets and lab-enrolled identities (`FaultTargetGuard`, `PeerTargetGuard`, BR). Never the dev stack.
- Mutant patches are never committed in patched form; `git status` shows only `tools/lab/`, `src/Tools/`, `tests/Dhadgar.Lab*`.
- Observation paths bypass Toxiproxy (D-ML34). Red seams excluded by `Seam!=S-Lx`; CI is ubuntu-24.04 with trait filters.
- No tool calls GitHub (D-ML28). Never a bare `publish -r` (D-S241). Parallel waves merge one at a time.
