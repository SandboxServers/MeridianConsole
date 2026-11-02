# Docker run 12 ruling: S-L15 heal wait, Identity refused, Notifications 503

Input: [wave5 Docker run 12 raw record](../../../../.claude/reports/meridian-lab/wave5-docker-run-12.md) (2026-11-02). This ruling was made from code and existing artifacts only. Docker was not run. Every later Docker run needs the user's yes.

## 0. Outcome

| Failure | Class | Root cause | Fix |
| --- | --- | --- | --- |
| L19n S-L15 red: "nodes-postgres listener did not accept connections 10 s after it was enabled" | Test-harness defect from my L19n spec | The test probes `127.0.0.1:20020` from the host. Toxiproxy publishes only 18474, 25000 and 25443, so the heal wait can never pass, and the "still accepts after disable" assertion is vacuous (F128) | **L19o**: pass-2 fix on `ml/l19n`. Probe from inside the lab network. Then redo the D-ML144 red/green record |
| L34 AuthStack: "identity did not answer 200 within 90 s; last status connection refused or reset" | Stack config gap: the L08 code plus a gap in the L34/L36 specs | `LabStack.UpAsync` creates only the `ProxyCatalog` rows tagged L08 (F129). Identity's Postgres (20030), Redis (20031) and RabbitMQ (20032) listeners do not exist, and Identity's Development auto-migrate is unguarded, so the process ends before Kestrel listens | **L08b** on the tip creates every stack proxy. Then **L34b** merges the tip and reruns |
| L36 Notify: "notifications did not answer 200 within 90 s; last status HTTP 503" | Same stack gap | Notifications' Postgres (20050) and RabbitMQ (20051) listeners do not exist. The guarded migrate logs a warning, the app runs, and `/healthz` (all checks) answers 503 on the MassTransit bus check. The timeout message names no check because the lab reads `"entries"` while ServiceDefaults writes `"checks"` (F130) | **L08b**, then **L36b** |

The three failures do not share one cause. L34 and L36 share F129. L19n is independent: S-L15 uses only L08 proxies, which exist. None of the three is environmental. Nodes and the Gateway started in every run (8 to 12) because their proxies are all L08 rows.

BlockedDecision: none.

