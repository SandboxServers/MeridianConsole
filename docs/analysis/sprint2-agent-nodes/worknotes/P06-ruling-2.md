# P06 ruling 2 (after ruling-apply 1 = blocked (spec))

Branch `s2/p06-gateway-agent-ingress`, head `7335f07`. Worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06`. Ruling-1 steps 1-6 are applied there but not committed.

Inputs (all in the P06 worktree, `.claude/reports/sprint2-agent-nodes/`): `P06-ruling-apply-1.md`, `P06-review-1.md`, `P06-security-1.md`; plus `P06-ruling-1.md` (this folder).

## Finding

The coder's diagnosis is correct, and the defect predates this campaign. `main` has ended with `app.MapReverseProxy().RequireRateLimiting("Global")` since `02fc15f`. YARP adds each route's `RateLimiterPolicy` as `EnableRateLimitingAttribute` metadata. Endpoint conventions run after that and add a second `EnableRateLimitingAttribute("Global")`. The rate-limiter middleware reads only the last one. As a result:

- On every proxied route, only `Global` runs: one shared partition (`"global"`), 1000 requests per 60 s for the whole Gateway.
- `Auth`, `PerTenant` and `PerAgent` are configured and never enforced. `RateLimiterConfigurationTests` only checks the configuration values, so no test noticed.
- Ruling-1's IP key is still right. It just has no effect until `PerAgent` actually runs.

## Ruling

**Resolved, scoped to the agent routes. This does not need the owner for P06.** Only routes whose `RateLimiterPolicy` is `PerAgent` change: today that is `agents-route` and `agent-hub-route`. On those routes `PerAgent` replaces `Global`. On every other route `Global` is still attached after the route's own policy, so their behaviour stays exactly as it is on `main`.

| Option | Verdict |
| --- | --- |
| (1) `Global` is added to every proxied route except those whose route policy is `PerAgent` | **Chosen.** It enforces D-S26 on the two routes this campaign owns and changes nothing that users see. It also takes agent traffic out of the shared 1000/min bucket, so a flood of forged certificates can no longer use up the panel's budget, and the panel can no longer starve agent heartbeats. |
| (2) A route's own policy replaces `Global` on every route | Rejected for P06. It would turn on `Auth` (30/min per IP) on identity, betterauth and enroll, and `PerTenant` (100/min per org, shared by all of an org's users) on every tenant route. Those are user-facing 429 changes outside this campaign's scope, and nobody has load-checked them. This goes to the owner as D-S220 (below). |
| (3) Move `Global` to `options.GlobalLimiter`, chained under each route policy | Rejected for P06, for the same reason as (2). It would also limit non-proxy endpoints. It is one of the D-S220 options. |
| Keep `Global` on agent routes too (chain both) | Rejected. ASP.NET allows only one endpoint policy, so chaining needs a `GlobalLimiter`, which is (3). The shared bucket is also the gateway-wide DoS described in D-S220, and agents should not be in it. |

**D-S26, second amendment (cite this ruling):** "On routes whose `RateLimiterPolicy` is `PerAgent`, that policy replaces the gateway-wide `Global` limiter. All other proxied routes keep `Global` as their only enforced limiter (D-S220)."

**D-S220 (new, owner, does not block P06 or this campaign):** whether to enforce the dormant `Auth` and `PerTenant` route limiters, and how. The options are (2) and (3) above, or (A) keep `Global` only. The single `"global"` partition is also a gateway-wide DoS today: one client can use up 1000/min for everyone. Recommendation: P15 files it as a follow-up issue, and the owner rules there. List it in the campaign's closing report, not as a `BlockedDecision` on any packet.

## Implementation steps (packet-coder, continue in the P06 worktree, same branch)

Rules (inlined):
- R-NULL: nullable is on and Release builds with warnings as errors.
- R-XUNIT: follow `Dhadgar.Gateway.Tests` conventions (`[Collection("Gateway Integration")]`, `GatewayWebApplicationFactory`, `Assert.*`).
- R-CPM: no new packages and no `Version=` in `.csproj`.
- R-USING: `using` on every `IDisposable` (certificates, requests, responses, factories).
- Edit only the files named below. Do not touch `docs/analysis/` or `docs/PROJECT-STATE.md`.
- Keep every ruling-1 change already in the worktree. Do not revert or rework them.

1. **New file `src/Dhadgar.Gateway/Services/RouteRateLimiting.cs`:**

   ```csharp
   namespace Dhadgar.Gateway.Services;

   /// <summary>Decides which proxied routes get the gateway-wide <c>Global</c> rate limiter.</summary>
   public static class RouteRateLimiting
   {
       public const string GlobalPolicy = "Global";

       // ASP.NET Core enforces only the last EnableRateLimiting metadata on an endpoint, so attaching Global to a
       // route silently disables the route's own policy. Policies listed here replace Global instead.
       // Only PerAgent for now (P06-ruling-2); Auth and PerTenant stay Global-only until the owner rules (D-S220).
       private static readonly HashSet<string> PoliciesThatReplaceGlobal = new(StringComparer.Ordinal) { "PerAgent" };

       /// <summary>True when a route with this <c>RateLimiterPolicy</c> should get <c>Global</c> attached.</summary>
       public static bool UsesGlobal(string? routePolicy) =>
           routePolicy is null || !PoliciesThatReplaceGlobal.Contains(routePolicy);
   }
   ```

2. **`src/Dhadgar.Gateway/Program.cs`, the end of the file (about :384-386):** replace

   ```csharp
   app.MapReverseProxy()
       .RequireRateLimiting("Global");
   ```

   with

   ```csharp
   // YARP has already added each route's RateLimiterPolicy when this convention runs, and the last
   // EnableRateLimiting wins. Global is attached after the route policy (so it overrides it) except where
   // RouteRateLimiting says the route policy replaces Global (PerAgent; D-S26, D-S220).
   app.MapReverseProxy()
       .ConfigureEndpoints((endpoint, route) =>
       {
           if (RouteRateLimiting.UsesGlobal(route.RateLimiterPolicy))
           {
               endpoint.RequireRateLimiting(RouteRateLimiting.GlobalPolicy);
           }
       });
   ```

   `ConfigureEndpoints(Action<IEndpointConventionBuilder, RouteConfig>)` exists on `ReverseProxyConventionBuilder` in Yarp.ReverseProxy 2.3.0. Add `using Dhadgar.Gateway.Services;` only if it is not already there (ruling-1 step 2 probably added it). Also change the `AddPolicy("Global", ...)` name literal to `RouteRateLimiting.GlobalPolicy`.

3. **`tests/Dhadgar.Gateway.Tests/AgentCertificateForwardingTests.cs`:**
   - Keep `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` exactly as it is. It must now pass.
   - Add `NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy`. It pins option (1)'s promise that nothing else changes:
     - `CreateApp` with extra settings `RateLimiting:Policies:Global:PermitLimit = "2"`, `RateLimiting:Policies:Global:WindowSeconds = "60"`, `RateLimiting:Policies:Auth:PermitLimit = "100"`.
     - Send 3 requests with `GET /api/v1/identity/.well-known/openid-configuration` (`identity-route`, policy `Auth`), no certificate, remote IP `203.0.113.20`. Dispose every request and response.
     - Assert the statuses are `[OK, OK, TooManyRequests]` and `capture.Requests.Count == 2`.
     - Why it fails if the order flips or option (2) creeps in: `Auth` (100) would win, and request 3 would return 200.
   - Add `AgentRoute_NotLimitedByGlobal`. It shows that agents left the shared bucket:
     - Extra settings `RateLimiting:Policies:Global:PermitLimit = "2"` and `RateLimiting:Policies:PerAgent:PermitLimit = "100"`.
     - Send 3 requests with `POST /hubs/agent/negotiate`, each with a fresh `CreateCertificate(AgentCertificateProfile.CaCommonName)`, remote IP `203.0.113.30`.
     - Assert all 3 return 200 and `capture.Requests.Count == 3`. Fails on revert to `.RequireRateLimiting("Global")`: request 3 returns 429.
   - If a `Global` setting does not reach the limiter, the extra-settings path is broken. Report it; do not work around it with environment variables. (The coder's earlier probe showed `Global` does read configuration.)

4. **`tests/Dhadgar.Gateway.Tests/RateLimitingBehaviorTests.cs`:** add the following theory. Add `using Dhadgar.Gateway.Services;` only if it is missing.

   ```csharp
   [Theory]
   [InlineData(null, true)]
   [InlineData("Auth", true)]
   [InlineData("PerTenant", true)]
   [InlineData("PerAgent", false)]
   public void RouteRateLimiting_UsesGlobal_OnlyPerAgentReplacesGlobal(string? routePolicy, bool expected) =>
       Assert.Equal(expected, RouteRateLimiting.UsesGlobal(routePolicy));
   ```

5. **`src/Dhadgar.Gateway/README.md`, the "Rate Limiting > Policies" section (about :540-562):**
   - Under **Global Policy**, replace the line `- Applied to ALL proxied requests via \`MapReverseProxy().RequireRateLimiting("Global")\`` with:
     - `` - Attached to every proxied route except those whose `RateLimiterPolicy` is `PerAgent` (`RouteRateLimiting.UsesGlobal`). ASP.NET Core enforces only the last rate-limit policy on an endpoint, so on those routes `Global` is the only limiter that runs. ``
   - Under **Auth Policy** and under **PerTenant Policy**, replace the first bullet (`- Applied to ...`) with:
     - `` - Configured on its routes but **not enforced**: `Global` overrides it (owner decision D-S220 pending) ``
   - Under **PerAgent Policy**, replace `- Applied to agent API routes` with:
     - `` - Enforced on `agents-route` and `agent-hub-route`, where it replaces `Global` ``
   - Replace `- Default: 500 requests per 60 seconds per agent` with:
     - `- Default: 500 requests per 60 seconds per client IP`

