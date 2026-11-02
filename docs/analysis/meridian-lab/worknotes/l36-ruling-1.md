# L36 ruling 1: sink readiness, `via=sink`, KI-NOTIF-OUTBOX, merge with L34

Packet L36 (Notifications, Discord, webhook sink). Branch `ml/l36`, worktree `ml-l36`, head `b5e4e873`, base `8191eb8f`. Review pass 3 of 3 ended on `fix` (`.claude/reports/meridian-lab/ml-l36-review-3.md` in `ml-l36`). Security pass 3 ended on `ship`.

**Outcome:**

- Apply both pass-3 notes, with N1 tightened: the sink is admitted by a sink-only predicate, not by every infra port.
- Rule the three pass-3 decisions here, so no owner question is needed:
  - L36 adds `http_request via=sink` (D-ML147).
  - I2 reports missing Notifications outbox tables as a matchable violation (D-ML148, which amends D-ML80 for I2 only).
  - The ledger's Discord readiness wording becomes `/healthz`.
- Revert the out-of-scope `GatewayRouteTable` rewrite. L21g owns it (D-ML145).
- Apply security Low 1, the `Authorization: Token …` redaction.
- Park the webhook delivery (KG-W7-2) and the dropped I2 error text (KG-W7-3).
- Run `NotifyDockerTests`. No session has run it yet, and it is the packet's own acceptance check.
- One packet-coder applies section 4. A merge step (section 6) reconciles L36 with L34. No owner question is raised.

## 1. Numbering fix

Two rulings each claimed D-ML144. [l19n ruling 1](l19n-ruling-1.md) was written first, at 08:32, and [l34 ruling 1](l34-ruling-1.md) at 08:34.

| ID | Holder | Action |
| --- | --- | --- |
| D-ML144 | L19n: S-L15 red on scratch branch `ml/l19n-red-scratch` | Keeps the ID |
| D-ML145 | L19n: `GatewayRouteTable` templates and Order (L21g) | Keeps the ID |
| D-ML146 | L34: BetterAuth's loopback Secrets stub | **Renamed from D-ML144** |
| D-ML147, D-ML148 | L36: this ruling | New |
| F126, F127 | L19n | Unchanged; this ruling adds no F number (it amends F25) |
| KG-W5-22 | L19n | Unchanged |
| KG-W7-1 | L34 | Unchanged |
| KG-W7-2, KG-W7-3 | L36: this ruling | New |

The next free IDs after this ruling are **D-ML149**, **F128** and **KG-W7-4**.

### 1.1 Rename step R1 (ml-l34, one packet-coder, mechanical)

Run this before L34 merges into `campaign/meridian-lab`. On base every D-ML number stops at D-ML143, and L19n's D-ML144 lives only in `ml-l19n`. So every `D-ML144` in `ml-l34` belongs to L34.

1. `git -C C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34 grep -n "D-ML144"`. The expected hits are:
   - `src/Tools/Dhadgar.Lab/Build/LabBuilder.cs` (2)
   - `src/Tools/Dhadgar.Lab/Services/BetterAuthLabAdapter.cs` (2)
   - `src/Tools/Dhadgar.Lab/README.md` (1)
   - `tests/Dhadgar.Lab.Tests/Build/NodeStagingTests.cs` (1)
   - `tests/Dhadgar.Lab.Tests/Services/BetterAuthSecretsStubTests.cs` (1)
   - `tools/lab/images/lab-secrets-stub.mjs` (1)
   - `tools/lab/images/node-app.Dockerfile` (2)
   - `docs/analysis/meridian-lab/worknotes/ml-l34.md` (1)

   Record the count.
2. In exactly those files, replace every `D-ML144` with `D-ML146` using the Edit tool. Change nothing else.
3. Check that `git grep -n "D-ML144"` finds nothing and that `git grep -c "D-ML146"` finds the same count as step 1. Then build `tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`. These are comment-only changes, so the build is the whole check.
4. Commit only those files, with subject `docs(lab): renumber L34's decision D-ML144 to D-ML146 [L34]`.

Gitignored reports under `.claude/reports/` keep their text. The orchestrator replaces `D-ML144` with `D-ML146` in the untracked `worknotes/l34-ruling-1.md`, so that it matches.

## 2. Dispositions

