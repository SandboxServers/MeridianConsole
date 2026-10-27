# RESUME: vertical-slice

**State (2026-10-26, evening):** wave 1 in flight. Ledger in worktree `vertical-slice-ledger` (branch `campaign/vertical-slice-ledger`, cut from `9a1a3a6`, new files only; rebase onto `origin/main` before its PR). `.claude/state/active-campaign` = `vertical-slice`.

**Base for wave 1:** `origin/main` `2c4c19c` (PR #187 merged, D-04).

**Update 2026-10-27 10:50:** all five W1 packets passed review (VS-01 292ab64, VS-02 f3956ac, VS-03 eabdbe5, VS-04 caa68b0, VS-05 44097ee). Running: wave 1 review pass 1 (report `vertical-slice-ledger/.claude/reports/vertical-slice/wave1-review-1.md`) and the contract-sync architect (resumed after a usage-limit stop; report `contract-sync-1.md`). Next: ship in order VS-03, VS-04, VS-05, VS-01, VS-02; owner yes before merging VS-03/04/05.

**Current wave:** 1 of 5. 28 packets: W1 5, W2 6, W3 6, W4 6, W5 5 (W4 may change after the D-19 ruling).

**Packets in flight (packet-coder, background):**

| Packet | Worktree | Branch | Security |
| --- | --- | --- | --- |
| VS-01 | `.claude/worktrees/vertical-slice-vs01` | `vs/01-park-projects` | No |
| VS-02 | `.claude/worktrees/vertical-slice-vs02` | `vs/02-park-routes` | No |
| VS-03 | `.claude/worktrees/vertical-slice-vs03` | `vs/03-agent-contracts` | Yes |
| VS-04 | `.claude/worktrees/vertical-slice-vs04` | `vs/04-service-jwt` | Yes |
| VS-05 | `.claude/worktrees/vertical-slice-vs05` | `vs/05-seams-harness` | Yes |

Also running: architect D-19 ruling (re-spec VS-19/VS-21 for owner's "agent also downloads a JRE"); report `vertical-slice-ledger/.claude/reports/vertical-slice/D-19-ruling-1.md`.

**Next actions, in order:**

1. On each coder `done`: packet review pass 1 (packet-reviewer), plus security-advisor for VS-03/04/05.
2. When all five reach `ship`: wave 1 review, then ship through ops in merge order VS-03, VS-04, VS-05, VS-01, VS-02. Security PRs need the `security-review` label plus the owner's yes before merge.
3. After the D-19 ruling returns: ops ships the ledger PR (rebased on `origin/main`).
4. Before wave 2: ops checks `origin/pr-88` = `0761b97` (fetch `refs/pull/88/head:refs/remotes/origin/pr-88` if missing), then worktrees for VS-06, VS-07, VS-09, VS-11 from `origin/main` after wave 1.

**Owner decisions:** all decided 2026-10-26. D-17 A (isolated wrapper service), D-18 A (database per service), D-19 agent downloads JRE + jar (re-spec pending).

**Environment notes:** ops refuses `git checkout`/`merge --ff-only` in the main checkout (still on the merged #187 branch; harmless). `gh` GraphQL is blocked: use REST (`gh api`). Board tooling not installed on this host: no board posts.

**Watch:**

- Contract-sync task for architect before wave 2: VS-03's fix pass changed contract details (see VS-03-fix-1 / review-2 "contract-doc lines"); also make VS-08, VS-14, VS-15 explicitly enforce `AgentMessageLimits` (escaped-frame sizes, every agent-sent string bounded: ErrorCode, CorrelationId, Signature, SigningKeyId) — security-2 on VS-03 noted they are documented, not enforced.
- VS-05 security-2 deferrals → VS-06 must include: hub/agent rejection seam case with a certificate from a different CA, and identity bound to the certificate (SPIFFE/CN), never to `X-Node-Id`. Residual: a configurator replacing `HttpMessageHandlerFactory` is not re-checked post-hook — VS-13 should decide.
- VS-04 security-1: YARP logs full destination URLs at Information (query `access_token` leaks to logs); `UrlRewritingDocumentRetriever.RequireHttps` lets `http://` through.

- Seam tests land skipped (`Pending VS-NN`) and must be unskipped by the named packet (D-13).
- VS-13 and VS-14 stack on VS-12; VS-08 and VS-10 wait for VS-06/VS-07 (and VS-09).
- Windows-only behaviour (VS-16, VS-21) is verified only by the owner's runbook run (VS-27).
- PROJECT-STATE changes only in VS-28.

**Files:** [README.md](README.md), [work-packets.md](work-packets.md).
