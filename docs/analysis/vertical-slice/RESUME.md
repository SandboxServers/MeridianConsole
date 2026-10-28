# RESUME: vertical-slice

**State (2026-10-28, late; Wave 2 in review):**
- `main` is `101c36d` (Wave 1 and Wave 2 stage 1 merged; SHAs in README). CI green.
- Decisions run to D-51. D-44 and D-46 are unrecovered (do not reuse). **D-51: Cady (`Cadacious`) merges the campaign PRs; no session merges.**
- Ledger edits ship in PR #244 (branch `campaign/vertical-slice-ledger-2`, worktree `vertical-slice-ledger2`).

**Wave 2 PRs** (merge order; all `security-review`; each stops at "ready to merge" for Cady):

| Order | PR | Packet | Worktree | State |
| --- | --- | --- | --- | --- |
| 1 | #248 | HF-02 | `vertical-slice-hf02` | review ship (p1), security ship (p2) |
| 2 | - | VS-09 | `vertical-slice-vs09` | security ship (p2); review fix pass 2, then review 3 |
| 3 | #249 | VS-08R | `vertical-slice-vs08` | security ship (p2); ruling applied, ruling check ship |
| 4 | #245 | VS-36 | `vertical-slice-vs36` | review ship (p2), security ship (p1); CI green |
| 5 | #247 | VS-06R | `vertical-slice-vs06` | review ship (p2), security ship (p1) |
| 6 | #246 | VS-35 | `vertical-slice-vs35` | review ship (p2), security ship (p2) |
| any | #244 | ledger | `vertical-slice-ledger2` | docs only |

**Next actions, in order:**
1. Finish VS-09's review loop; open its PR.
2. When every Wave 2 PR is green, hand the list to Cady in order. VS-09, VS-08R and VS-06R all edit Nodes `Program.cs`: after each merge, merge `main` into the next PR branch (D-38) and wait for green before Cady merges it.
3. Owner steps: HF-02D Cloudflare checklist; HF-02 owner steps 2 and 3 (trusted origins set, then `DELETE FROM "session"` or rotate `BETTER_AUTH_SECRET` after deploy).
4. Remove each packet worktree once its PR merges (copy reports worth keeping to `worknotes/` first).
5. Contract sync 2 (ruling § 4, plus merging the two `FilteredPagedResponse` types, D-47), then Wave 3.

**Follow-ups collected in Wave 2 (route at contract sync 2):**
- The Gateway's `TenantScoped` policy doesn't compare the path org with the token's `org_id`; confirm every service enforces the org match (VS-35 security-1).
- VS-09 N7: a runbook note says the signing key lives with the CA directory and must be backed up. Ephemeral storage means re-enrolling every agent. Goes to the Nodes docs packet.
- Carried to VS-14: keyless agents refuse every command; #94 stays open until agent-side verification lands. VS-13/14 wire `CommandTtlSeconds`.
- VS-08R: abort live hub connections on decommission (owner in worknotes/VS-08R.md).
- `src/Dhadgar.BetterAuth/CLAUDE.md` still describes the old cookie domain.

**Watch:**
- `gh`: `C:\Program Files\GitHub CLI\gh.exe`. Node/npm are installed; refresh PATH from Machine+User in each shell.
- Never commit the file-mode-noise scripts or restore churn in `src/Agents/*/packages.lock.json`.
- D-38: merge `main` in, never rebase or force-push. Push every coder commit as soon as it exists.
- Never `git worktree prune`. Seams live in `tests/Dhadgar.AgentSeam.Tests`; `SeamJwt` is `Dhadgar.Testing.Authentication` (D-43).