## 1. Facts checked

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | Toxiproxy publishes only its API and the agent ingress to the host | `LabStack.cs:299-301` (tip): `["127.0.0.1:18474:8474", "127.0.0.1:25000:25000", "127.0.0.1:25443:25443"]`. C10 says "listen addresses are inside the Toxiproxy container" (`work-packets.md:625`) |
| 2 | S-L15 probes the host loopback at the proxy listen port | `ml-l19n/tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs:398-416` `CanConnectAsync` connects to `"127.0.0.1", port`. `ProxyPort("nodes-postgres")` (line 315) returns the catalog `ListenPort` 20020. Heal wait at lines 274-275 |
| 3 | The same probe makes the cut assertion vacuous in S-L15 (lines 266-268) and S-L7 (lines 119-121) | `Assert.False(await CanConnectAsync(...))` is always true, because nothing on the host listens on 20020. S-L7 passed in runs 8-11 because its pg-cut proof is the Nodes 5xx at lines 123-136, not this assertion |
| 4 | The spec told the coder to do this | `worknotes/nodes-ef-retry-packet.md` section 5, step 2.5: "`WaitUntilAsync(CanConnectAsync(...))` (10 s window, 1 s poll). This probes the proxy listener, never Nodes." The intent was right but the probe point was wrong |
| 5 | The red stopped before the measured request | The raw record shows the failure at line 275. The Nodes tail shows only baseline and warm 401s and no Npgsql failure, so the first post-heal enroll was never sent. The red neither passed nor failed on its question |
| 6 | The stack creates only L08 proxies | `LabStack.cs:319` (tip), `:323` (ml-l34), `:322` (ml-l36): `foreach (var proxy in ProxyCatalog.ForPacket("L08"))`. Nothing else calls `CreateProxyAsync` (`git grep` over all three branches). Adapter `Proxies` lists are never used to create anything |
| 7 | The catalog has rows for L34 and L36 that are never created | `ProxyCatalog.cs:22-40`: gateway-identity 20011, gateway-betterauth 20013, identity-* 20030-20032, betterauth-postgres 20040 (L34); gateway-notifications 20014, gateway-discord 20015, notifications-* 20050-20052, discord-* 20060-20061 (L36). ml-l36 adds `discord-rabbitmq` 20062 |
| 8 | The specs never said who creates them | L08 spec `work-packets.md:1551`: "`LabStack` creates the L08 rows". L34 item 6 (`:3649`) and L36 item 4 (`:3719`) list `ProxyCatalog` only as data. This is my gap, not the coders' |
| 9 | Services reach their dependencies only through those rows | `LabConnectionStrings.ServiceDb` uses `Host=meridian-lab-toxiproxy;Port=<ProxyCatalog.Find("<service>-postgres")>`. `IdentityLabAdapter.cs:80-90` (ml-l34) uses Postgres via 20030, Redis via 20031, `RabbitMq__Host=meridian-lab-toxiproxy`, Port 20032. The Identity keys match what Identity reads (`Program.cs:94`, `:123`) |
| 10 | Identity ends on a refused Postgres | `Dhadgar.Identity/Program.cs:801-812`: in Development, `await db.Database.MigrateAsync()` runs before `app.Run` with no try/catch. The lab sets `ASPNETCORE_ENVIRONMENT=Development`. Port 15010 is never bound, which matches "connection refused or reset" for 90 s |
| 11 | Notifications runs but is unhealthy | `Dhadgar.Notifications/Program.cs`: the Development migrate is wrapped in try/catch and logs a warning. MassTransit registers a bus health check. `/healthz` uses `Predicate = _ => true` (`ServiceDefaultsExtensions.cs:492-496`), so an unreachable broker means 503 |
| 12 | The 503 message hides the failing check | `LabServices.cs:494-529` `FailingHealthChecks` reads `"entries"`, while `ServiceDefaultsExtensions.cs:528-552` writes `"checks"`. The suffix is always empty |
| 13 | The service containers outlive a readiness failure, and their logs are captured | `LabServices.cs:161-216`: `--restart no`, the target is registered, `_logs.FollowAsync` runs before `WaitReadyAsync`, and a failed wait does not remove the container. The lines go to `LabProcessSupervisor.ReadLines(<service>)` (redacted ring) and `<home>\logs\<service>.jsonl`. `AuthStackDockerTests` and `NotifyDockerTests` print neither, and they delete `home` in `finally` |
| 14 | No test fixes the created proxy set | `ProxyCatalogTests.cs:30-36` counts `ForPacket("L08")` against the table (data only). `LabStackTests` runs `UpAsync` against `FakeDocker` and `FakeToxiproxyHandler` (`LabStackTests.cs:224-228`, `:290`) and never asserts which proxies were created |

Not verified (logs were deleted with `home`): any second-order failure in Identity, BetterAuth or Notifications once their listeners exist. Section 6 captures those logs on the next run.

## 2. Rulings

### D-ML149: S-L15 and S-L7 probe the proxy listener from inside the lab network (F128)

Toxiproxy's service listeners are reachable only on `meridian-lab-net`. A seam test that checks a proxy listener runs `pg_isready` inside `meridian-lab-postgres` against `meridian-lab-toxiproxy:<port>` through `IDockerCli`, with the same `--context` as every lab call. Exit 0 means the listener accepts and the upstream answers. Any non-zero exit means it does not. Host TCP connects to catalog listen ports are banned in tests, except for 25000 and 25443, which are published. The D-ML144 record (red on scratch, green on the tip) is redone after L19o. The hard stop and the hollow-green guard still apply.

### D-ML150: `stack_up` creates every C10 proxy except L41's (F129)

The `Packet` column in `ProxyCatalog` names the packet that first needs a row, not the packet that creates it. `LabStack.UpAsync` creates and registers `ProxyCatalog.StackProxies`, which is every row except those owned by L41. L41 makes `agent-ingress-tls` live together with the Gateway's 8443 listener, and creation is idempotent (`ToxiproxyClient.CreateProxyAsync` treats 409 as success). A proxy whose upstream container is not running is harmless: Toxiproxy accepts and closes. This is the same failure the service would see without the proxy, but `p:<name>` now exists for faults. Amends the L08 spec (`work-packets.md:1551`).

### D-ML151: a readiness timeout explains itself, and Docker tests print the logs before teardown (F130)

