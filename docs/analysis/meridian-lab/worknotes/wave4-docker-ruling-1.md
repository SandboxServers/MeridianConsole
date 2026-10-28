# Wave 4 Docker-run ruling, pass 1 (L22, L49, L51)

Input: `.claude/reports/meridian-lab/wave4-docker-run.md` (branch `campaign/meridian-lab`, `df22a313`, Release). 9 tests ran: 6 passed and 3 failed. Line numbers below are at `df22a313`. Every item also names the member, so a shifted line is still findable. The architect ran no Docker; each cause below was settled by reading the code.

Verdicts: **L22 fix** (fault-smoke spec), **L51 fix** (Toxiproxy timeout test), **L49/L51 fix** (S-L11).

## Findings

| ID | Finding | Evidence |
| --- | --- | --- |
| F78 | `fault-smoke.toml` asserts something the product does not do. Its `readyz-down` clause expects Nodes `/readyz` to be non-200 while `nodes-postgres` is cut. Nodes has no Postgres readiness check (F67, D-ML71 still open), so `/readyz` stays 200 and the clause can never hold. The brief suspected the D-ML70 pooling cause; that is not it. D-ML70 already moved S-L7 off readiness for this same reason. `readyz-recovered` proves nothing either: it holds throughout, so a no-op heal also passes. The row is also missing two things the DB path needs. First, a minted token, so the enroll reaches the token lookup. Second, an I1 allowance. A DB-down enroll is an exception that `GlobalExceptionHandler` logs at Error as "Unhandled exception" and answers with 500. I1 never excuses that, and the section has `allow_invariants = []`. | `fault-smoke.toml:3-4,39-62`; `src/Dhadgar.Nodes/Program.cs:24,48-49,103-109` (F67); `ServiceDefaults/Errors/GlobalExceptionHandler.cs:17,58-63,116`; `Invariants/I1UnhandledInvariant.cs:69-77,91-97`; S-L7 timings `pg-disable-to-5xx=2084 ms`, `pg-heal-to-401=25 ms` |
| F79 | `ToxiproxyTimeoutToxicDockerTests` never creates the stack it probes. It connects to `127.0.0.1:25000` (`agent-ingress`) on the assumption that an earlier test left the stack up. Every other Docker test tears its stack down, so it gets connection refused. Two more latent defects: it has no `LabDockerGate` collection, so it can run beside a test that wipes the stack, and its request sends `Host: 127.0.0.1`. The Gateway's `AllowedHosts` (`localhost;api.meridianconsole.com;*.meridianconsole.com`) refuse that host with 400, so the first `200` assertion would fail even with a running stack. | `Faults/ToxiproxyTimeoutToxicDockerTests.cs:13-31`; `src/Dhadgar.Gateway/appsettings.json:10`; `Seams/ToxiproxySeamTests.cs:361-362` (same 400, already worked around); `Http/LabHttp.cs:130-134` |
| F80 | S-L11 runs `history_run` without starting Nodes or the Gateway. The seam only runs `lab_build` and `stack_up` (`EnrollHistorySeamTests.cs:62-72`). Nodes creates its schema when it starts (`AutoMigrateDatabaseAsync`, `Nodes/Program.cs:167`), so `enrollment_tokens` does not exist yet. `stack_reset_data` succeeds because it only truncates what exists. `EnrollmentTokenService.CreateTokenAsync` then throws, and `NodesLabAdapter.MintTokenAsync` turns that into `[Lab.Failed] nodes token mint failed: <ExceptionType>`. The real message was lost in transit: `DispatchedHistoryEnvironment.CallAsync` keeps only `error.code` and drops `error.message`. NodesSeam (S-L2) mints successfully after `StartAsync("nodes")`, which confirms the cause. The barrier upstream is `agent-ingress` → Gateway, so with no Gateway running the clients would have failed next. | `Seams/EnrollHistorySeamTests.cs:62-80`; `Tools/History/HistoryRunTool.cs:72-86,213-236`; `Nodes/NodesLabAdapter.cs:85-88`; `History/HistoryRunJob.cs:312,318-328,361`; `Tools/Stack/StackTools.cs:80`; `Seams/NodesSeamDockerTests.cs` (passes) |
| F81 | Once F80 is fixed, the barrier proxy would forward every history enroll with `Host: 127.0.0.1:25000`. `StrippedHeaders` removes the client's `Host`, and `HttpClient` sets it again from the destination URI. The Gateway refuses that IP-literal host with 400, so every first enroll would end in a definite failure, and S-L11 would test the host filter instead of the enrollment race. `LabHttp` and `LabServices.WaitReadyAsync` already send `LabServices.ProbeHostHeader` to the Gateway ports. The barrier does not. | `Barrier/BarrierProxy.cs:33-36,394-428`; `Http/LabHttp.cs:130-134`; `Services/LabServices.cs:30-33,414`; `Gateway/appsettings.json:10` |

