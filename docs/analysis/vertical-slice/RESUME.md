# RESUME: vertical-slice

**State (2026-10-27, late):**
- `main` is `5eead35`. It holds Sprint 2 `6df3338`, then #233 VS-02, #230 VS-04 and #231 VS-05, all merged by another session without the owner's yes, then #236 (lock fix).
- Reconcile ruling: [worknotes/sprint2-reconcile-ruling.md](worknotes/sprint2-reconcile-ruling.md). Section 9 covers this state, and the packets are in [work-packets.md](work-packets.md).
- Decisions run to D-42. D-40 is lenient JSON. D-41 is merge authority. D-42 folds Seams.Tests and removes `IHubConnectionConfigurator`.
- These edits are uncommitted in worktree `vertical-slice-ledger` (PR #234, based on an old `main`).

**Wave 1, remaining:**

| Order | PR | Packet | What it needs | Security |
| --- | --- | --- | --- | --- |
| 1 | #234 | ledger | ops merges `main` in, commits these edits | No |
| 2 | #229 | VS-03R | merge `main` (clean), VS-03R packet, review | Yes |
| 3 | #232 | VS-01 | merge `main` per revised resolutions (ruling § 9.4) | No (label) |
| 4 | new | VS-34 | fold Seams.Tests into AgentSeam.Tests, drop the hook; merge `main` after #232 | Yes |

Already merged, with nothing left except follow-ups: #230 (FU-41a post-merge security pass), #233, #231 (VS-34 removes what it added).

**Next actions, in order:**

1. Tell the owner about the three unapproved merges (FU-41e).
2. ops merges `main` into #234 (D-38) and commits the ledger edits.
3. The #232 merge-main coder applies the revised resolutions. Four things changed:
   - keep the `Seams.Tests` sln block;
   - parked lock files keep `main`'s content;
   - the grep prints the two Scope `.astro` files;
   - the merge subject cites `5eead35`.
4. Dispatch VS-03R (#229) and VS-34. Dispatch the FU-41a `security-advisor` pass (read-only) on `main`'s Gateway auth block.
5. Each PR in order: review, plus a security pass where the table says so, then the **owner's yes**, then the owner's main session merges it (D-41).
6. Wave 2, after wave 1 and FU-41a. Stage 1: VS-11, VS-32R, VS-07R, VS-09, VS-08R. Stage 2: VS-06R. Merge in that order. Before VS-11, check `origin/pr-88` = `0761b97`. Before wave 3: **contract sync 2** (ruling § 4).

**Watch:**

- D-41: only the owner's main session merges, and only after the owner says yes to that PR. Every other session stops at "ready to merge".
- D-38: merge `main` into PR branches; never rebase or force-push. D-39: lock-file rule; never run a bare `publish -r` or `restore -r`.
- Never `git worktree prune`. `gh` GraphQL is blocked: use REST (`gh api`).
- `SeamJwt` source for VS-32R: `git show 6895cd1:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs`.
- Seams live in `tests/Dhadgar.AgentSeam.Tests`. Rejection seams prove which server answered and have a positive twin.
- Windows-only behaviour counts only with a recorded run. PROJECT-STATE changes only in VS-28.