`LabServices.WaitReadyAsync` names failing checks from the ServiceDefaults `"checks"` object (names only, R-LOG). It also appends the container state (`<status> <exit code>`) from `docker container inspect`, accepted only when it matches `^[a-z]+ -?\d+$`, otherwise `unknown`. Every Docker test that starts services writes a redacted log tail for each started service in `finally`, before `StopAsync` or `DownAsync`. Each line goes through `SensitiveText.Redact` on top of the ring's vault redaction (see the F127 candidate, [l19n ruling 1](l19n-ruling-1.md) section 7).

## 3. Packets

Each packet goes to one Sonnet `packet-coder`, then `packet-reviewer`. Branches merge locally into `campaign/meridian-lab` (D-ML131). Never `publish -r` or `restore -r`. Never `git worktree prune`. Lock files: none of these packets adds a package, so `git status` must show no `packages.lock.json` change.

Rules to inline in every packet below (packet-coder skips CLAUDE.md):

1. `Result`/`Result<T>` for validation, IO and Docker failures, never exceptions. `Result<T>.Success(null)` is forbidden.
2. Docker only through `IDockerCli.RunAsync(DockerEngine.Linux, args, ct)` with an argument list. Never a string command line, never `Process` directly.
3. No secrets, connection strings, tokens or free-form service text in `Result` errors or exception messages (R-LOG). Check names and fixed tokens only.
4. `using` on every `IDisposable` (HttpClient, JsonDocument, CancellationTokenSource).
5. No package versions in `.csproj`. Central Package Management only. This packet adds no package.
6. Nullable is enabled and warnings are errors in Release: build with `-c Release` and fix every warning.
7. xUnit conventions of `tests/Dhadgar.Lab.Tests`: `[Fact]`, `Assert.*`, file-scoped namespaces, Docker tests carry `[Trait("RequiresDocker", "true")]`, and the non-Docker filter is `RequiresDocker!=true` (never `Category!=Docker`).
8. Edit only the named files. Do not run Docker. The orchestrator runs it after the user's yes.

### L19o: S-L15 and S-L7 probe the listener in-network (D-ML149, F128)

| Item | Value |
| --- | --- |
| Branch / worktree | `ml/l19n` / `.claude/worktrees/ml-l19n` (pass-2 fix on L19n; head `66b39945`) |
| Size | Small |
| Security review | No (lab test code only) |
| Commit subject | `fix(lab): probe Toxiproxy listeners in-network in S-L15 and S-L7 [L19o]` |

Files and members:

1. New `tests/Dhadgar.Lab.Tests/Seams/ProxyListenerProbe.cs`:

   ```csharp
   namespace Dhadgar.Lab.Tests.Seams;

   /// <summary>
   /// Checks a Toxiproxy listener from inside the lab network (D-ML149). Catalog listen ports are not published to the host,
   /// so a host TCP connect proves nothing. pg_isready runs in the lab Postgres container against the proxy.
   /// </summary>
   internal static class ProxyListenerProbe
   {
       internal static IReadOnlyList<string> Args(string proxyName)
       {
           ProxyDefinition proxy = ProxyCatalog.Find(proxyName)
               ?? throw new InvalidOperationException($"Proxy {proxyName} is not in the C10 catalog.");
           return
           [
               "exec", $"{LabDefaults.ContainerPrefix}postgres",
               "pg_isready", "-h", $"{LabDefaults.ContainerPrefix}toxiproxy", "-p", proxy.ListenPort.ToString(CultureInfo.InvariantCulture),
               "-U", "postgres", "-t", "2",
           ];
       }

       /// <summary>True when pg_isready exits 0 through the proxy. A Docker call failure is a test failure, not "closed".</summary>
       internal static async Task<bool> AcceptsAsync(IDockerCli docker, string proxyName, CancellationToken ct)
       {
           Result<DockerOutput> run = await docker.RunAsync(DockerEngine.Linux, Args(proxyName), ct);
           Assert.True(run.IsSuccess, run.IsFailure ? run.Error : string.Empty);
           return run.Value!.ExitCode == 0;
       }
   }
   ```

   Use the real `LabDefaults` member names. The prefix helper is `LabStack.ContainerName` (`LabStack.cs:219`), which is private. Use `LabDefaults.ContainerPrefix` as it does. Only proxies whose upstream is `meridian-lab-postgres:5432` may be passed. Assert this in `Args` (`proxy.Upstream.StartsWith("meridian-lab-postgres:", StringComparison.Ordinal)`) so nobody points pg_isready at RabbitMQ.