## Decisions

| ID | Decision | Status | Refs |
| --- | --- | --- | --- |
| D-ML83 | The fault smoke checks the Postgres cut on the Nodes DB path, the way D-ML70 does for S-L7, not through readiness. The row mints a token and cuts `nodes-postgres`. It then requires a 5xx (`gte 500`, `lt 600`) from a direct-port enroll during the cut and 201 from the same token after the heal. `readyz-recovered` stays as the post-heal anchor. The row sets `allow_invariants = ["I1"]`, because the cut enroll is a 500 that `GlobalExceptionHandler` logs as "Unhandled exception" (F67, D-ML71). The spec makes no readiness claim. A comment records that a readyz-down clause returns, and I1 is narrowed, when D-ML71 lands. A spec-validate unit test pins this shape (no `/readyz` `ne 200` clause; I1 allowed only on this row). | Accepted (architect) | F78; F67; D-ML70; D-ML71; L22 file 6 amended |
| D-ML84 | Every `RequiresDocker` test that needs the lab stack creates its own stack in the test and joins `[Collection(LabDockerGate.Name)]`. It uses `LabStack.UpAsync(wipe: true)` plus `LabServices.StartAsync`, as S-L7 does, and its `finally` calls `StopAsync` for each started service, then `DownAsync(wipe: true)`. `DownAsync` removes only the containers in the lab's registry, so cleanup stays label and registry scoped and never touches the dev stack. Requests to the Gateway's ports (15000, 25000) send `Host: LabServices.ProbeHostHeader` (R-HOST). | Accepted (architect) | F79; L51 item 9 amended |
| D-ML85 | S-L11 starts `nodes`, then `gateway`, with `svc_start` under its lease before calling `history_run`. In `DispatchedHistoryEnvironment`, the nested tool's error message is kept: `[<code>] <tool> failed: <message>`, capped at 512 chars. The payload is already redacted by the dispatcher. The barrier proxy sets `Host` to `LabServices.ProbeHostHeader` on every forwarded request. Its upstream is always loopback and a lab port, so this name is equivalent to the address it replaces. | Accepted (architect) | F80; F81; L49 item 5 amended |

## Rulings on each failure

| Failure | Ruling | Fix items |
| --- | --- | --- |
| (1) `FaultSmokeE2ETests`, `readyz-down` not held | Re-spec (D-ML83). The claim that was really under test is that the fault reaches Nodes and heals. The new clauses still state that, and a mutant can break it: a no-op disable yields 201 at `enroll-down`, and a no-op heal leaves `enroll-after` at 5xx. | 1-3 |
| (2) `ToxiproxyTimeoutToxicDockerTests`, connection refused | Fix (D-ML84): own stack, collection, probe host | 4 |
| (3) `EnrollHistorySeamTests`, `nodes_token_mint failed` | Fix (D-ML85). The cause was found by reading, so no diagnostic step is needed. Item 5 still makes the next failure self-describing. F81 is the next failure this seam would hit, so it is fixed in the same pass. | 5-9 |

