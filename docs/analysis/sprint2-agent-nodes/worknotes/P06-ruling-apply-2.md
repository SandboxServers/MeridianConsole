# P06 ruling apply, pass 2

Status: done. Ruling-2 steps 1-5 applied on top of ruling-1 changes; one local commit.

## Applied
- New Services/RouteRateLimiting.cs; Program.cs uses ConfigureEndpoints so Global is skipped on PerAgent routes; Global policy name uses the constant.
- Tests: NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy, AgentRoute_NotLimitedByGlobal, UsesGlobal theory. PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp unchanged and now passes.
- README rate-limit section updated per step 5.
- Review/security notes: all covered by ruling 1 and 2 (no extras).

## Checks
- Release build with TreatWarningsAsErrors: 0 warnings, 0 errors.
- Gateway tests: 144 passed, 0 failed.

## Revert checks (code restored after each)
1. Global attached to all routes: PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp and AgentRoute_NotLimitedByGlobal fail (2 failures).
2. agent-cert:{hash} key restored: PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp fails (1 failure).
3. UsesGlobal always false: NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy and 3 theory rows fail (4 failures).