2. `ToxiproxySeamTests.cs`:
   - S-L15, lines 266-268: replace with `Assert.False(await ProxyListenerProbe.AcceptsAsync(rig.Docker, "nodes-postgres", ct), "The nodes-postgres listener still accepts connections after it was disabled.");`
   - Lines 274-275: `bool proxyUp = await WaitUntilAsync(() => ProxyListenerProbe.AcceptsAsync(rig.Docker, "nodes-postgres", ct), 10, 1, ct);` Keep the message. The probe opens its own connection from the Postgres container and never touches Nodes' pool, so the "no Nodes request during the cut" rule still holds.
   - S-L7, lines 119-121: the same replacement. The test has a docker handle where it builds (`rig.Docker`, or the local it passes to `LabBuilder.BuildAsync`). Use that handle.
   - Delete `CanConnectAsync` (lines 398-416) and `ProxyPort` (lines 315-316) if nothing else uses them. A compile error on revert is intended.
3. New `tests/Dhadgar.Lab.Tests/Seams/ProxyListenerProbeTests.cs` (non-Docker):
   - `Args_NodesPostgres_TargetsToxiproxyInsideTheLabNetwork`: equals the exact list, with `-h meridian-lab-toxiproxy -p 20020`, and contains no `127.0.0.1`.
   - `Args_NonPostgresUpstream_Throws` (`nodes-rabbitmq`).
   - Why red on revert: reverting to the host probe deletes `ProxyListenerProbe`, so the tests do not compile. Pointing `-h` at `127.0.0.1` fails the first test. The behavioral proof is the Docker record in section 5.

Checks, in order:

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release`
2. `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"`. Expect exactly one red, `GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster`, until L21g is merged (KG-W5-22). Any other red stops the packet.
3. `git status` shows only the three test files.

Docs owed: none in the library. The architect amends `nodes-ef-retry-packet.md` step 2.5 (section 7).

### L08b: `stack_up` creates every stack proxy; readiness timeouts explain themselves (D-ML150, D-ML151, F129, F130)

| Item | Value |
| --- | --- |
| Branch / worktree | `ml/l08b-stack-proxies` / `.claude/worktrees/ml-l08b`, from the campaign tip `8191eb8f` (or later) |
| Size | Small to medium (about 30k tokens). Do not add more |
| Security review | **Yes, short pass.** Item 4 prints service logs to test output. The advisor checks the redaction path and that no free text reaches `Result` errors |
| Commit subject | `fix(lab): create every C10 stack proxy at stack_up and explain readiness timeouts [L08b]` |

Files and members:

1. `src/Tools/Dhadgar.Lab/Faults/ProxyCatalog.cs`: add the following, and change the class summary from "creates the rows owned by L08" to "creates `StackProxies`".

   ```csharp
   /// <summary>
   /// The proxies <c>stack_up</c> creates: every row except L41's agent-ingress-tls, which L41 makes live with the Gateway's
   /// 8443 listener (D-ML150). <see cref="ProxyDefinition.Packet"/> names who first needs a row, not who creates it.
   /// </summary>
   public static IEnumerable<ProxyDefinition> StackProxies =>
       All.Where(proxy => !string.Equals(proxy.Packet, "L41", StringComparison.Ordinal));
   ```

2. `src/Tools/Dhadgar.Lab/Stack/LabStack.cs:319`: change `ProxyCatalog.ForPacket("L08")` to `ProxyCatalog.StackProxies`. Change nothing else in `UpAsync`. Teardown at `:913` already removes every `Proxy` target.
3. `src/Tools/Dhadgar.Lab/Services/LabServices.cs`:
   - `FailingHealthChecks` (line 494): read `"checks"` instead of `"entries"`, and make it `internal static` (IVT for `Dhadgar.Lab.Tests` already exists, `SecretVault.cs:5`). Keep names-only output.
   - The timeout branch (lines 435-440): before returning, call a new `private async Task<string> ContainerStateAsync(string containerName, CancellationToken ct)` that runs `["container", "inspect", "--format", "{{.State.Status}} {{.State.ExitCode}}", containerName]` with `CancellationToken.None`. The name is the same container the start used (`adapter.Container`). It returns the trimmed stdout only when it matches `^[a-z]+ -?\d+$`, otherwise `unknown` (Docker failure, non-zero exit or any other text).
   - Build the message with a new `internal static string ReadinessTimeoutMessage(string name, string lastStatus, string failingChecks, string containerState)`, which returns `[Lab.Timeout] {name} did not answer 200 within {ReadinessTimeoutSeconds} s; last status {lastStatus}[; failing checks: {failingChecks}]; container {containerState}`. Keep the existing prefix and wording, because the runs 8-12 reports and tests match on "did not answer 200 within".
