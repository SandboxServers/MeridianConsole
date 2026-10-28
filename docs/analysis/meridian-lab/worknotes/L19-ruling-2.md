# L19 ruling 2: S-L7 RabbitMQ leg and the Gateway Host header

- **Packet:** L19, step 5 (`tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs`, uncommitted in worktree `ml-l19`).
- **Trigger:** continuation 4 hit the step 4 stop rule of [ruling L19-1](L19-ruling-1.md). After `nodes-rabbitmq` was disabled, Nodes `/readyz` stayed HTTP 200 for 60 s with `checks.masstransit.status` `"Degraded"`. Baseline, latency and Postgres legs passed.
- **Decisions:** D-ML72 (rabbit leg asserts on the body; Host header accepted; architect). D-ML73 (Nodes readiness under a broker outage, **BlockedDecision**, non-blocking). **Finding:** F68.

## What is actually happening

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | `/readyz` uses `MapHealthChecks` with no `ResultStatusCodes`, so the ASP.NET Core default applies: Healthy → 200, **Degraded → 200**, Unhealthy → 503. | `src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs:508-514` |
| 2 | The writer prints top-level `status` as `"ok"` only for Healthy. Anything else, Degraded included, prints `"unhealthy"`, so the body says `unhealthy` over an HTTP 200. Each entry's `status` is `HealthStatus.ToString()` (`Healthy`, `Degraded`, `Unhealthy`). | `ServiceDefaultsExtensions.cs:519-556` |
| 3 | With the broker unreachable, MassTransit 8.5.12 reports `masstransit` Degraded: the five consumer endpoints say "faulted (Broker unreachable: lab@meridian-lab-toxiproxy:20021/)" and only the temporary bus endpoint is Healthy. | coder report, continuation 4; `Directory.Packages.props:60-62`; `src/Dhadgar.Nodes/Program.cs:92-109` |
| 4 | The Gateway's active check probes Nodes `/healthz`, which uses the same writer and the same status mapping. A Degraded `masstransit` therefore leaves `/healthz` at 200, and the Gateway keeps Nodes in rotation during a broker outage. | `ServiceDefaultsExtensions.cs:492-498`; `src/Dhadgar.Gateway/appsettings.json:291-298` |
| 5 | `LabServices.WaitReadyAsync` accepts any 200, so after `StartAsync` returns, `masstransit` can still be Degraded while the endpoints start. Ruling 1's baseline (`/readyz` 200) does not prove the check is Healthy. Ruling 1's heal condition (`/readyz` 200) also holds during the fault, so a no-op heal would have passed it. | `src/Tools/Dhadgar.Lab/Services/LabServices.cs:399-426`; ruling 1 step 4 |

F68 is a fact about product semantics, not a lab defect. Nodes publishes through the EF outbox (`Program.cs:113-119`), so keeping Nodes in rotation while the broker is down can be the intended behaviour; L23's `nodes-rabbit-down-outbox` row expects enroll 201 during the outage. The owner decides under D-ML73. The test must stay green whichever way that goes.

## Ruling (D-ML72)

The rabbit leg asserts on the **readiness body**, and the HTTP status stays out of the down assertion. The body is the product's own readiness report, read on the direct port (R-OBSERVE). It names the faulted dependency, so this is a stronger proof than a status code, not a weaker one.

1. **Baseline gains a body check.** Before any fault, wait until `/readyz` is fully healthy (definition below). This proves the later Degraded state was caused by the fault and was not left over from startup.
2. **Down condition (rabbit disabled).** All of these must hold on one response:
   - the response arrived (`Status` not null) and `Status is 200 or 503`. Both are accepted: 200 is today's mapping, and 503 is what a D-ML73 change would give. Nodes must not answer anything else;
   - top-level `status` is not `"ok"`;
   - `checks.masstransit.status` is `"Degraded"` or `"Unhealthy"`;
   - the raw JSON of `checks.masstransit` contains `meridian-lab-toxiproxy:20021`, built as `$"{GatewayAdapter.ToxiproxyHost}:{ProxyPort("nodes-rabbitmq")}"`. This ties the degradation to the disabled proxy. It is a substring match on `GetRawText()`, so it does not depend on whether MassTransit puts the text in `description` or `data`. The host prefix keeps a stray `duration_ms` of 20021 from matching.
