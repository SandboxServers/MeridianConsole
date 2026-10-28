# L19 ruling 1: S-L7 readiness leg

- **Packet:** L19, step 5 (`tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs`), worktree `ml-l19`, branch tip `df63ad9e`.
- **Trigger:** the coder stopped `blocked spec`. After `nodes-postgres` was disabled, Nodes `/readyz` stayed 200 for 30 s.
- **Decisions:** D-ML70 (S-L7 re-spec, accepted by the architect); D-ML71 (Nodes readiness product fix, **BlockedDecision**, non-blocking). **Finding:** F67.

## What is actually wrong

The coder guessed that pooled Npgsql connections survive the disable. They don't need to: **Nodes `/readyz` has no Postgres check at all.**

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | Nodes calls the no-argument overload `builder.AddDhadgarServiceDefaults()`. | `src/Dhadgar.Nodes/Program.cs:24` |
| 2 | That overload adds only `self`, tagged `live`. | `src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs:58-76` |
| 3 | The `postgres` check (`AddNpgSql`, tag `ready`) is added only by the `HealthCheckDependencies` overload, and no service in `src/` calls that overload. | `ServiceDefaultsExtensions.cs:159-181`; a grep for `AddDhadgarServiceDefaults(HealthCheckDependencies` matches nothing |
| 4 | The comment saying Postgres is "already registered by AddDhadgarServiceDefaults with HealthCheckDependencies.Postgres" is false. | `Program.cs:48-49` |
| 5 | The only `ready` check on Nodes is MassTransit's (`masstransit`, tags `ready` and `messaging`). | `Program.cs:104-109` |
| 6 | `/readyz` reports `ready` checks only. The body is `{service, status, timestamp, checks: {<name>: {status, ...}}}`. | `ServiceDefaultsExtensions.cs:508-556` |
| 7 | The Gateway's active health check on cluster `nodes` probes Nodes `/healthz` (all checks: `self` plus `masstransit`) every 30 s with `ConsecutiveFailures`. | `src/Dhadgar.Gateway/appsettings.json:291-298` |
| 8 | When Postgres is unreachable, enroll with a well-formed body and an unknown token reaches `EnrollmentTokenService.ValidateTokenAsync`, whose `FirstOrDefaultAsync` throws. `GlobalExceptionHandler` maps that exception to 500. The audit write swallows its own DB error, so the `{}` body still gets 400 `invalid_platform`. With Postgres up, the same well-formed request gets 401 `invalid_token`. | `Services/EnrollmentService.cs:55-96`; `Services/EnrollmentTokenService.cs:100-137`; `Audit/AuditService.cs:69-76`; `ServiceDefaults/Errors/GlobalExceptionHandler.cs:101-117`; `Endpoints/AgentEndpoints.cs:65-76` |
| 9 | Nodes maps enroll at `/agents/enroll`; the Gateway strips `/api/v1`. Nodes has no rate limiter, so F54's limits apply only to Gateway calls. | `AgentEndpoints.cs:20-30`; `Gateway/appsettings.json:187-196`; `Program.cs` has no `RateLimit` |

None of the lab-side options in the brief can make "`nodes-postgres` down makes Nodes `/readyz` fail" true. `Pooling=false`, a short idle lifetime, a `reset_peer` toxic and a restart all leave readiness without a Postgres check. A restart with Postgres down would fail at startup (Development auto-migrates), which tests startup, not readiness. Fixing the product is outside this campaign's brief (no edits under `src/Dhadgar.*` outside `src/Tools`).

## Ruling (D-ML70)

S-L7 keeps its meaning: a proxy fault set by the lab's `ToxiproxyClient` on a real Toxiproxy reaches a real service, and healing it restores the service. Each leg asserts on a path that really depends on the faulted proxy:

1. **Readiness leg moves to `nodes-rabbitmq`.** MassTransit's `ready` check is the dependency Nodes readiness really has.
2. **The `nodes-postgres` leg asserts on the DB path,** not on readiness: a direct-port enroll with a well-formed body and an unknown token fails with a 5xx while the proxy is down and returns 401 after the heal.
3. **The latency and reset legs stay.** The latency toxic now stays in place until the reset, so the reset is shown to remove a toxic that is really there.
4. The test never asserts the F67 gap (that `/readyz` stays 200 with Postgres down). Such an assertion would turn red on the fix. D-ML71 adds the readiness assertion to the Postgres leg when Nodes gains the check.
5. Proxy operations use `ToxiproxyClient` directly, as the coder did. This is accepted: the S-L7 seam row names `ToxiproxyClient`, and `FaultBookTests` and `BlastRadiusTests` cover the fault tools. The L19 block now says so.