4. New `tests/Dhadgar.Lab.Tests/Services/ServiceLogTail.cs` (test helper used later by L34b, L36b and others):

   ```csharp
   internal static class ServiceLogTail
   {
       /// <summary>Writes the last <paramref name="count"/> captured lines of each service, redacted twice (vault + SensitiveText).</summary>
       internal static void Write(ITestOutputHelper output, LabProcessSupervisor supervisor, IEnumerable<string> services, int count)
       {
           foreach (string service in services)
           {
               Result<IReadOnlyList<LogLine>> lines = supervisor.ReadLines(service);
               if (lines.IsFailure) { output.WriteLine($"{service}-log unavailable: {lines.Error}"); continue; }
               foreach (LogLine line in lines.Value!.TakeLast(count))
               {
                   string message = SensitiveText.Redact(line.Message, out _);
                   output.WriteLine($"{service}-log {line.Timestamp:O} {line.Level} {line.Category} {message}");
               }
           }
       }
   }
   ```

5. Tests (non-Docker):
   - `LabStackTests.UpAsync_FakeDocker_CreatesAndRegistersEveryStackProxy`: after `UpAsync`, the `registry` out-param holds `p:<name>` for every `ProxyCatalog.StackProxies` row and specifically `p:identity-postgres`, `p:notifications-rabbitmq` and `p:gateway-identity`, and holds no `p:agent-ingress-tls`. **Red on revert:** with `ForPacket("L08")`, `p:identity-postgres` is missing. Read the registry through its public list member as declared in `LabTargetRegistry`.
   - `ProxyCatalogTests.StackProxies_CoverEveryAdapterProxy`: reflect over `typeof(LabStack).Assembly` for non-abstract `ILabServiceAdapter` types with a public parameterless constructor, and assert every name in each adapter's `Proxies` is in `StackProxies`. On the tip it covers Nodes and the Gateway. After L34b and L36b merge, it covers their adapters. This is the guard that would have caught F129 at L34's review.
   - `ProxyCatalogTests.StackProxies_ExcludesOnlyL41`: `All.Count() - StackProxies.Count()` equals the L41 row count.
   - `LabServicesHealthTests.FailingHealthChecks_ServiceDefaultsBody_NamesUnhealthyChecksOnly`. The oracle is a literal in the exact `WriteHealthResponseAsync` shape (`ServiceDefaultsExtensions.cs:521-552`): `{"service":"Dhadgar.Notifications","status":"unhealthy","timestamp":"2026-11-02T00:00:00Z","checks":{"self":{"status":"Healthy","duration_ms":0.1},"masstransit-bus":{"status":"Unhealthy","duration_ms":3.2,"description":"Not ready: connect failed amqp://lab:hunter2@x"}}}`. Expect exactly `masstransit-bus`, and the result contains neither `hunter2` nor `description`. **Red on revert:** `"entries"` yields an empty string.
   - `ReadinessTimeoutMessage_...`: the format with and without checks, and the state sanitizer (`exited 139` kept; `running 0` kept; `Error: no such object` becomes `unknown`).
   - `ServiceLogTailTests.Write_RedactsCredentialPairs`: a fake supervisor is not needed if `LabProcessSupervisor` can be fed lines in tests. Otherwise test the line formatter as an `internal static string Format(string service, LogLine line)` and assert `access_token=abc` becomes `access_token=[redacted]`.

Checks, in order:

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l08b\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release`
2. `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l08b\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"`. Expect only the `GatewayRouteTableTests` red, unless L21g has merged first, in which case expect zero reds.
3. `git status` shows only the named files.

Docs owed: none in the library (the lab README does not describe proxy creation). The architect amends `work-packets.md:1551` and C10 (section 7).

### L34b: L34 takes L08b and prints its logs (pass-2 fix on L34)