## Rules to inline (packet-coder skips CLAUDE.md)

- `Result<T>` for every validation, IO, network or Docker failure; never throw for them. `Result<T>.Success(null)` throws, so never write it.
- Docker calls are separate `ArgumentList` items through `IDockerCli`; never build a command string. Test helpers that call `docker` already use `ArgumentList`; keep that.
- Trusted host before every send: assert `LabHosts.IsLabEndpoint(uri)` (service ports) before opening a socket or sending a request. The `Host` header value is always the constant `LabServices.ProbeHostHeader`, never a literal and never a production host.
- `using` on every `IDisposable` (`TcpClient`, `ToxiproxyClient`, `SecretVault`, `LabStack`, `LabProcessSupervisor`, `HttpClient`) and `Process`.
- No package versions in `.csproj` (Central Package Management). This fix adds no package and no project reference.
- Nullable is on and warnings are errors: build with `-p:TreatWarningsAsErrors=true`.
- xUnit conventions of `tests/Dhadgar.Lab.Tests`: `[Fact]`; Docker tests carry `[Trait("RequiresDocker", "true")]`; tests that start a daemon join `[Collection("LabHome")]`; tests that bring up the stack directly join `[Collection(LabDockerGate.Name)]`.
- Never log, print or assert on a token or a vault value. Errors carry codes and redacted messages only.
- Edit only the files named below. Do not edit `docs/analysis/**`; the architect has already made the contract edits.

## Fix list

### 1. Re-spec the fault smoke row (D-ML83)

File: `tests/Dhadgar.Lab.Tests/Uat/Specs/fault-smoke.toml` (whole file, 63 lines). Replace the whole file with:

```toml
schema = 1

# Fault smoke (L22, D-ML83). One row: Postgres for Nodes is cut after a token is minted. An enroll on the Nodes direct port
# fails with a 5xx while it is cut, and the same token enrolls with 201 after the heal; Nodes /readyz is 200 after the heal.
# No readiness claim during the cut: Nodes /readyz has no Postgres check (F67, D-ML71). When D-ML71 lands, add a readyz-down
# clause and narrow allow_invariants.
# I1 is allowed on this row only: the cut enroll is a 500 that GlobalExceptionHandler logs as "Unhandled exception" (F67).
# Enroll-route calls: 2, both on the Nodes direct port, which has no rate limiter (F54).

[section]
id = "fault-smoke"
title = "Fault smoke"
sequential = true
requires = ["docker"]
allow_invariants = []

[[row]]
id = "pg-down"
title = "Cutting Nodes' Postgres fails the Nodes DB path, and the heal restores it"
expected = "A direct enroll returns a 5xx while Postgres is cut; after the heal the same token returns 201 and /readyz returns 200"
allow_invariants = ["I1"]

  [[row.fault]]
  id = "pg"
  inject = { tool = "fault_proxy_disable", args = { proxy = "nodes-postgres" } }
  start = "after_step:mint"
  heal = "after_step:enroll-down"

  [[row.step]]
  id = "up"
  tool = "stack_up"
  args = { wipe = true }

  [[row.step]]
  id = "start"
  tool = "svc_start"
  args = { service = "nodes" }

  [[row.step]]
  id = "mint"
  tool = "nodes_token_mint"
  args = { label = "fault-smoke-${run_id}" }
  capture = { token_ref = "/token_ref" }

  [[row.step]]
  id = "settle-down"
  wait_ms = 5000

  [[row.step]]
  id = "enroll-down"
  tool = "nodes_enroll_probe"
  args = { token_ref = "${token_ref}", platform = "linux", via = "nodes" }

  [[row.step]]
  id = "settle-up"
  wait_ms = 10000

  [[row.step]]
  id = "enroll-after"
  tool = "nodes_enroll_probe"
  args = { token_ref = "${token_ref}", platform = "linux", via = "nodes" }

  [[row.expect]]
  id = "db-path-down-5xx"
  text = "the direct enroll while Postgres is cut returns a status of at least 500"
  source = "step"
  step = "enroll-down"
  pointer = "/result/status"
  op = "gte"
  value = 500
  required = true

  [[row.expect]]
  id = "db-path-down-below-600"
  text = "the direct enroll while Postgres is cut returns a status below 600"
  source = "step"
  step = "enroll-down"
  pointer = "/result/status"
  op = "lt"
  value = 600
  required = true

  [[row.expect]]
  id = "db-path-recovered-201"
  text = "the same token enrolls with 201 on the direct port after the heal"
  source = "step"
  step = "enroll-after"
  pointer = "/result/status"
  op = "eq"
  value = 201
  required = true

  [[row.expect]]
  id = "readyz-recovered"
  text = "Nodes GET /readyz returns 200 after the heal"
  source = "http"
  via = "nodes"
  method = "GET"
  path = "/readyz"
  op = "eq"
  pointer = "/status"
  value = 200
  since = "fault_heal:pg"
  timeout_ms = 30000
  poll_ms = 500
  required = true
```