No product file changes. No lab `src/` file changes. Only the test file changes.

## What the coder changes

**One file:** `tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs` (still uncommitted in `ml-l19`). Keep its setup, `finally`, `ProbeAsync`, `CanConnectAsync`, `WaitUntilAsync` shape and `AssertNoToxicsAsync`. Change the following.

### Constants and helpers

```csharp
private const string NodesEnrollPath = "/agents/enroll";          // Nodes direct (Gateway strips /api/v1)
private const int PgWindowSeconds = 30, PgPollSeconds = 5;        // at most 7 direct enroll calls per wait
private const int RabbitDownSeconds = 60, RabbitHealSeconds = 120; // heartbeat fallback and MassTransit reconnect backoff

private static Uri NodesEnrollUri() => new($"http://127.0.0.1:{LabPorts.Nodes}{NodesEnrollPath}");

// Product contract DTO, never a local copy (D-ML7). A fresh random token is never minted, so the DB lookup misses it.
private static string UnknownTokenEnrollBody() => JsonSerializer.Serialize(
    new Dhadgar.Contracts.Nodes.EnrollNodeRequest(
        Token: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        Platform: "linux",
        Hardware: new Dhadgar.Contracts.Nodes.HardwareInventoryRequest("lab-s-l7-probe", null, 1, 1, 1, null),
        PublicKey: "AAAA"),
    JsonSerializerOptions.Web);
```

- Make `WaitUntilAsync` take `(Func<Task<bool>> condition, int windowSeconds, int pollSeconds, CancellationToken ct)`.
- Add `ReadyzAsync(http, ct)`, which returns `(int? Status, string Body)`; the body is needed for the readiness assertion message.
- Add `ProxyHasToxicAsync(http, proxy, toxicName, ct)`, a `GET /proxies` on the infra endpoint that reports whether the toxic is listed. It uses the same `LabHosts.IsLabInfraEndpoint` assertion as `AssertNoToxicsAsync`.
- Add `using System.Security.Cryptography;`.

### Sequence (replaces the body of the `try` after the services start)

1. **Baseline.** `/readyz` returns 200. `ProbeAsync(NodesEnrollUri(), POST, UnknownTokenEnrollBody())` returns 401. This proves the probe reaches the DB lookup; a 400 here means the body is malformed, so fix the body and never loosen the assertion.
2. **Latency (Gateway hop).**
   - Add the `l19-latency` toxic as now. `POST /api/v1/agents/enroll` `{}` through the Gateway (`EnrollUri()`) must return 400, with `ElapsedMs >= 1000`.
   - **Do not remove the toxic.** It stays until step 5.
   - Steps 3 and 4 use only the Nodes direct port (R-OBSERVE), so the toxic does not affect them. The Gateway's active check (10 s timeout) tolerates the 1 s.
3. **Postgres (DB path).**
   - `SetEnabledAsync("nodes-postgres", false)`. Keep the `CanConnectAsync(20020)` is-false assertion.
   - Within `PgWindowSeconds`, polling every `PgPollSeconds`: the unknown-token enroll on the direct port returns `Status is >= 500 and < 600`. Assertion message: `"Nodes enroll still {status} {PgWindowSeconds} s after nodes-postgres was disabled; the DB path did not see the proxy fault."`
   - `SetEnabledAsync("nodes-postgres", true)`. Within the same window, the unknown-token enroll returns 401.
   - Comment above this leg: `// Nodes /readyz has no Postgres check (F67, D-ML71); the DB path is asserted instead. Add the readyz assertion here when D-ML71 lands.`
4. **RabbitMQ (readiness).**
   - `SetEnabledAsync("nodes-rabbitmq", false)`.
   - Within `RabbitDownSeconds`, polling every 1 s: `/readyz` is not 200. Then parse the last body: `checks.masstransit.status` must not be `"Healthy"`. That proves the failure came from the faulted dependency. On failure, put the body in the assertion message.
   - `SetEnabledAsync("nodes-rabbitmq", true)`. Within `RabbitHealSeconds`, polling every 2 s: `/readyz` is 200.
   - This leg runs last among the faults. Under it, Nodes `/healthz` also fails, so the Gateway may mark Nodes unhealthy for up to one 30 s interval after the heal. Nothing after this leg goes through the Gateway.
5. **Reset.**
   - Before the reset, assert `ProxyHasToxicAsync("gateway-nodes", "l19-latency")` is true. Without it the reset assertion would hold even if `ResetAsync` did nothing.
   - Call `ResetAsync`, then `AssertNoToxicsAsync` as now: no toxics, every proxy enabled.