| Item | Disposition | Edit |
| --- | --- | --- |
| Review-3 N1: `StartAsync("sink")` is refused after the container starts | **Apply, tightened (D-ML147).** Add a sink-only predicate. The review's `IsLabInfraEndpoint` would also admit the Toxiproxy API and RabbitMQ management ports. Move the check before any docker call, so a refusal leaves nothing running | E1, E2, T1-T3 |
| Review-3 N2: stale README sink line | **Apply.** Also fix the two tool descriptions that still list only gateway and nodes | E7, E8 |
| Review-3 decision: C4 `http_request via=sink` | **Apply in L36 (D-ML147).** Contract C4 (`work-packets.md:324`) assigns `sink` to L36, and the packet's contract excerpt lists it. This is a ruling, not an owner question | E3, T4 |
| Review-3 decision: KI-NOTIF-OUTBOX cannot match an `InvariantCheckError` | **Apply (D-ML148).** On a migrated DB, 42P01 from I2 is a violation, `outbox-tables-missing` | E4, E5, E6, T5 |
| Review-3 decision: Discord `/readyz` should read `/healthz` | **Ledger edit** (section 8) | none |
| Review-3 out of scope: `GatewayRouteTable` rewrite (review-1 N6) | **Revert in L36.** L21g owns both files (D-ML145). The base table already routes `notifications` and `discord`. L36's non-Docker check accepts the one KG-W5-22 failure | E0 |
| Packet Files item 5: "a Discord webhook test call reaches the sink, or the refusal is recorded" | **Park as KG-W7-2 (owner L38).** The lab has no bus-publish tool. Discord delivers only from `SendDiscordNotificationConsumer`, and Notifications does not register `DiscordWebhookClient` (F25). Record the gap in the test summary | E9 |
| Security-3 Low 1: `Authorization: Token|Bot|Digest <value>` keeps the value | **Apply.** A narrow post-pass, so the existing outputs do not change | E10, T6 |
| Security-3 Low 2: I2 drops a failure text when another target has violations | **Park as KG-W7-3 (owner L38).** E4 removes the 42P01 case. A full fix needs an errors field on `InvariantOutcome` (C11), which L36 should not change | none |
| Review-3 Low: an unquoted multi-word password keeps its tail | **Accept.** Form bodies encode spaces, and JSON values are quoted | none |
| Review-3 Low: Discord readiness accepts any 503 on `/healthz` | **Accept** (F26 design). `NotifyDockerTests` also asserts `botStatus` in the body | none |
| Docker test never run | **Required now**, check C5 (section 5) | none |

## 3. Decisions

**D-ML147: the sink is the only infra endpoint that `svc_start` and `http_request` reach.**

- `LabHosts.IsSinkEndpoint(uri)` is true only for http, a loopback host and port `LabPorts.Sink` (18090).
- `svc_start` accepts a readiness URL that passes either `IsLabEndpoint` or `IsSinkEndpoint`, and checks it before any docker call.
- `http_request via=sink` composes `http://127.0.0.1:18090<path>`, and only that `via` value may use `IsSinkEndpoint`.
- `IsLabEndpoint` and `IsLabInfraEndpoint` do not change.
- Rejected: adding 18090 to `ServicePorts`, because `IsLabEndpoint` also admits https and fault targets read `ServicePorts`. Also rejected: admitting `IsLabInfraEndpoint` in readiness and `http_request`, because a UAT row could then call the Toxiproxy API (the fault controller) or RabbitMQ management.

**D-ML148: on a migrated database, I2 reports a missing outbox table as violation `outbox-tables-missing`.** This amends D-ML80 for I2 only.

- The violation applies when the gate said `Applicable` and a probe fails with exactly `LabDb.RejectedStatementError("42P01")`. I2 then records `InvariantViolation("I2", <service>, "outbox-tables-missing", "<table>: table does not exist (SQLSTATE 42P01)", window.To, {table, sqlstate})` and skips that target's other probe.
- With no gate, or with any other error, I2 keeps the error path. I3, `TransitionSnapshotter` and every other reader keep "42P01 on a migrated DB is an error".
- Basis: F25, now confirmed in source.
  - Notifications calls `AddEntityFrameworkOutbox<NotificationsDbContext>` and `UseBusOutbox` (`src/Dhadgar.Notifications/Program.cs:42-45`).
  - It migrates at boot (`Program.cs:67`).
  - Its only migration, `20260119165431_InitialNotifications`, and its model have no outbox entities.
  - So the gate says `Applicable`, and every probe hits 42P01.
- This is a product defect in the invariant's own subject, so it is a violation. A violation is matched to KI-NOTIF-OUTBOX (C12) and survives beside Nodes violations. A check error is never matched, turns every row in the window UNVERIFIED, and was hidden by any Nodes violation (security-3 Low 2).
- Rejected:
  - Keeping the error. KI-NOTIF-OUTBOX could then never match, which is the review's point.
  - A skip. "Not applicable" would hide the defect, and D-ML80 forbids grading by skip.
- Consequence: any row whose window overlaps a running Notifications container now gets this violation, matched to KI-NOTIF-OUTBOX. Today the same rows get UNVERIFIED, so no row grades worse. L38's catalogs claim the issue.