| Item | Value |
| --- | --- |
| Branch / worktree | `ml/l34` / `.claude/worktrees/ml-l34` (head `a266b479`) |
| Prerequisite | L08b merged into `campaign/meridian-lab`. L34 ruling 1's rename step R1 is done (`l34-ruling-1.md` section 1.1) |
| Size | Small |
| Security review | No (merge plus a call to the helper L08b's advisor already reviewed) |
| Commit subjects | merge: `Merge campaign/meridian-lab into ml/l34 [L34b]`; fix: `fix(lab): print service log tails in AuthStackDockerTests [L34b]` |

Steps:

1. `git -C <ml-l34> merge campaign/meridian-lab`. Expected conflicts:
   - `LabServices.cs`: keep L34's changes and L08b's `FailingHealthChecks`, `ContainerStateAsync` and `ReadinessTimeoutMessage`.
   - `LabStack.cs`: the hunks do not overlap; L08b changes line 319 and L34 changes lines 37-40, 259-265 and 357+.

   Do not take any branch's `GatewayRouteTable.cs`. That file is L21g's.
2. `tests/Dhadgar.Lab.Tests/Services/AuthStackDockerTests.cs`: in `finally`, before any `StopAsync` or `DownAsync`, call `ServiceLogTail.Write(_output, <supervisor>, ["nodes", "identity", "betterauth", "panel", "gateway"], 80)`, after a 1 s delay for the follower flush (as S-L15 does at line 293). The test needs `ITestOutputHelper` through its constructor if it does not already take one. Use the supervisor instance the test passed to `LabServices`.
3. No product change. If Identity still fails after the proxies exist, the next run's tail says why, and that becomes a separate ruling.

Checks:

1. Build `ml-l34\tests\Dhadgar.Lab.Tests` in Release.
2. Run the non-Docker filter.
3. `StackProxies_CoverEveryAdapterProxy` must pass. It now covers `IdentityLabAdapter` and the BetterAuth adapter, and it is red if any of their proxies is missing from `StackProxies`.

Docs owed: none.

### L36b: L36 takes L08b and prints its logs (pass-2 fix on L36)

| Item | Value |
| --- | --- |
| Branch / worktree | `ml/l36` / `.claude/worktrees/ml-l36` (head `1d55ab48`) |
| Prerequisite | L34b done. Follow the merge order with L34 in [l36 ruling 1](l36-ruling-1.md) section 6. L08b merged |
| Size | Small |
| Security review | No |
| Commit subjects | `Merge campaign/meridian-lab into ml/l36 [L36b]`; `fix(lab): print service log tails in NotifyDockerTests [L36b]` |

Steps:

1. Merge the campaign branch.
   - `ProxyCatalog.cs`: keep L36's `discord-rabbitmq` row and L08b's `StackProxies`. They do not overlap, but check the result.
   - `LabStack.cs`: the `CredentialNames` hunk is L36's only change there. Keep L08b's line 319.
2. `NotifyDockerTests.cs`: in `finally`, call `ServiceLogTail.Write(..., ["sink", "notifications", "discord"], 80)` before teardown, the same way L34b does.
3. Checks: the same as L34b, with this worktree's paths. `StackProxies_CoverEveryAdapterProxy` must cover `NotificationsLabAdapter`, `DiscordLabAdapter` and `SinkLabAdapter`.

Docs owed: none.

## 4. Order, including L21g

1. **In parallel, no Docker:** L21g ([l19n ruling 1](l19n-ruling-1.md) section 5), L08b and L19o.
   - L21g touches only `Invariants/GatewayRouteTable*`, and L08b touches neither file, so they can merge in either order.
   - L19o is on `ml/l19n` and does not need L08b, because S-L15 and S-L7 use only L08 rows.
2. Merge L21g and L08b into `campaign/meridian-lab` (D-ML131). After both are in, the tip's non-Docker suite expects **zero** reds.
3. L34b, then L36b, each merging the updated tip. Each then has zero non-Docker reds, because L21g comes in through the merge.
4. Optionally merge the tip into `ml/l19n` so its suite is also clean. This is not required for the record. If done, recreate the scratch from the new head.
5. **One Docker session with the user's yes** (section 5). Run the steps serially on a clean daemon, in this order: S-L15 red, S-L15 green, S-L7, AuthStack, Notify.
6. Then the review passes:
   - L19n pass 2 (record plus L19o).
   - L34 and L36 per their rulings.
   - Merge L19n, then L34, then L36. Each merge runs the non-Docker suite on the tip.

## 5. Next Docker run procedure (needs the user's yes)

Preconditions (C0 from [l19n ruling 1](l19n-ruling-1.md) section 4):
- `docker --context desktop-linux info` works.
- No `meridian-lab` containers or networks exist.
- Port 8789 is free.
- No leftover `TestResults` folders. Delete `ml-l36\tests\Dhadgar.Lab.Tests\TestResults\53437788-...` and the campaign worktree's untracked `tests/Dhadgar.Lab.Tests/TestResults/`, both of which are untracked output.
- Lock files are clean.

1. **Scratch rebuild (ops).** Bring the scratch to the L19o head (call it `H`).
   - Either: `git -C <ml-l19n-red> cherry-pick <L19o sha>`.
   - Or: remove the scratch worktree with `git worktree remove` (never prune) and recreate it per [l19n ruling 1](l19n-ruling-1.md) section 4.B from `H`.

   Then verify both of these:
   - `git diff H ml/l19n-red-scratch -- . ":(exclude)src/Dhadgar.Nodes"` is empty.
   - `git diff --cached 37560f0d -- src/Dhadgar.Nodes` is empty on the scratch (the restore holds).
2. **S-L15 red** on the scratch, with the command from the raw record and `--results-directory %TEMP%\l19n-records\red`.
   - **Accept only:** the failure text `First enroll after an idle Postgres cut answered 500` and a Nodes tail with an Npgsql read or connection failure.
   - **Hard stop:** a failure at the heal wait (L19o did not fix it) or at the cut assertion (a disabled proxy still accepts). Report failed and do not run the green.
3. **S-L15 green** on `ml/l19n` (`H`), with `--results-directory %TEMP%\l19n-records\green`. Pass means 6 × 401 and an EF retry or connection-failure line in the Nodes tail. A pass without that line counts as a hollow green and is reported as failed (D-ML144).
4. **S-L7** on `ml/l19n`: `--filter "Seam=S-L7"`. The cut assertion must now be real.
5. **AuthStack** on `ml-l34`: the command from the raw record.
   - On failure, the output carries the readiness message with `; container <state>` and the redacted tails of all five services. Record the state, the last 20 lines of the failing service's tail, and the failing checks.
   - If a tail says "unavailable", the in-test tail failed, and `docker logs` cannot help afterwards because teardown removes the container. Do not rerun ad hoc. Report it, and the architect specs a tail fallback that runs `docker logs --tail 200 <container>` through `IDockerCli` inside `finally`.
6. **Notify** on `ml-l36`: the command from the raw record. The readiness message now names the failing checks if `/healthz` still answers 503.
7. After each step:
   - Teardown checks: no containers labelled `meridian-lab=1`, no lab networks, port 8789 free.
   - Copy the TRX files to `%TEMP%\docker-run-13\`.
   - Write the raw record to `.claude/reports/meridian-lab/wave5-docker-run-13.md` in the same shape as run 12.

Where logs are captured:
- During the run: `LabProcessSupervisor.ReadLines(<service>)`, a redacted ring fed by `ContainerLogFollower` from `docker logs -f`.
- On disk: `<test home>\logs\<service>.jsonl`. The test deletes `home` in `finally`, so the in-test tail (L34b, L36b, and the existing `WriteNodesLogTail` in S-L15) is the record.
- The containers stay until teardown (`--restart no`).

## 6. Expected results and what they would mean

| Step | Expected | If not |
| --- | --- | --- |
| S-L15 red | 500 on the first post-heal enroll | 401 on the scratch means the red cannot be taken this way. Write an architect ruling; do not weaken the test |
| S-L15 green | 6 × 401, retry line present | Back to L19n review with the tail |
| AuthStack | Identity 200, then BetterAuth, panel, Gateway, sign-up 200 | The container state and the tail name the next defect, which gets its own ruling. Likely suspects to check first: the Identity exchange key file mount and the BetterAuth Secrets stub (D-ML146) |
| Notify | Notifications 200; Discord 503 with `botStatus` | Failing checks named. If `masstransit-bus` still fails with the proxy present, check `RabbitMq__*` keys against `AddDhadgarMessaging` |

## 7. Ledger edits (orchestrator applies)

Next free IDs: F128 (F127 stays reserved for the access_token candidate in [l19n ruling 1](l19n-ruling-1.md) section 7), D-ML149, KG-W5-23, KG-W7-4. After this ruling: **F131, D-ML152, KG-W5-24, KG-W7-5**.

1. `README.md` findings:
   - **F128:** "S-L15 and S-L7 probe Toxiproxy listeners from the host at `127.0.0.1:<listen port>`. Only 18474, 25000 and 25443 are published, so the S-L15 heal wait can never pass and both cut assertions are vacuous. The spec (`nodes-ef-retry-packet.md` 2.5) prescribed it." Evidence: section 1 rows 1-5. Disposition: D-ML149; L19o.
   - **F129:** "`LabStack.UpAsync` creates only the `ProxyCatalog` rows tagged L08. The L34 and L36 specs list their rows as data only, so Identity, BetterAuth, Notifications and Discord, and the Gateway's routes to them, have no listener. Identity's unguarded Development migrate ends the process (run 12: connection refused); Notifications' bus check answers 503." Evidence: section 1 rows 6-11. Disposition: D-ML150; L08b, L34b, L36b.
   - **F130:** "`LabServices.FailingHealthChecks` reads `entries`, while ServiceDefaults writes `checks`, so a 503 readiness timeout never names its failing check. The Docker service tests print no service log and delete `home`, so a failed start leaves no evidence." Evidence: section 1 rows 12-13. Disposition: D-ML151; L08b, L34b, L36b.
2. `README.md` decisions: add D-ML149, D-ML150 and D-ML151 as written in section 2. Status: Accepted (architect). Basis: this ruling.
3. `README.md` known gaps:
   - **KG-W5-23:** "**Live (F128):** S-L7's Postgres-cut assertion is vacuous, and S-L15 cannot pass its heal wait. Closes with L19o and the run-13 record."
   - **KG-W7-4:** "**Live (F129, F130):** L34's and L36's Docker acceptance tests have never passed; their stack proxies were not created. Closes on run 13 green after L08b, L34b and L36b."
4. `README.md` packets table (spaces around pipes):
   - `| L08b | 5 (follow-up) | stack_up creates every C10 proxy except L41's; readiness timeout names failing checks and container state; ServiceLogTail helper (F129, F130, D-ML150, D-ML151) (ml/l08b-stack-proxies, worktree ml-l08b) | none (no Docker) | yes (short) | Ready ([docker-run-12 ruling](worknotes/docker-run-12-ruling.md) section 3) |`
   - `| L19o | 5 (follow-up) | S-L15 and S-L7 probe the nodes-postgres listener in-network with pg_isready (F128, D-ML149) (ml/l19n, worktree ml-l19n) | S-L15, S-L7 | no | Ready (same ruling) |`
   - `| L34b | 7 (follow-up) | merge tip with L08b; AuthStack prints service log tails (F129, F130) (ml/l34) | AuthStack Docker | no | BlockedDependency (L08b) |`
   - `| L36b | 7 (follow-up) | merge tip with L08b; Notify prints service log tails (F129, F130) (ml/l36) | Notify Docker | no | BlockedDependency (L34b) |`
   - Change the L19n status to `Pass 2: L19o, then run 13 record (D-ML144, D-ML149)`.
5. `work-packets.md`:
   - Line 1551: replace "`LabStack` creates the L08 rows after Toxiproxy is ready" with "`LabStack` creates `ProxyCatalog.StackProxies` (every row except L41's; D-ML150) after Toxiproxy is ready".
   - Under the C10 proxy table (after line 625's sentence): "The listen ports are reachable only on `meridian-lab-net`; tests probe them in-network (D-ML149)."
6. `worknotes/nodes-ef-retry-packet.md` section 5 step 2.5: prefix with "Superseded by D-ML149: the listener is probed in-network with `ProxyListenerProbe` (L19o)."
7. `worknotes/wave5-docker-run.md` "Docker run 12" section: fix the stray line break in "notifications". Add a line: "Ruled: [docker-run-12 ruling](docker-run-12-ruling.md)."
8. `RESUME.md` next actions:
   1. L21g, L08b and L19o in parallel (no Docker).
   2. Merge L21g and L08b.
   3. L34b, then L36b.
   4. Ask the user's yes for run 13 (this ruling, section 5).
   5. Reviews, then merge L19n, L34 and L36.
