# P06 ruling apply, pass 1

Status: blocked, nothing committed. Changes are in the worktree, uncommitted.

## Blocker

`PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` fails with the new IP key (statuses OK, OK, OK, OK), and would also fail with the old key. Cause: `Program.cs` ends with `app.MapReverseProxy().RequireRateLimiting("Global")`. That adds an `EnableRateLimiting("Global")` attribute after YARP's per-route `RateLimiterPolicy` metadata, and the rate limiter middleware uses the last attribute. So `PerAgent`, `Auth` and `PerTenant` never run on proxied routes at HEAD; only `Global` does.
Proved: with env `RateLimiting__Policies__Global__PermitLimit=3` the test passes (4th request 429). `PerAgent:PermitLimit=1` (via env, UseSetting and in-memory) had no effect.

The ruling's premise (a working PerAgent limiter) does not hold, and the revert check cannot be run. Fixing it (for example attaching the Global policy only to routes without a RateLimiterPolicy, or via a GlobalLimiter) changes limiting on every route and is outside the named steps. Needs a decision. The test file keeps the test as the ruling specified it.

## Applied (uncommitted)

- Step 1: `src/Dhadgar.Gateway/Services/RateLimitPartitionKeys.cs` added.
- Step 2: Program.cs `Auth` and `PerAgent` use it (`agent-ip:{ip}`); both unused usings removed, Release build with warnings as errors passes.
- Step 3: RateLimitingBehaviorTests uses the real code, plus the link-local fact. The existing theory row `fe80::1` expected `fe80::`, which was the old copy's behavior; changed to `unknown-linklocal` (the real, moved logic).
- Step 4: CreateApp `extraSettings` (applied via in-memory and `UseSetting`), SendAsync `remoteIp`, both new tests. The spoofed-header-with-real-certificate test passes.
- Step 5: README row, line 456 and PerAgent partition-key line replaced.
- Step 6: Nodes port bound to 127.0.0.1; comment replaced on the nodes service only.
- Ledger: D-S26 amended in the main checkout's `docs/analysis/sprint2-agent-nodes/README.md`.
- Security fix notes: all covered by the ruling; none extra.

## Checks

- Release build: pass.
- Gateway tests: 137 passed, 1 failed (the blocker above).
- Revert check: not run (limiter not applied on routes).