## 4. Edits for the implementer (one packet-coder, worktree `ml-l36`)

**Worktree:** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l36`, branch `ml/l36`, starting from `b5e4e873`.

**Security review area:** yes (R-HOST allowlist, sink redaction). The orchestrator adds a `security-advisor` pass.

**Rules to keep:**

- `Result<T>` for every failure, never throw.
- Every HTTP target goes through a `LabHosts` predicate before the request (R-HOST).
- No package versions in `.csproj`.
- `using` on every `IDisposable`.
- Nullable enabled, warnings as errors.
- xUnit `[Fact]`/`[Theory]` in the existing test classes, and assertion messages pass `result.Error`.
- Never log or print secret values (R-LOG).
- Edit only the files named below.

### Commit 1: revert the route table

**E0.** Run `git checkout 8191eb8f -- src/Tools/Dhadgar.Lab/Invariants/GatewayRouteTable.cs tests/Dhadgar.Lab.Tests/Invariants/GatewayRouteTableTests.cs`. Then commit only those two files, with subject `revert(lab): leave GatewayRouteTable to L21g (D-ML145) [L36]`. Check: `git diff 8191eb8f -- <the two files>` is empty.

### Commit 2: the ruling fixes

**E1.** In `src/Tools/Dhadgar.Lab/LabHosts.cs`, add this after `IsLabInfraEndpoint`:

```csharp
/// <summary>
/// True only for http, a loopback host, and <see cref="LabPorts.Sink"/>. The webhook sink is the one infra endpoint a lab
/// service's readiness check and <c>http_request via=sink</c> may reach (D-ML147); the other infra ports stay out.
/// </summary>
public static bool IsSinkEndpoint(Uri uri) => IsLabInfraEndpoint(uri) && uri.Port == LabPorts.Sink;
```

**E2.** In `src/Tools/Dhadgar.Lab/Services/LabServices.cs`, make three changes:

- (a) Add this method:

  ```csharp
  /// <summary>The adapter's readiness URL on the loopback host.</summary>
  internal static Uri ReadinessUri(ILabServiceAdapter adapter) =>
      new($"http://127.0.0.1:{adapter.HostPort}{adapter.HealthPath}", UriKind.Absolute);

  /// <summary>True when the adapter's readiness URL is a lab service endpoint or the sink (D-ML147).</summary>
  internal static bool IsReadinessEndpoint(ILabServiceAdapter adapter)
  {
      Uri uri = ReadinessUri(adapter);
      return LabHosts.IsLabEndpoint(uri) || LabHosts.IsSinkEndpoint(uri);
  }
  ```

- (b) In `StartAsync`, insert the refusal right after the `Adapter(service)` failure return and before `RequireStackUpAsync`:

  ```csharp
  if (!IsReadinessEndpoint(adapter.Value!))
  {
      return Result<LabServiceStartDto>.Failure("[Lab.Untrusted] readiness URL is not a lab endpoint");
  }
  ```

  Delete the old check at lines 205-209, the `readyUri` construction and its `IsLabEndpoint` test. If `WaitReadyAsync` or a later line needs the URI, use `ReadinessUri(target)`. `grep -n "IsLabEndpoint(readyUri)"` must then find nothing.
- (c) Replace the `WaitReadyAsync` doc sentence (line 452) "The caller checks the URL against `<see cref="LabHosts.IsLabEndpoint"/>` before calling." with "`StartAsync` checks the URL with `IsReadinessEndpoint` before any docker call."

**E3.** In `src/Tools/Dhadgar.Lab/Http/LabHttp.cs`, make three changes:

- (a) In `ServiceTargets`, add `["sink"] = LabPorts.Sink,`.
- (b) Replace the last statement of `Compose` with:

  ```csharp
  bool trusted = LabHosts.IsLabEndpoint(target)
      || (string.Equals(via, "sink", StringComparison.Ordinal) && LabHosts.IsSinkEndpoint(target));
  return trusted
      ? Result<Uri>.Success(target)
      : Result<Uri>.Failure("[Lab.Untrusted] the composed URI is not a lab endpoint");
  ```

- (c) Line 84 doc: "(`gateway`, `agent_ingress`, `sink` or a lab service name)".

Also, in `src/Tools/Dhadgar.Lab/Tools/Http/HttpRequestTool.cs:31`, the description becomes "Send one lab-only HTTP request via gateway, agent_ingress, sink or a lab service. …", with the rest of the sentence unchanged. The Host header stays `ProbeHostHeader` only for gateway and agent_ingress.

**E4.** In `src/Tools/Dhadgar.Lab/Data/LabDb.cs`, add the method below and use it at line 147. The text stays the same, byte for byte.

```csharp
/// <summary>The error for a statement Postgres rejected; only the SQLSTATE is returned (the message can echo values).</summary>
internal static string RejectedStatementError(string sqlState) =>
    $"[Lab.InvalidArgument] Postgres rejected the statement (SQLSTATE {sqlState})";
