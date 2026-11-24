# RESUME: vertical-slice

**State (2026-11-24, restart after a pause; Wave 2 merged):**
- `main` is `a5d408c` (2026-11-03). Waves 1 and 2 are fully merged (SHAs in README). No campaign work has run since 2026-11-01; `main` has since taken only meridian-lab product fixes (#256 to #261) and Dependabot bumps.
- Decisions run to D-52. D-44 and D-46 are unrecovered (do not reuse). **D-52: Cady (`Cadacious`) or Joshua (`BangRocket`) merges campaign PRs; no session merges on its own initiative.**
- No vertical-slice worktrees remain on the owner's machine (checked 2026-11-24; only the main checkout and `restart-rebaseline`).

**Next actions, in order:**
1. Contract sync 2 (architect): ruling § 4, merge the two `FilteredPagedResponse` types (D-47), and route the Wave 2 follow-ups below. Check what #261 (Nodes list/detail on Contracts DTOs, outside the campaign) already did for D-47 before speccing it.
2. Wave 3: VS-12, VS-15 and VS-17 first (they depend only on W2 and contract sync 2), then VS-13, VS-14 and VS-33, then VS-16. VS-12 shrinks per D-37; re-check its scope against Sprint 2's `FileAgentStateStore` and VS-09's signing-key fields.
3. **Deploy gate, not current work** (owner, 2026-11-24): before the next deploy to dev, staging or prod, run the HF-02D Cloudflare checklist and HF-02 owner steps 2 and 3 (trusted origins set, then `DELETE FROM "session"` or rotate `BETTER_AUTH_SECRET`). Checklist: HF-02 in work-packets.md.

**Follow-ups collected in Wave 2 (route at contract sync 2):**
- The Gateway's `TenantScoped` policy doesn't compare the path org with the token's `org_id`; confirm every service enforces the org match (VS-35 security-1).
- VS-09 N7: a runbook note says the signing key lives with the CA directory and must be backed up. Ephemeral storage means re-enrolling every agent. Goes to the Nodes docs packet.
- Carried to VS-14: keyless agents refuse every command; #94 stays open until agent-side verification lands. VS-13/14 wire `CommandTtlSeconds`.
- VS-08R: abort live hub connections on decommission (owner in worknotes/VS-08R.md).
- `src/Dhadgar.BetterAuth/CLAUDE.md` still describes the old cookie domain.

**Watch:**
- meridian-lab runs in parallel on `campaign/meridian-lab` (owner, 2026-11-24); its product fixes land as separate PRs. Before speccing a Wave 3 packet that touches Nodes, Identity, ServiceDefaults or the CLI, check `main` for lab fixes in the same files (#256 to #263).
- `gh`: `C:\Program Files\GitHub CLI\gh.exe`. Node/npm are installed; refresh PATH from Machine+User in each shell.
- Never commit the file-mode-noise scripts or restore churn in `src/Agents/*/packages.lock.json`.
- D-38: merge `main` in, never rebase or force-push. Push every coder commit as soon as it exists.
- Never `git worktree prune`. Seams live in `tests/Dhadgar.AgentSeam.Tests`; `SeamJwt` is `Dhadgar.Testing.Authentication` (D-43).