### Stop rules (do not weaken; stop `blocked` and report instead)

- Step 3: the DB probe stays 401 for 30 s after the disable. Then, and only then, add one retry variant and report that you needed it: before the disable, add a toxic `l19-pg-reset` of type `reset_peer`, `stream = "upstream"`, `timeout = 0`, toxicity 1.0, on `nodes-postgres`. Remove it with `RemoveToxicAsync` after the heal. If the probe is still 401, stop `blocked` with the observed statuses.
- Step 4: `/readyz` stays 200 for 60 s. Stop `blocked`, with the `/readyz` body sampled every 5 s in the report; MassTransit may report `Degraded`, which maps to 200. Do not lengthen the window and do not switch to `/healthz`. The architect rules again.
- Step 4 heal takes more than 120 s: stop `blocked` with the measured time.
- Report the measured times for every wait: disable to 5xx, heal to 401, rabbit disable to not-200, rabbit heal to 200. L23 uses them.

### Why the test fails when reverted

| Mutation | Red at |
| --- | --- |
| `SetEnabledAsync` sends the wrong body or proxy name | step 3 (still 401) or step 4 (still 200) |
| `SetEnabledAsync(..., true)` no-op | step 3 heal (still 5xx) or step 4 heal (not 200) |
| `AddToxicAsync` drops `attributes` or the stream | step 2 (`ElapsedMs < 1000`) |
| `ResetAsync` no-op | step 5 (`l19-latency` still listed) |
| Probe body malformed (never reaches DB) | step 1 (400, not 401) |

### Checks (unchanged, in order)

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L7|FullyQualifiedName~BlastRadiusDockerTests"   # Docker running
```

Set `CI=true` for the builds, as in continuation 2. Commit subject: `feat(lab): blast-radius guard, write-ahead fault book and toxiproxy faults (step 5/5)`. Security review: yes (L19 is already flagged).

### Repo rules for this edit

- Contract types come from `Dhadgar.Contracts.Nodes` only; no local DTO copy.
- Every probe asserts `LabHosts.IsLabEndpoint` (service ports) or `LabHosts.IsLabInfraEndpoint` (Toxiproxy API) before sending (R-HOST).
- Observation is on the direct ports only (R-OBSERVE, D-ML34): `/readyz` and the DB probe on 15040, the Toxiproxy API on 18474. The only call through the Gateway is the latency probe.
- `using` on every `HttpRequestMessage`, `HttpResponseMessage`, `JsonDocument`, `TcpClient` and CTS.
- Nullable enabled, warnings as errors; xUnit `[Fact]` with traits `Seam=S-L7` and `RequiresDocker=true`, as now.
- No package versions in `.csproj`. None is needed: `System.Security.Cryptography` is in the framework.

## Ledger changes made with this ruling

- README: F67 added; D-ML70 and D-ML71 added; S-L7 seam row reworded.
- `work-packets.md` L19 test bullet for `Seams/ToxiproxySeamTests.cs` reworded to the sequence above.
- `work-packets.md` L23 rows `nodes-pg-down-enroll` and `nodes-rabbit-down-outbox` gain F67 notes (L23 is not yet dispatched):
  - `nodes-pg-down-enroll`: the designed 503 has no product path today. Nodes answers 500, which `FaultImpactMap` never excuses (D-ML46). Per D-ML23 the expectation stays and the row carries a known issue until D-ML71.
  - `nodes-rabbit-down-outbox`: the Gateway's `/healthz` active check can turn the enroll into a 503. The L23 spec uses S-L7's measured times.

## D-ML71 (BlockedDecision, non-blocking): Nodes readiness covers Postgres

The owner decides whether, and where, Nodes (and every other service whose `Program.cs` relies on the same false assumption) switches to `AddDhadgarServiceDefaults(HealthCheckDependencies.Postgres)`.

Recommended: **yes, as a small product PR outside this campaign**, owned by the vertical-slice or product queue the way VS-07R owns the CA route (D-ML63). The PR also deletes the false comment at `Program.cs:48-49`.

Two behaviour changes follow:

- `/readyz` and the Gateway's `/healthz` probe start failing during a Postgres outage, so Gateway routes to Nodes return 503 instead of 500.
- L23's designed 503 then comes true.

When it lands, the S-L7 Postgres leg gains "`/readyz` not 200 within 30 s; 200 within 30 s after heal", and the `nodes-pg-down-enroll` known issue closes.

L19 is not blocked: D-ML70 does not depend on the answer.