Why these values:

- The heal is `after_step:enroll-down`, not `after_steps`, so `enroll-after` runs healed.
- `enroll-down` has no `expect_error`, because `nodes_enroll_probe` returns Success with `status` for any HTTP answer. The clauses grade the status.
- `settle-down` is 5000 ms against S-L7's measured 2084 ms. `settle-up` is 10000 ms against 25 ms; the slack absorbs Npgsql discarding broken pooled connections.
- The token is unused after the cut enroll, because the database never saw it.

Security: no.

Verify: item 3's test; the orchestrator's Docker rerun (Checks).

### 2. Retitle the E2E to the claim it now makes

File: `tests/Dhadgar.Lab.Tests/Uat/FaultSmokeE2ETests.cs`.

- Lines 11-15 (class summary): replace with:

  ```csharp
  /// <summary>
  /// L22 seam (D-ML83): the daemon runs in process; <c>Specs/fault-smoke.toml</c> mints a token, cuts Nodes' Postgres through
  /// <c>fault_proxy_disable</c>, and checks that a direct-port enroll answers 5xx during the cut and 201 after the heal. Nodes
  /// <c>/readyz</c> has no Postgres check (F67), so readiness is checked only after the heal. Needs the Linux Docker engine.
  /// Containers are removed by label and name prefix only.
  /// </summary>
  ```

- Line 25: rename `FaultSmoke_PostgresCut_ReadyzFailsThenRecovers` to `FaultSmoke_PostgresCut_DbPathFailsThenRecovers`.
- Change nothing else. The traits, the flow and the per-row `grade == "pass"` assertion stay. An allowed I1 violation never changes a grade (`RowGrader.cs:54`), and only unallowed violations count toward the run's failures (`RowGrader.cs:167`).

Security: no.

Verify: build.

### 3. Pin the spec's shape with a non-Docker test

New file: `tests/Dhadgar.Lab.Tests/Uat/FaultSmokeSpecValidateTests.cs`. Copy `LabSelfTestSpecValidateTests.cs` (61 lines) whole, then:

- Rename the class to `FaultSmokeSpecValidateTests` and the summary to "The fault-smoke spec loads and validates against the daemon's real tool registry, and keeps the D-ML83 shape."
- Rename the fact to `FaultSmokeSpec_LoadsValidates_AndChecksTheDbPathNotReadiness`.
- Change the temp root prefix to `"fault-smoke-spec-"`.
- Change the path to `Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "tests", "Dhadgar.Lab.Tests", "Uat", "Specs", "fault-smoke.toml")`, and the `LoadText` file name to `"fault-smoke.toml"`.
- After the existing `problems.Count == 0` assertion, add:

  ```csharp
  UatRow row = Assert.Single(loaded.Spec!.Rows);
  Assert.Equal("pg-down", row.Id);
  Assert.Empty(loaded.Spec.Section.AllowInvariants);
  Assert.Equal(new[] { "I1" }, row.AllowInvariants);
  Assert.DoesNotContain(row.Expect, c => c.Op == "ne" && c.SourceFields.TryGetValue("path", out JsonElement p) && p.GetString() == "/readyz");
  Assert.Contains(row.Expect, c => c.Required && c.Op == "gte" && c.Value is { } v && v.GetInt32() == 500
      && c.SourceFields.TryGetValue("step", out JsonElement s) && s.GetString() == "enroll-down");
  Assert.Contains(row.Expect, c => c.Required && c.Op == "eq" && c.Value is { } v && v.GetInt32() == 201
      && c.SourceFields.TryGetValue("step", out JsonElement s) && s.GetString() == "enroll-after");
  UatFault fault = Assert.Single(row.Faults);
  Assert.Equal("after_step:mint", fault.Start);
  Assert.Equal("after_step:enroll-down", fault.Heal);
  ```

  Add `using System.Text.Json;`.

Why it fails when reverted:

- Restoring the old spec fails three assertions: `AllowInvariants`, the `gte 500` on `enroll-down`, and the fault `Start`/`Heal`.
- The `DoesNotContain` catches a re-added `/readyz` `ne 200` http clause.
- Dropping `allow_invariants` fails `Assert.Equal(new[] { "I1" }, ...)`.
- A typo in a tool or variable fails the validator assertion.

Security: no.

Verify: `dotnet test ... --filter "FullyQualifiedName~FaultSmokeSpecValidateTests"`.

### 4. Toxiproxy timeout test brings up its own stack (D-ML84)

File: `tests/Dhadgar.Lab.Tests/Faults/ToxiproxyTimeoutToxicDockerTests.cs` (62 lines). Rewrite the class as follows. Keep the namespace, the `ReadSomeAsync` helper (lines 48-61) unchanged and the toxic logic unchanged.