6. **Checks, in order:**

   ```powershell
   dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06\tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj
   ```

   All Gateway tests must pass. Run these revert checks and record them in the report, restoring the code after each:
   1. Put back `.RequireRateLimiting("Global")`. Expect `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` and `AgentRoute_NotLimitedByGlobal` to fail.
   2. Put back the `agent-cert:{hash}` key in `PerAgent`, with the ruling-1 revert check text. Expect `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` to fail.
   3. Make `UsesGlobal` always return `false`. Expect `NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy` and the theory to fail.

7. **Commit** everything locally on `s2/p06-gateway-agent-ingress` in one commit, no push: the ruling-1 changes plus these. Stage these explicit paths:
   - `deploy/compose/docker-compose.services.yml`
   - `src/Dhadgar.Gateway/Program.cs`
   - `src/Dhadgar.Gateway/README.md`
   - `src/Dhadgar.Gateway/Services/RateLimitPartitionKeys.cs`
   - `src/Dhadgar.Gateway/Services/RouteRateLimiting.cs`
   - `tests/Dhadgar.Gateway.Tests/AgentCertificateForwardingTests.cs`
   - `tests/Dhadgar.Gateway.Tests/RateLimitingBehaviorTests.cs`

   Subject: `fix(gateway): enforce PerAgent by client IP on agent routes; loopback-bind dev Nodes port`. In the body, name the pre-existing defect: "`RequireRateLimiting(\"Global\")` overrode every route policy; only PerAgent routes now opt out (D-S220 for the rest)". End with the campaign's two trailer lines.

Security review: yes (Gateway auth path and rate limiting). The pass-2 re-review runs `packet-reviewer` and `security-advisor` on `7335f07..<new head>`.

## Ledger follow-ups (orchestrator, not the coder)

- D-S26 in `docs/analysis/sprint2-agent-nodes/README.md`: append the second amendment above.
- Add D-S220 to the decisions table as "Open (owner), not blocking". Make the "No owner decision is open" line say that D-S220 is open and deferred to a follow-up issue.
- P15 files the D-S220 issue: the `Auth`/`PerTenant` limiters are not enforced, and the single `"global"` partition is a gateway-wide DoS. Options (A), (2) and (3) go in the issue, with a note that the `PerTenant` 100/min per org is untested against real panel traffic.
- P15's `PROJECT-STATE.md` text must not claim that the Gateway "rate-limits auth per IP" or "rate-limits per tenant" until D-S220 lands.
- Later Gateway-path seam tests (D-S216) run under `PerAgent` with remote IP `null`, which maps to the `agent-ip:unknown` bucket (500/min). That is enough headroom; if a seam test does many hub calls, raise `RateLimiting:Policies:PerAgent:PermitLimit` in its factory.