3. **Heal condition (rabbit enabled).** `Status == 200`, top-level `status == "ok"` and `checks.masstransit.status == "Healthy"`. HTTP 200 alone no longer counts, because it holds during the fault (fact 5).
4. **Windows unchanged:** down within `RabbitDownSeconds = 60`, poll 2 s; heal within `RabbitHealSeconds = 120`, poll 2 s. Baseline wait: 60 s, poll 2 s.
5. **The test never asserts the F68 gap.** Nothing in it requires 200 while the broker is down. D-ML73 can land without a test change. Ruling 1's principle 4 applies.
6. **Host header accepted** (the coder's deviation). `ProbeAsync` sets `request.Headers.Host = LabServices.ProbeHostHeader` (`"localhost"`) only when `uri.Port == LabPorts.Gateway`. Conditions, all already met by the uncommitted code (`ToxiproxySeamTests.cs:326-334`):
   - the request URI stays `http://127.0.0.1:<LabPorts.Gateway>/...`, and `LabHosts.IsLabEndpoint(uri)` is asserted on the URI before sending; the header never picks the destination;
   - the value is the constant `LabServices.ProbeHostHeader`, never a literal and never a production host (`api.meridianconsole.com` and `*.meridianconsole.com` are in the Gateway's `AllowedHosts` but must not be used);
   - plain HTTP to loopback; no TLS settings, certificate callbacks or handler changes;
   - Nodes direct-port probes send no Host override (baseline 401 shows none is needed).

   This is the lab's existing mechanism. `LabServices.WaitReadyAsync` (`LabServices.cs:421`) and `LabHttp` (`Http/LabHttp.cs:131-134`, L09 ruling 2) do the same thing against the Gateway's product `AllowedHosts` (`src/Dhadgar.Gateway/appsettings.json:10`). No product configuration is weakened.

No product file changes. No lab `src/` change. Only the test file changes.

## What the coder changes

**One file:** `tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs` (uncommitted in `ml-l19`). Keep everything else from continuation 4 as it is: the latency and Postgres legs, `ProbeAsync` with the Host header, the reset and the `finally`.

### Timing output

Continuation 4 lost the timings because the test failed before the single `Console.WriteLine`. Replace that line:

```csharp
private readonly ITestOutputHelper _output;

public ToxiproxySeamTests(ITestOutputHelper output) => _output = output;   // xunit v2: using Xunit.Abstractions;
```

- Write each timing with `_output.WriteLine($"S-L7 timing {name}={ms} ms")` as soon as it is measured, before the assertion that follows it. The names are `pg-disable-to-5xx`, `pg-heal-to-401`, `rabbit-baseline-healthy`, `rabbit-disable-to-degraded` and `rabbit-heal-to-healthy`.
- Put the elapsed milliseconds in every wait's assertion message as well.
- Delete the combined `Console.WriteLine`.

### Helpers (replace `MassTransitStatus`)

```csharp
private readonly record struct ReadyBody(string Top, string MassTransit, string MassTransitRaw);

private static ReadyBody ParseReady(string body)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string top = root.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? "null" : "missing";
        if (!root.TryGetProperty("checks", out JsonElement checks) || !checks.TryGetProperty("masstransit", out JsonElement mt))
        {
            return new ReadyBody(top, "missing", string.Empty);
        }

        string status = mt.TryGetProperty("status", out JsonElement ms) ? ms.GetString() ?? "null" : "missing";
        return new ReadyBody(top, status, mt.GetRawText());
    }
    catch (JsonException)
    {
        return new ReadyBody("unparseable", "unparseable", string.Empty);
    }
}

private static bool IsFullyReady((int? Status, string Body) r)
{
    ReadyBody b = ParseReady(r.Body);
    return r.Status == 200 && b.Top == "ok" && b.MassTransit == "Healthy";
}

private static bool IsRabbitDegraded((int? Status, string Body) r, string brokerMarker)
{
    ReadyBody b = ParseReady(r.Body);
    return r.Status is 200 or 503
        && b.Top != "ok"
        && (b.MassTransit is "Degraded" or "Unhealthy")
        && b.MassTransitRaw.Contains(brokerMarker, StringComparison.Ordinal);
}
```

`checks` is written with `OrdinalIgnoreCase` keys, but the property name is `masstransit` exactly (`Program.cs:106`), so `TryGetProperty` is correct.

### Sequence changes

1. **Baseline** (replaces `Assert.Equal(200, (await ReadyzAsync(http, ct)).Status)`): `WaitUntilAsync(async () => IsFullyReady(last = await ReadyzAsync(http, ct)), 60, 2, ct)`, with `last` a `(int? Status, string Body)` local. On failure the message is `"Nodes /readyz not fully healthy 60 s after start (masstransit must be Healthy before the fault). Body: {last.Body}"`. Log `rabbit-baseline-healthy`. The unknown-token 401 probe stays as it is.
2. **Latency and Postgres:** unchanged.
3. **RabbitMQ:**
   - Comment above the leg: `// Degraded maps to HTTP 200 on /readyz (F68, D-ML73); the body is asserted, never the status code.`
   - `string brokerMarker = $"{GatewayAdapter.ToxiproxyHost}:{ProxyPort("nodes-rabbitmq")}";`
   - Disable `nodes-rabbitmq` as now. Wait until `IsRabbitDegraded(lastReady, brokerMarker)`, window 60 s, poll 2 s. Failure message: `"Nodes /readyz did not report masstransit Degraded/Unhealthy naming {brokerMarker} within {RabbitDownSeconds} s of disabling nodes-rabbitmq ({ms} ms). Last HTTP {lastReady.Status}. Body: {lastReady.Body}"`. Log `rabbit-disable-to-degraded`.
   - Delete the old `Status != 200` wait and the separate `MassTransitStatus` assertion.
   - Enable `nodes-rabbitmq` as now. Wait until `IsFullyReady(lastReady)`, window 120 s, poll 2 s. Failure message: `"Nodes /readyz not fully healthy {RabbitHealSeconds} s after nodes-rabbitmq was enabled ({ms} ms). Last HTTP {lastReady.Status}. Body: {lastReady.Body}"`. Log `rabbit-heal-to-healthy`.
4. **Reset:** unchanged.
5. **Class summary:** replace "makes Nodes `/readyz` fail on the MassTransit check and healing restores 200" with "makes Nodes `/readyz` report `masstransit` Degraded or Unhealthy naming the proxy, and healing restores a fully healthy report (D-ML72)".

### Stop rules (do not weaken; stop `blocked` and report instead)

- **Baseline** not fully healthy within 60 s: stop. Report the body sampled every 10 s.
- **Down** condition not met within 60 s: stop. Report the body sampled every 10 s, and name the clause that failed: status, top, masstransit status or marker. Do not drop the marker clause, do not widen the window and do not switch to `/healthz`. If MassTransit words the broker address differently, report the exact text, and the architect rules again.
- **Heal** takes more than 120 s: stop, with the measured time and the last body.
- Report every timing listed above. L23 uses them.

### Why the test fails when reverted

| Mutation | Red at |
| --- | --- |
| `SetEnabledAsync("nodes-rabbitmq", false)` no-op or wrong proxy name | rabbit down: `masstransit` stays Healthy |
| `SetEnabledAsync("nodes-rabbitmq", true)` no-op | rabbit heal: stays Degraded, top-level `unhealthy` (green under ruling 1's 200-only heal) |
| Fault applied to a different proxy that MassTransit also uses | marker clause: the body names another port |
| Startup still Degraded when the leg begins | baseline wait, not a false green at rabbit down |
| D-ML73 changes Degraded to 503 | nothing: 503 is accepted while the broker is down, and heal still needs 200 |

The rows of ruling 1 for the latency, Postgres and reset legs still apply.

### Checks (unchanged, in order)

```powershell
$env:CI = 'true'
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L7|FullyQualifiedName~BlastRadiusDockerTests" --logger "console;verbosity=detailed"   # Docker running
```

`<W>` is `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19`. The detailed logger makes the `ITestOutputHelper` timings show. The commit subject is unchanged: `feat(lab): blast-radius guard, write-ahead fault book and toxiproxy faults (step 5/5)`. Security review: yes (L19 is already flagged).

### Repo rules for this edit

- R-OBSERVE: `/readyz` and the DB probe go to 15040 directly, and the Toxiproxy API to 18474. The latency probe is the only call through the Gateway.
- R-HOST: assert `LabHosts.IsLabEndpoint` or `IsLabInfraEndpoint` on the URI before every send. The Host header is `LabServices.ProbeHostHeader`, set on Gateway-port requests only.
- Contract types come from `Dhadgar.Contracts.Nodes` only. Lab constants (`GatewayAdapter.ToxiproxyHost`, `ProxyCatalog`) are used instead of literals.
- `using` on every `HttpRequestMessage`, `HttpResponseMessage`, `JsonDocument`, `TcpClient` and CTS.
- Nullable enabled, warnings as errors, xUnit v2 `[Fact]` with traits `Seam=S-L7` and `RequiresDocker=true`. `ITestOutputHelper` comes from `Xunit.Abstractions`.
- No package versions in `.csproj`; no new package is needed.

## Ledger changes made with this ruling

- README: F68 added. D-ML72 and D-ML73 added. The D-ML70 row notes that D-ML72 amends its rabbit clause. The S-L7 seam row is reworded.
- `work-packets.md`: the L19 RabbitMQ and baseline bullets are reworded. L23 `nodes-rabbit-down-outbox` gets an F68 note: the Gateway keeps Nodes in rotation while `masstransit` is Degraded, so no 503 is expected today. A 503 there is a finding.

## D-ML73 (BlockedDecision, non-blocking): Nodes readiness during a broker outage

The owner decides what Nodes readiness should say while RabbitMQ is unreachable.

| Option | Effect |
| --- | --- |
| A. Keep Degraded → 200 (today) | Nodes stays in the Gateway rotation, and enroll and heartbeats keep working through the outbox. Kubernetes keeps the pod ready. |
| B. Map Degraded → 503 on `/readyz` (for example `ResultStatusCodes`, or MassTransit `MinimalFailureStatus`) | Readiness fails during a broker outage. The Gateway probes `/healthz`, so it is affected only if `/healthz` changes too. In that case every Nodes route returns 503 during the outage, which defeats the outbox. |

**Recommended: A**, and in the same small ServiceDefaults PR, make the writer print `"degraded"` for Degraded instead of `"unhealthy"`. Today the body contradicts the 200. The test survives either option: it checks `!= "ok"` and accepts 200 or 503. L19 does not depend on the answer.