- Usings: add the set from `Seams/ToxiproxySeamTests.cs:5-18` that the code below uses (`Dhadgar.Agent.Core.Files`, `Dhadgar.Lab.Build`, `Dhadgar.Lab.Docker`, `Dhadgar.Lab.Leasing`, `Dhadgar.Lab.Processes`, `Dhadgar.Lab.Secrets`, `Dhadgar.Lab.Services`, `Dhadgar.Lab.Stack`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging.Abstractions`; `Redactor` is in `Dhadgar.Lab.Secrets`). Add `Dhadgar.Lab.Audit` only if a type below needs it, as S-L7's wiring may. Remove any using the compiler reports unused.
- Attributes on the class: `[Collection(LabDockerGate.Name)]` above the existing `[Trait("RequiresDocker", "true")]`.
- Summary (lines 8-12): keep the C19 sentence and replace "Needs the lab stack's `agent-ingress` proxy (25000)" with "Brings up its own lab stack, Nodes and the Gateway (D-ML84), and probes the Gateway through the `agent-ingress` proxy". Keep the "If the second request completes ..." sentence unchanged.
- Constants: delete `Port = 25000`. Use `LabPorts.AgentIngress`. Add `private const string LinuxContext = "desktop-linux";`. Replace line 19 with:

  ```csharp
  private static readonly byte[] Request = Encoding.ASCII.GetBytes(
      "GET /healthz HTTP/1.1\r\nHost: " + LabServices.ProbeHostHeader + "\r\nConnection: keep-alive\r\n\r\n");
  ```

- Test body. Keep the name `TimeoutToxic_AddedToAnOpenConnection_HoldsTheNextRequest` and `[Fact]`. Shape:

  ```csharp
  string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
  string home = Path.Combine(Path.GetTempPath(), "meridian-lab-l51-timeout-" + Guid.NewGuid().ToString("N"));
  _ = Directory.CreateDirectory(home);
  // Same wiring as ToxiproxySeamTests.cs:55-87: paths, validator, docker (LinuxContext), vault, toxiproxy, refs, targets, stack,
  // connections, redactor, leases, supervisor, logs, overlay, services ([new GatewayAdapter(), new NodesAdapter()]).
  // Every IDisposable among them is a `using var`, exactly as there.
  using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
  CancellationToken ct = cts.Token;
  try
  {
      Result<StackStatusDto> up = await stack.UpAsync(wipe: true, ct);
      Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);
      Result<LabBuildDto> build = await LabBuilder.BuildAsync(repoRoot,
          LabBuilder.Projects.Where(project => project.Name is "nodes" or "gateway").ToList(),
          docker, paths, validator, TimeProvider.System, ct);
      Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);
      foreach (string name in new[] { "nodes", "gateway" })   // the Gateway's readiness needs Nodes
      {
          Result<LabServiceStartDto> started = await services.StartAsync(name, build.Value.BuildId, ct);
          Assert.True(started.IsSuccess, started.IsFailure ? started.Error : string.Empty);
      }

      Assert.True(LabHosts.IsLabEndpoint(new Uri($"http://127.0.0.1:{LabPorts.AgentIngress}/healthz")), "agent-ingress is not a lab endpoint");
      using var client = new TcpClient();
      await client.ConnectAsync("127.0.0.1", LabPorts.AgentIngress, ct);
      NetworkStream stream = client.GetStream();
      // ... the existing lines 29-45 unchanged: first request 200, add the toxic through `toxi` (the stack's ToxiproxyClient),
      // second request empty within 3 s, and RemoveToxicAsync in the inner finally ...
  }
  finally
  {
      foreach (string name in new[] { "gateway", "nodes" })
      {
          _ = await services.StopAsync(name, CancellationToken.None);
      }

      _ = await stack.DownAsync(wipe: true, CancellationToken.None);
      Directory.Delete(home, recursive: true);
  }
  ```

  Use the single `ToxiproxyClient` that the stack wiring creates (named `toxi` in S-L7) for `AddToxicAsync` and `RemoveToxicAsync`, and delete the old `using var toxiproxy = new ToxiproxyClient();` (line 24).

- Do not delete this test or change its 3 s assertion. If the live rerun shows the second request completing, the orchestrator sends that to a ruling (C19). The coder does not act on it.

Security: yes, light (`security-advisor`). The test now creates and wipes lab containers, so check that the cleanup is the registry-scoped `DownAsync` and that no `docker` call names a non-lab resource.

Verify: build (`Faults` has no non-Docker test for this file); the orchestrator's Docker rerun.

### 5. Keep the nested tool error in `history_run` failures (D-ML85)

File: `src/Tools/Dhadgar.Lab/Tools/History/HistoryRunTool.cs`, class `DispatchedHistoryEnvironment`.

- Replace lines 227-235 (from `string code = ...` to the `return Result<JsonElement>.Failure(...)`) with `return Result<JsonElement>.Failure(FailureText(tool, result.Payload));`.
- Add this member after `Text` (line 211):

  ```csharp
  private const int MaxNestedMessageLength = 512;

  /// <summary>
  /// The failure text for a nested tool error: <c>[code] tool failed: message</c>. The payload is the dispatcher's redacted error
  /// envelope (<c>{"error":{"code","message"}}</c>); a missing code is <c>Lab.Unavailable</c>, a missing message is left out, and a
  /// long message is cut to 512 characters.
  /// </summary>
  internal static string FailureText(string tool, JsonElement payload)
  {
      string code = "Lab.Unavailable";
      string? message = null;
      if (payload.ValueKind == JsonValueKind.Object
          && payload.TryGetProperty("error", out JsonElement error)
          && error.ValueKind == JsonValueKind.Object)
      {
          if (error.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } codeText)
          {
              code = codeText;
          }

          if (error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } text)
          {
              message = text.Length <= MaxNestedMessageLength ? text : text[..MaxNestedMessageLength];
          }
      }

      return message is null ? "[" + code + "] " + tool + " failed" : "[" + code + "] " + tool + " failed: " + message;
  }
  ```

The S-L11 failure then reads `history_run failed: {"error":{"code":"Lab.Failed","message":"nodes_token_mint failed: nodes token mint failed: <Type>"}}`. `LabToolDispatcher.ParseError` (`:269-281`) splits on the first `]` only, so the nested text survives.

Security: yes (`security-advisor`). The nested message now reaches the MCP caller. It came from a payload the dispatcher already passed through `_redactor.Redact` (`LabToolDispatcher.cs:298`), and the outer `history_run` error is redacted again on the way out. Check that no history-environment tool puts a vault value or connection string in its error text.

Verify: item 6's tests.

### 6. Tests for item 5

File: `tests/Dhadgar.Lab.Tests/History/HistoryRunJobTests.cs`. Add four facts next to the existing `BuildSnapshot` facts (around :182-210). Build each payload with `JsonDocument.Parse(...).RootElement` inside a `using`:

- `FailureText_KeepsTheNestedMessage`: `{"error":{"code":"Lab.Failed","message":"nodes token mint failed: PostgresException"}}` gives exactly `"[Lab.Failed] nodes_token_mint failed: nodes token mint failed: PostgresException"`.
- `FailureText_NoMessage_KeepsCodeOnly`: `{"error":{"code":"Lab.Failed"}}` gives `"[Lab.Failed] nodes_token_mint failed"`.
- `FailureText_NotAnEnvelope_IsUnavailable`: `[]` gives `"[Lab.Unavailable] nodes_token_mint failed"`.
- `FailureText_LongMessage_IsCapped`: a message of 2000 `'x'` characters gives a result that ends with exactly 512 `'x'` characters, and whose length is `"[Lab.Failed] nodes_token_mint failed: ".Length + 512`.

Call it as `Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.FailureText(...)`, as the `BuildSnapshot` facts do. Reverting item 5 removes the member and the build fails. Restoring the code-only text fails the first and fourth facts.

Security: no.

Verify: `--filter "FullyQualifiedName~HistoryRunJobTests"`.

### 7. The barrier proxy sends the lab probe host upstream (D-ML85, F81)

File: `src/Tools/Dhadgar.Lab/Barrier/BarrierProxy.cs`, method `BuildOutbound` (:394-428). Just before `return request;` (:427), add:

```csharp
// The upstream is a loopback lab port (StaysOnUpstream). The Gateway's AllowedHosts refuse the IP-literal authority that
// HttpClient would derive from the destination, so the proxy names the lab probe host, as LabHttp does (R-HOST, F81).
request.Headers.Host = Dhadgar.Lab.Services.LabServices.ProbeHostHeader;
```

Leave `"Host"` in `StrippedHeaders` (:35): the client's own `Host` is still never forwarded.

Security: yes (`security-advisor`). This is the L49 barrier, a blast-radius area. The destination check (`StaysOnUpstream`, :461-463) is unchanged, and the header value is a constant loopback name, so no client input reaches it.

Verify: item 8.

### 8. Barrier test for item 7

File: `tests/Dhadgar.Lab.Tests/Barrier/BarrierProxyTests.cs`. Add after `HopByHopHeaders_Stripped` (:107-127):

```csharp
[Fact]
public async Task Forwarded_Host_IsTheLabProbeHost()
{
    await using Upstream up = await Upstream.StartAsync(200);
    var sink = new RecordingSink();
    await using BarrierProxy proxy = await StartProxyAsync(sink);
    Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

    using HttpResponseMessage response = await Send(proxy, 1);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    IReadOnlyDictionary<string, string> seen = Assert.Single(up.Seen);
    Assert.Equal(LabServices.ProbeHostHeader, seen["Host"]);
}
```

Add `using Dhadgar.Lab.Services;` if it is missing. Reverted, the upstream sees `127.0.0.1:25000` and the assertion fails. The test upstream already listens on `LabPorts.AgentIngress` (:361), so the destination is the real one.

Security: no (test).

Verify: `--filter "FullyQualifiedName~BarrierProxyTests"`.

### 9. S-L11 starts Nodes and the Gateway (D-ML85)

File: `tests/Dhadgar.Lab.Tests/Seams/EnrollHistorySeamTests.cs`.

- After the `stack_up` call (:68-72) and before the `history_run` call (:74), add:

  ```csharp
  // Nodes creates its schema on start (F80); the barrier's upstream is agent-ingress -> Gateway, whose readiness needs Nodes.
  foreach (string service in new[] { "nodes", "gateway" })
  {
      using JsonDocument started = await CallToolAsync(
          daemon.BaseAddress,
          token,
          "svc_start",
          new Dictionary<string, object?>(StringComparer.Ordinal) { ["service"] = service, ["lease_id"] = leaseId });
  }
  ```

  `svc_start` is `Leased` (`ToolPolicy.cs:38`), and with no `build_id` it uses the latest build from `lab_build`.

- Summary (:12-16): replace the second sentence with "The stack is brought up with `stack_up wipe`, and Nodes then the Gateway are started with `svc_start`, all under one lease, so the schema exists and the barrier's upstream answers (F80)."
- The rest stays: the assertions, the `finally` and `RemoveLabContainers`.

Security: no (test; cleanup unchanged).

Verify: build; the orchestrator's Docker rerun.

## Checks (in order; non-Docker only, per D-ML74)

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FaultSmokeSpecValidateTests|FullyQualifiedName~HistoryRunJobTests|FullyQualifiedName~BarrierProxyTests|FullyQualifiedName~ShippedSpecsValidateTests"
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests"
```