```

**E5.** In `src/Tools/Dhadgar.Lab/Invariants/I2OutboxInvariant.cs`, make three changes:

- (a) Add these members:

  ```csharp
  /// <summary>The check id of a missing outbox table on a migrated database (D-ML148, KI-NOTIF-OUTBOX).</summary>
  public const string TablesMissingCheckId = "outbox-tables-missing";

  private const string UndefinedTable = "42P01";
  ```

- (b) In the probe loop, replace the `if (query.IsFailure)` block with:

  ```csharp
  if (query.IsFailure)
  {
      if (_gate is not null && string.Equals(query.Error, LabDb.RejectedStatementError(UndefinedTable), StringComparison.Ordinal))
      {
          // The gate said migrated, yet the outbox table is absent: a product defect, matched to KI-NOTIF-OUTBOX (D-ML148).
          violations.Add(new InvariantViolation(
              Id,
              target.Service,
              TablesMissingCheckId,
              $"{probe.Table}: table does not exist (SQLSTATE {UndefinedTable})",
              window.To,
              _redactor.Redact(JsonSerializer.SerializeToElement(new { table = probe.Table, sqlstate = UndefinedTable }))));
          break;
      }

      // Keep checking the other targets; the failure is returned after the violations found so far (KG-W7-3).
      failure ??= $"{target.Service} {probe.CheckId}: {query.Error}";
      break;
  }
  ```

  `_gate is not null` is enough. The loop has already `continue`d past every database whose state is not `Applicable`.
- (c) Rewrite the `NotificationsTargets` doc (lines 41-43) as: "The Notifications outbox (L36): the EF outbox MassTransit uses in `notifications`. Its migrations do not create the tables (F25), so on a migrated database each check reports violation `outbox-tables-missing` (D-ML148, KI-NOTIF-OUTBOX)."

**E6.** In `docs/guides/uat-specs/known-issues.toml`, in the KI-NOTIF-OUTBOX entry, change `patterns = ['^inv:I2\|notifications\|']` to `patterns = ['^inv:I2\|notifications\|outbox-tables-missing$']`. Leave everything else in the entry unchanged. In `tests/Dhadgar.Lab.Tests/Results/KnownIssuesReachabilityTests.cs:26`, change the sample to `SignatureBuilder.Invariant("I2", "notifications", I2OutboxInvariant.TablesMissingCheckId)` and add `using Dhadgar.Lab.Invariants;` if it is missing. The old sample's check `outbox` is one I2 never emits.

**E7.** In `src/Tools/Dhadgar.Lab/README.md`, make two changes:

- Line 57: replace "not started by the lab yet (no sink start path); the probe mode and image exist." with "started by `svc_start sink` from the probe image (`meridian-lab/probe:<build_id>`), published on `127.0.0.1:18090` only, with a 128 MiB memory cap. `http_request via=sink` reads it (D-ML147)."
- Line 55: replace "Its EF outbox is checked by I2 and reports `KI-NOTIF-OUTBOX` while the tables are missing." with "Its EF outbox is checked by I2. Its migrations create no outbox tables (F25), so I2 reports `outbox-tables-missing`, matched to `KI-NOTIF-OUTBOX` (D-ML148)."

**E8.** Two tool descriptions:

- `src/Tools/Dhadgar.Lab/Tools/Services/ServiceTools.cs:34` becomes "Start a lab service (gateway, nodes, notifications, discord, sink) from a build; refused unless the stack is up."
- `src/Tools/Dhadgar.Lab/Tools/Build/BuildTool.cs:41`: replace "gateway, nodes and probe" with "gateway, nodes, notifications, discord and probe". Leave the rest of the sentence unchanged.

**E9.** In `tests/Dhadgar.Lab.Tests/Services/NotifyDockerTests.cs`, change the summary's last sentence (lines 22-23) to "The Discord webhook call through the sink is not exercised: the lab has no bus-publish path to `SendDiscordNotification` (KG-W7-2)."

**E10.** In `src/Tools/Dhadgar.Lab.Probe/Sink/SinkRecorder.cs`, add the regex below. In `Redact`, apply it as the last step: `return AuthorizationScheme.Replace(SecretPair.Replace(withBearer, RedactPair), "$1[redacted]");`.

```csharp
/// <summary>
/// After <see cref="SecretPair"/>, an unquoted <c>authorization: Token abc</c> reads <c>authorization: [redacted] abc</c>;
/// this redacts the credential that followed a scheme word other than Bearer/Basic (security L36 pass 3).
/// </summary>
private static readonly Regex AuthorizationScheme = new(
    @"(?i)(authorization\\?""?\s*[:=]\s*\[redacted\]\s+)[^""&,\s}\[]+",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
```

The `\[` exclusion leaves `authorization: [redacted] [redacted]`, the Bearer output, unchanged.

**E11.** Make the admin key a named argument now, so the L34 merge (section 6) cannot bind it to L34's new sixth parameter:

- `LabServices.cs:384`: `adminKey);` becomes `AdminApiKey: adminKey);`.
- `NotifyAdapterTests.cs:77`: `withAdminKey ? "lab-admin-key-value" : null);` becomes `AdminApiKey: withAdminKey ? "lab-admin-key-value" : null);`.

### Tests (each one fails when its edit is reverted)

**T1.** In `NotifyAdapterTests`, add `Readiness_EveryShippedAdapter_IsALabOrSinkEndpoint`.

- It enumerates the adapters the way `LabCoverageTests.RegisteredAdapterNames` does: every concrete `ILabServiceAdapter` type in `typeof(ILabServiceAdapter).Assembly`, through `RuntimeHelpers.GetUninitializedObject`. Name it `ShippedAdapters()`.
- Assert `IsReadinessEndpoint` for each adapter, with the adapter name in the message.
- Also assert that the names contain `sink` and that there are at least 5 adapters.
- It fails for `sink` against `b5e4e873`. After the L34 merge it also covers identity, betterauth and panel.

**T2.** In `NotifyAdapterTests`, add a `[Theory]` `Readiness_InfraPortOtherThanSink_IsRefused` over `LabPorts.ToxiproxyApi` and `LabPorts.RabbitMqManagement`. It asserts `LabServices.IsReadinessEndpoint(new LoopbackAdapter(port, HttpStatusCode.OK))` is false. It fails if E2 uses `IsLabInfraEndpoint`, as review-3 suggested.

**T3.** In `NotifyAdapterTests`, add `StartAsync_UntrustedReadiness_RefusedBeforeAnyDockerCall`.

- Build `LabServices` the way `NotifyDockerTests` lines 35-71 do, with a temp `home` deleted in `finally`, two changes:
  - `adapters = [new LoopbackAdapter(LabPorts.ToxiproxyApi, HttpStatusCode.OK)]`;
  - a private `CountingDocker : IDockerCli` that records every call and answers `Result<DockerOutput>.Success(new DockerOutput(1, "", "fake"))`, passed to `LabTargetRegistry`, `LabStack` and `LabServices`.
- Call `StartAsync("loopback", "a9c623a1", ct)`.
- Assert the error starts with `[Lab.Untrusted]` and that `CountingDocker.Calls` is empty.
- Against `b5e4e873` the stack check comes first and returns `[Lab.NotFound] stack is down`.
- No Docker is needed and no traits are set.

**T4.** In `ObservationPathTests`, add two tests:

- `HttpDirect_ToSink_ResolvesToLoopbackSinkPort`: `LabHttp.Compose("sink", "/_sink/requests")` succeeds with port 18090 and host `127.0.0.1`.
- `HttpDirect_ToSink_SchemeRelativePath_IsRefused`: `Compose("sink", "//evil.example/x")` fails. Assert `IsFailure`, then assert the error starts with `[Lab.Untrusted]` or `[Lab.InvalidArgument]` (either refusal holds).

In `LabHostsTests`, add `IsSinkEndpoint_KnownCases_MatchesExpectation` as a `[Theory]` with these cases:

| URI | Expected |
| --- | --- |
| `http://127.0.0.1:18090/` | true |
| `http://localhost:18090/` | true |
| `https://127.0.0.1:18090/` | false |
| `http://10.0.0.5:18090/` | false |
| `http://127.0.0.1:18474/` | false |
| `http://127.0.0.1:15040/` | false |

The first Compose test fails without E3 (a), with `[Lab.NotFound]`, and without E3 (b), with `[Lab.Untrusted]`.

**T5.** In `I2OutboxInvariantTests`, make three changes:

- Replace `I2_Applicable_42P01_IsStillAFailure` with `I2_Applicable_42P01_IsTablesMissingViolation`.
  - Setup: `NodesTargets`, gate `Applicable`, and a query failing with `LabDb.RejectedStatementError("42P01")`.
  - Assert success with exactly one violation, `CheckId == I2OutboxInvariant.TablesMissingCheckId`, `Service == "nodes"`, a `Detail` that contains `42P01`, and evidence that is not null.
- Add `I2_NoGate_42P01_IsStillAFailure`: the same query with no gate gives `IsFailure`, and the error contains `42P01`.
- In `I2_NodesViolation_SurvivesNotificationsMissingTables`, make three changes:
  - pass an `Applicable` gate;
  - use `LabDb.RejectedStatementError("42P01")` for `Message`;
  - assert two violations: nodes `outbox-message-stuck` and notifications `outbox-tables-missing`.

  Rename it `I2_NodesViolation_AndNotificationsTablesMissing_BothReported`.

`I2_QueryFails_ResultIsFailure` stays as it is. It uses a non-42P01 error, so it keeps the anti-swallow check. The replaced test fails without E5 (it gets `IsFailure`). The no-gate test fails if E5 drops the `_gate is not null` guard.