`git status` after the commit shows only the nine files named in items 1-9 (one new: `FaultSmokeSpecValidateTests.cs`). No lock file changes, because no package is added.

The orchestrator runs these at the wave boundary, not the coder, with Docker running:

```powershell
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FaultSmokeE2ETests|FullyQualifiedName~ToxiproxyTimeoutToxicDockerTests|FullyQualifiedName~EnrollHistorySeamTests"
```

Stop rules for that rerun (no fix-pass widening):

- If fault-smoke `db-path-down-5xx` is not held, send the row JSON to a ruling. The status it recorded is the evidence; a 2xx or 4xx means the cut did not reach the token lookup.
- If S-L11 fails with a message from the history environment, it now carries the nested text. Send it to a ruling.
- If S-L11 fails on `enroll-lost-response` linearizability, that is D-ML49 territory (idempotent retry, owner decision). Send it to a ruling and do not edit the model.
- If the timeout toxic test's second request completes, that is C19 (L51 item 9). Send it to a ruling.

## Packet facts

| Field | Value |
| --- | --- |
| Packet | wave4-docker fix, pass 1 (L22, L49, L51) |
| Worktree / branch | `.claude/worktrees/meridian-lab` / `campaign/meridian-lab` |
| Commit subject | `fix(lab): apply wave 4 docker-run ruling, pass 1 [L22/L49/L51]` |
| Security review | Yes, for items 4, 5 and 7 (lab container lifecycle in a test, nested error text reaching MCP, barrier headers) |
| Size | About 25k tokens |
| Order | After any wave 4 R1/R2 fix pass still open on `BarrierProxy.cs`, `HistoryRunTool.cs` or `HistoryRunJobTests.cs`. Re-read each file before editing. |
| Docs owed | None from the coder. The architect amended L22 file 6, L49 item 5 and L51 item 9 in `work-packets.md`. |

## Notes for the orchestrator

- Packet-table statuses are yours to set: L22, L49 and L51 fix.
- The brief's guess that this was the D-ML70 pooling cause is corrected. The fault-smoke failure is F67, no Postgres readiness check, and pooling played no part.
- `nodes.toml` row `nodes-pg-down-enroll` (`known_issue = "KI-F67"`) will also record an I1 violation during its cut. It grades as a known issue, so this ruling leaves it alone. Revisit it with D-ML71.
- Numbering used: F78-F81 and D-ML83-D-ML85.