**T6.** In `tests/Dhadgar.Lab.Tests/Probe/SinkTests.cs`, add a `[Theory]` `Redact_AuthorizationWithOtherScheme_IsRedacted`.

- It takes these inputs:
  - `Authorization: Token abc123SECRET`
  - `authorization: Bot abc123SECRET`
  - `Authorization=Digest abc123SECRET&x=1`
- Assert `SinkRecorder.Redact(input)` does not contain `abc123SECRET`.
- For the third input, also assert it ends with `&x=1`.
- It fails without E10.
- The existing Bearer and Basic tests must still pass unchanged.

### Commit 2 subject and docs

Stage only the files named in E1-E11 and T1-T6 (no lock files). Commit with subject `fix(lab): apply L36 ruling 1, sink readiness, via=sink, I2 tables-missing [L36]`, ending with the session's attribution lines.

Then update `docs/analysis/meridian-lab/worknotes/ml-l36.md` in the same commit:

- Add a `## Ruling 1 (l36-ruling-1)` section that lists E0-E11 and T1-T6 as done, the check results (C1-C6), and the `NotifyDockerTests` result line.
- In that section, say the stale untracked `worknotes/L36.md` is superseded. Do not commit it.

## 5. Checks (in order, all in `ml-l36`)

```powershell
$W = 'C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l36'
dotnet build "$W\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj" -c Release -p:TreatWarningsAsErrors=true                    # C1
dotnet build "$W\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj" -c Release -p:TreatWarningsAsErrors=true        # C2
dotnet build "$W\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj" -c Release -p:TreatWarningsAsErrors=true            # C3
dotnet test "$W\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj" -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"   # C4
dotnet test "$W\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj" -c Release --no-build --filter "FullyQualifiedName~NotifyDockerTests"   # C5, Docker Desktop running (context desktop-linux)
git -C $W status --porcelain                                                                                           # C6
```

**C4** allows exactly one failure: `Dhadgar.Lab.Tests.Invariants.GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster` (F126, KG-W5-22, the L19n precedent). Any other failure, or a different failing test in that class, means the packet `failed`. If L21g has merged into the campaign and this branch by then, the expected count is 0.

**C5** must pass:

- 1 test, and the sink start gets past `StartAsync("sink")`.
- Before the run, check `docker --context desktop-linux info`. If it fails, return `blocked docker` with the error line. Do not skip C5.
- If C5 fails, record the failing assertion and the `StartAsync` error in the worknote and return `failed`. Do not patch beyond this ruling.

**C6** shows only the committed files. If `packages.lock.json` or any other file changed during the build or C5, restore it with `git checkout -- <file>`, list it in the report and do not commit it.

## 6. Merge step with L34 (after both pass their ruling checks)

L34 and L36 conflict in seven files. Every conflict is an additive union.

**Order:**

1. R1 (section 1.1) lands on `ml/l34`.
2. Whichever packet ships to `campaign/meridian-lab` first merges cleanly.
3. A fresh packet-coder then runs this step in the second packet's worktree: it merges `campaign/meridian-lab` into that branch, resolves the conflicts with these rules, and reruns C1-C5 there.
4. Commit with subject `merge(lab): campaign into <branch>, union of L34 and L36 adapters`, ending with the attribution lines.

The expected order is L34 first, then the step runs in `ml-l36`.

| File | Resolution |
| --- | --- |
| `Build/LabBuilder.cs` `Projects` | Keep both sets, in the order gateway, nodes, notifications, discord, probe, identity, betterauth, panel. The probe entry uses L36's `meridian-lab-probe.dll`, because the AssemblyName is `meridian-lab-probe` in both branches. Keep L34's staging hunks |
| `README.md` services | Keep L34's table and paragraphs. Add rows `Notifications`, `15090`, Postgres/RabbitMQ/webhook (`notifications-*`), `/healthz`; `Discord`, `15120`, Postgres/RabbitMQ/webhook (`discord-*`), `/healthz` (503 asserted); `Sink`, `18090`, none, `/_sink/requests`. Keep L36's three bullets (with E7) below the table. Replace "Identity has no outbox tables, so the I2 outbox invariant covers Nodes only." with "I2 checks the Nodes and Notifications outboxes; Identity has none." Add Notifications, Discord and Sink to the Gateway row's dependencies |
| `Services/GatewayAdapter.cs` | Keep all four proxy-port constants. `Proxies` = `["gateway-redis", "gateway-nodes", "gateway-identity", "gateway-betterauth", "gateway-notifications", "gateway-discord"]`. Keep all four cluster address entries |
| `Services/ILabServiceAdapter.cs` | Parameters `…, RabbitMqPassword, string? ExchangePublicKeyPem = null, string? ExchangePrivateKeyPem = null, string? BetterAuthSecret = null, string? AdminApiKey = null`, with all four `<param>` docs. Keep L36's `PrintMembers` and append `, ExchangePublicKeyPem = ` + (`[present]` or `null`), `, ExchangePrivateKeyPem = [redacted]` and `, BetterAuthSecret = [redacted]` |
| `Services/LabServices.cs` `ComposeEnvironment` | Keep L34's identity and betterauth blocks, then L36's notifications/discord DB and admin-key blocks. The context call is `new LabServiceContext(buildId, connections, …, password.Value!, exchangePublic, exchangePrivate, betterAuthSecret, AdminApiKey: adminKey)` |
| `Stack/LabConnectionStrings.cs` | Keep all four refs: `IdentityExchange`, `BetterAuthSecret`, `NotificationsAdmin` and `DiscordAdmin` |
| `Stack/LabStack.cs` credentials | `[Postgres, LabOwner, LabReader, RabbitMq, Redis, IdentityExchange, BetterAuthSecret, NotificationsAdmin, DiscordAdmin]` |

After resolving:

- `git grep -n "<<<<<<<\|>>>>>>>"` finds nothing.
- Every `LabServiceContext` construction passes `AdminApiKey` by name: `LabServices.cs`, `NotifyAdapterTests.Context`, L34's `AuthStackAdapterTests.Context`, and `ServiceAdapterTests.Context`.
- `Context_ToString_NeverPrintsSecretsOrConnectionStrings` and L34's `ToString` test both pass.
- T1 now enumerates at least 8 adapters.
- C4 and C5 pass, and the L34 Docker test (`FullyQualifiedName~AuthStackDockerTests`, or the name L34's packet gives) passes too.

The merge step is not a security-review area unless a conflict needs more than this table. If it does, return `blocked spec`.

## 7. Ruling check

1. A fresh `packet-reviewer` reviews `b5e4e873..<commit 2>` against this note only. It does not reopen settled pass 1-3 notes. The pass is `ship` only if all of these hold:
   - `git diff 8191eb8f -- src/Tools/Dhadgar.Lab/Invariants/GatewayRouteTable.cs tests/Dhadgar.Lab.Tests/Invariants/GatewayRouteTableTests.cs` is empty.
   - `grep -n "IsLabEndpoint(readyUri)" src/Tools/Dhadgar.Lab/Services/LabServices.cs` and `grep -n "no sink start path" src/Tools/Dhadgar.Lab/README.md` find nothing.
   - In `StartAsync`, the `IsReadinessEndpoint` refusal comes before `RequireStackUpAsync`.
   - `Compose` admits `IsSinkEndpoint` only for `via == "sink"`.
   - `IsLabEndpoint`, `IsLabInfraEndpoint` and `LabPorts` are unchanged since `8191eb8f`.
   - I2 makes a violation only when `_gate is not null` and the error equals `LabDb.RejectedStatementError("42P01")`.
   - The toml pattern ends `outbox-tables-missing$`, and the reachability sample uses `TablesMissingCheckId`.
   - The reviewer reverts E2 (b), E3 (b), E5 (b) and E10 one at a time, locally and uncommitted, and sees T3, T4, T5 and T6 go red respectively. The worktree is then restored with `git checkout -- .`.
   - The report carries C4's single allowed failure and C5's passing result line. With no C5 result, the verdict is not `ship`.
2. A `security-advisor` pass covers the same range. The advisor confirms four things:
   - the sink predicate admits only http, loopback and port 18090;
   - no other infra port became reachable through `svc_start` or `http_request`;
   - the scheme-relative `via=sink` path is refused;
   - E10 adds no backtracking risk (one bounded character class after a literal).
3. The merge step (section 6) is checked by its own C4/C5 results and the grep list in section 6, not by a separate review.

If the ruling check is not `ship`, the orchestrator records BlockedDecision. There is no ruling 2 without a new brief.

## 8. Ledger edits for the orchestrator

The orchestrator makes these edits after commit 2 lands, in the campaign worktree's `docs/analysis/meridian-lab/`.

**1. `README.md`, Decisions.**

- Rename L34's row to **D-ML146**. Its text is from [l34 ruling 1](l34-ruling-1.md) section 8 item 2.
- Add **D-ML147**: "The webhook sink is the only infra endpoint `svc_start` readiness and `http_request` reach: `LabHosts.IsSinkEndpoint` (http, loopback, 18090), `via=sink` only; the readiness check runs before any docker call. Rejected: 18090 in `ServicePorts`; `IsLabInfraEndpoint` (would expose the Toxiproxy API and RabbitMQ management)." Status: Accepted (architect). Basis: L36 review-3 N1 and decision 1; [l36 ruling 1](worknotes/l36-ruling-1.md).
- Add **D-ML148**: "I2 reports a missing outbox table on a migrated DB (gate `Applicable`, SQLSTATE 42P01) as violation `outbox-tables-missing`; without a gate, or for any other error, it stays a check error. Amends D-ML80 for I2 only; I3 and `TransitionSnapshotter` keep 42P01 as an error. Rejected: keeping the error (KI-NOTIF-OUTBOX can never match); a skip." Status: Accepted (architect). Basis: F25; L36 review-3 decision 2; this note.
- In D-ML80's text, after "42P01 on a migrated database included", add "(except I2's missing outbox tables, D-ML148)".

**2. `README.md`, Findings, F25.** Replace "its model appears to lack the outbox tables" with "its model and its only migration (`20260119165431_InitialNotifications`) have no outbox tables, and it migrates at boot (`Program.cs:67`), so I2 reports `outbox-tables-missing` (D-ML148)".

**3. `README.md`, Known gaps.**

- Add **KG-W7-2**: "**Live:** no lab path makes Discord deliver a webhook to the sink. The lab has no bus-publish tool for `SendDiscordNotification`, and Notifications does not register `DiscordWebhookClient` (F25). `NotifyDockerTests` proves start and readiness only." Closes with "L38 adds a catalog row with a lab-reachable trigger, or records the row as unreachable with a known issue." Owner: L38.
- Add **KG-W7-3**: "**Live, narrow:** when one I2 target has violations and another target's query fails (other than D-ML148's 42P01), the outcome keeps the violations and drops the error text (security L36 pass 3)." Closes with "an errors field on `InvariantOutcome` (C11), or L38 confirms no row needs it." Owner: L38.

**4. `README.md`, packet table.**

- L36 row: status becomes "Ruling 1 ([l36 ruling 1](worknotes/l36-ruling-1.md); D-ML147, D-ML148)".
- L34 row: its D-ML144 reference becomes D-ML146.

**5. `work-packets.md` L36 section.**

- Line 3709: "Discord's `/readyz` 503" becomes "Discord's `/healthz` 503".
- Line 3712: append "(sink predicate D-ML147)".
- Line 3719, item 4: replace "(Notifications' outbox, if the tables exist; else the I2 check reports `InvariantCheckError` "outbox tables missing", which is KI-NOTIF-OUTBOX)" with "(Notifications' outbox; with no outbox tables on its migrated DB, I2 reports violation `outbox-tables-missing`, matched to KI-NOTIF-OUTBOX, D-ML148)".
- Line 3720, item 5: replace "a Discord webhook test call reaches the sink, or the refusal is recorded" with "the sink starts and answers `GET /_sink/requests` with `[]`; webhook delivery is KG-W7-2 (L38)".
- Add to **Files**: "6. `LabHosts.IsSinkEndpoint`, `LabServices.IsReadinessEndpoint`, `LabHttp` `via=sink` (D-ML147)".
- The branch line: `ml/l36` is the actual branch.

**6. `work-packets.md` C11.**

- Line 731: after "and so is any query failure on an applicable database, 42P01 included", add "— except I2, which reports a missing outbox table on an applicable database as violation `outbox-tables-missing` (D-ML148)".
- Line 2772: after "42P01 on a migrated DB is still an error", add "(I2's outbox tables excepted, D-ML148)".

**7. `work-packets.md` C4 (line 324).** After `sink` (L36), add "through `LabHosts.IsSinkEndpoint` only (D-ML147)".

**8. L38 spec.**

- Its Files list gains: "Notifications catalog rows claim `KI-NOTIF-OUTBOX` through `inv:I2|notifications|outbox-tables-missing`; KG-W7-2 and KG-W7-3 are closed or re-parked here."
- In its contract excerpt, cite D-ML147 for any row that reads the sink through `http_request via=sink`.

**9. `worknotes/l34-ruling-1.md`.** Replace every `D-ML144` with `D-ML146`. `worknotes/l19n-ruling-1.md` is unchanged.
