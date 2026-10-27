# Wave 1 ruling 1 (after wave1-review-1 = decision)

Input: `.claude\worktrees\sprint2-agent-nodes-w1\.claude\reports\sprint2-agent-nodes\wave1-review-1.md` (copy it to `worknotes/wave1-review-1.md` before that worktree is ever removed). Tree: `campaign/sprint2-agent-nodes-w1` at `c1a1497`, local only (D-S215). Code read on that tip.

The reviewer's gate run is accepted: S1 5/5, S2 4/4, Nodes.Tests 457, Gateway.Tests 144, Agent.Core.Tests 16, Contracts.Tests 5, Agent.Windows.Tests 383. No contract drift and no duplicated cross-service type.

## Summary

| Item | Packet | Ruling | Who applies it |
| --- | --- | --- | --- |
| N1 (with D1) | P04 → P16, P08 | **Re-spec, no wave 1 code change.** The hub's `IsRevoked` check is right for wave 1, because grace does not exist yet. P16 gains step 6b (the hub calls `AgentCertificateState.IsUsable`), two hub tests and a predicate guard (step 8a). P08's S3 now drives a real hub connection and heartbeat with the old certificate, so the seam starts red on this. | Done in this ruling: `work-packets.md` amended. |
| N2 | P05 → **P24f** | **New fix packet P24f** on the w1 tip. `ConnectAsync` returns `Result` for every fault except the caller's cancellation (D-S227). | Sonnet `packet-coder`, steps below |
| D2 | P19, P15 | **Accept, close the gap in the UAT.** P19's `-StackOnly` smoke gains S-hub-ws (WebSockets-only hub connection and heartbeat through the Gateway). P15's status wording says "long-polling transport" unless that step passed in the recorded run. Finding F22. | Done in this ruling: `work-packets.md` amended |
| D3 | P15 (D-S220 issue) | **Accept, record.** Corrected: the 401 short-circuits before *every* Gateway limiter, `global` included, not just `PerAgent`. Added to P15's D-S220 follow-up issue text. | Done in this ruling |
| D4 | - | **No action.** Dev compose only; D-S210 (A) is the owner's ruling, and the file documents the CIDR. | - |
| D5 | - | **No action.** The one campaign PR carries `security-review` (D-S215). P24f gets a `security-advisor` pass. | - |
| D6 | - | **No action.** Already owned: `HeartbeatService` and `ControlPlaneClient`'s `NodeId` reads by P11, Windows `Program` wiring by P14, S3 by P08, S4 by P13. | - |

**Owner decisions: none.** N1 is a spec gap in P16 (the architect's), not a risk trade-off. Its only option that changes behaviour, "the hub ignores grace", would contradict the owner's D-S212 (B). The open owner decisions are unchanged: D-S222, D-S223 and D-S220 (not blocking).

## N1: the hub's inline revocation check (P04 decision)

**Finding (F20).** `AgentHub.IsCertificateCurrentAsync` (`src/Dhadgar.Nodes/Hubs/AgentHub.cs:128-132`) runs `.AnyAsync(c => c.Thumbprint == thumbprint && c.IsRevoked, ct)` on `OnConnectedAsync` and on every `Heartbeat`, and aborts the connection when it is true. P16 makes a renewed certificate `IsRevoked = true` with a future `GraceUntil`. After P16 as written, that certificate passes `MtlsMiddleware` (negotiate 200) and the hub then aborts it. That breaks the D-S212 promise and the lost-response recovery. S3 as written checked only negotiate, so P16 would have passed every listed test.

**Why it is not fixed in wave 1.** `GraceUntil` and `IsUsable` do not exist until P16. Today, `IsRevoked` without grace is the correct rule, and `AgentHubTests.Connect_WithRevokedCertificate_IsRefused` and `Heartbeat_AfterCertificateRevoked_IsRefusedAndConnectionClosed` pin it.

**Ruling: re-spec P16 and P08** (applied in `work-packets.md` by this ruling):

1. **Contract C3a**: the `IsUsable` callers are listed: `ValidateCertificateAsync`, `AgentHub.IsCertificateCurrentAsync` and the renew endpoint.
2. **P16 rule block**: the same caller list, plus a pointer to step 8a.
3. **P16 step 6** is re-anchored to the P17f code: the revocation check after `FindAgentCertificateAsync`, which returns `CertificateTrustStatus.Untrusted`.
4. **P16 step 6b (new)**: `AgentHub.IsCertificateCurrentAsync` loads the row with `FirstOrDefaultAsync(c => c.Thumbprint == thumbprint)` and returns `row is null || AgentCertificateState.IsUsable(row, timeProvider.GetUtcNow().UtcDateTime)`. Its doc comments are updated.
5. **P16 step 8a (new)**: a grep guard over `IsRevoked` in `src/Dhadgar.Nodes`, with an allow-list. Any hit in `AgentHub.cs` or `CertificateAuthorityService.cs` is a defect.
6. **P16 tests**: `Heartbeat_CertificateInGrace_IsAcknowledged` and `Heartbeat_CertificatePastGrace_IsRefusedAndAborted` in `AgentHubCertificateExpiryTests`, with the seeding code given. The CA test names are corrected to `IsTrusted`/`IsUntrusted`, and `Decommission_EndsRenewalGrace` now expects `Untrusted`. The revert check names step 6b.
7. **P16 docs**: the Nodes README hub section states the usable rule.
8. **P08 (S3)**: before renewal, the direct test copies the enrolled certificate and key into `oldStore`. After renewal, a real `ControlPlaneClient` on `oldStore` connects and its heartbeat is acknowledged. After 61 minutes, its next heartbeat is not acknowledged. The Gateway-path test does the connect and heartbeat through the Gateway. The "expected red" paragraph names the hub abort, which P16 step 6b turns green.
9. **P09 step 3**: keep the `CertificateTrustStatus` enum. This is a P07-ruling-1 leftover, folded in here.

No implementer is needed for N1 now. P08 and P16 coders read the amended sections. The ruling-check reviewer confirms that the amendments say what is above.

## N2: `ConnectAsync` still throws (P05 fix)

**Finding (F21).** P05 listed four exception types to catch around `StartAsync`. The outer `catch (Exception) { …; throw; }` (`ControlPlaneClient.cs:166-171`) still lets everything else escape: `ICertificateStore.GetClientCertificate()` (`:94`, a Windows store fault), `new Uri(...)` (`:67`, a malformed endpoint), and the hub builder. P12's bootstrap loop would crash on them. The gap is in P05's spec, so it gets a fix packet and no re-review of P05.

**Ruling: new fix packet P24f (D-S227).** Numbered after P23, the highest ID in the ledger. P17 is P17f.

## P24f: `ControlPlaneClient.ConnectAsync` returns `Result` for unexpected faults

- **Branch** `s2/p24f-connect-result`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p24f`, from the w1 tip `c1a1497`. **Depends on** nothing beyond w1. **Merges** into w1 last in wave 1, after P07. **Security review** yes (agent project, `IControlPlaneClient` implementation): run `security-advisor` beside the reviewer.
- **Commit subject** `fix(agent-core): ConnectAsync returns Result for unexpected faults`
- **Size** small, about 15k tokens: 1 product file, 1 test file, 1 README.

**Rules (inlined):**
- R-RESULT: return `Result` for validation, IO and network failures; never throw for them. Only the caller's own `OperationCanceledException` propagates. Error strings use the `"[Area.Code] message"` style.
- R-LOG: never log key material or certificates. The `Result` error carries the exception **type name** only, never `ex.Message`.
- R-USING: `using` / `await using` on every `IDisposable` in tests (`AgentMeter`, `AgentActivitySource`, `ControlPlaneClient`, `CancellationTokenSource`).
- R-NULL: nullable is on, and Release builds with warnings as errors.
- R-CPM: no new packages and no `Version=` in any `.csproj`.
- R-XUNIT: xUnit v2 `[Fact]` and plain `Assert`; names follow `Subject_Condition_Expectation`; follow the existing `ControlPlaneClientTests` style.
- Edit only the three files named below. Do not touch `docs/analysis/`, `IControlPlaneClient.cs`, `HeartbeatService.cs` or any test project other than `Dhadgar.Agent.Core.Tests`.

### Steps

1. **`src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs`, `ConnectAsync`, line 67:** replace `var baseUri = new Uri(_options.ControlPlane.Endpoint);` with:

   ```csharp
   if (!Uri.TryCreate(_options.ControlPlane.Endpoint, UriKind.Absolute, out var baseUri))
   {
       return Refuse("[ControlPlane.InvalidEndpoint] Control plane endpoint is not an absolute URI.");
   }
   ```

   Leave the HTTPS check that follows unchanged. It now uses this `baseUri`.

2. **Same method, the outer catch at lines 166-171:** replace the whole block `catch (Exception ex) { _logger.LogError(...); SetState(...); throw; }` with these two clauses, in this order, and keep the `finally` that releases `_connectionLock`:

   ```csharp
   catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
   {
       // The caller's own cancellation is the one exception ConnectAsync lets through (D-S227).
       SetState(ConnectionState.Failed, "Connect cancelled");
       throw;
   }
   catch (Exception ex)
   {
       // Any other fault (certificate store, hub builder) is a failed connect, never a throw (D-S24, F8, D-S227).
       _logger.LogError(ex, "Unexpected error while connecting to control plane");
       SetState(ConnectionState.Failed, ex.GetType().Name);
       return Result.Failure($"[ControlPlane.ConnectFailed] unexpected {ex.GetType().Name}");
   }
   ```

   Do not change the inner `try/catch` around `StartAsync` or the `Refuse` and `ConnectFailed` helpers. If the analyzers reject the general catch (CA1031), use `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`, the filter P17f uses. Never add a suppression.

3. **`tests/Dhadgar.Agent.Core.Tests/Communication/ControlPlaneClientTests.cs`:** add three tests and two private stores. Reuse `CreateClient` and the `meter`/`activity` pattern of the existing tests.
   - `ThrowingCertificateStore : ICertificateStore`: `GetClientCertificate()` throws `new CryptographicException("store fault")`. Every other member behaves like `StubCertificateStore`.
   - `CancellingCertificateStore(CancellationTokenSource cts) : ICertificateStore`: `GetClientCertificate()` runs `cts.Cancel(); throw new OperationCanceledException(cts.Token);`. Every other member behaves like `StubCertificateStore`.
   - `ConnectAsync_CertificateStoreThrows_ReturnsFailure_NoThrow`: `https://localhost` with `ThrowingCertificateStore`. Assert `result.IsFailure`, `result.Error` starts with `"[ControlPlane.ConnectFailed]"` and contains `"CryptographicException"` but not `"store fault"`, and `client.State == ConnectionState.Failed`. **Red without step 2:** `ConnectAsync` throws `CryptographicException`.
   - `ConnectAsync_InvalidEndpoint_ReturnsFailure_NoThrow`: endpoint `"not a uri"` with `StubCertificateStore`. Assert a failure starting with `"[ControlPlane.InvalidEndpoint]"` and state `Failed`. **Red without step 1:** the error is `ConnectFailed … UriFormatException` (with step 2), or a throw (without it).
   - `ConnectAsync_CallerCancelledDuringConnect_Throws`: `using var cts = new CancellationTokenSource();` and `CancellingCertificateStore(cts)`; `await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));`; then assert `client.State == ConnectionState.Failed`. **Red if the first catch clause is dropped:** the general catch would return a failure instead of propagating the cancellation.
   - Add `using System.Security.Cryptography;` if needed.

4. **Docs: `src/Agents/Dhadgar.Agent.Core/README.md` lines 299-300.** Replace the second bullet with: ``- `ConnectAsync` never throws except for the caller's own cancellation. Failures return `Result.Failure` with a `[ControlPlane.*]` code: `InvalidEndpoint`, `InsecureEndpoint`, `NoCertificate`, or `ConnectFailed` (transport errors, timeouts, and any unexpected fault such as a certificate store error, reported by exception type only).``

5. **Checks, in order** (`<wt>` = the P24f worktree; no Docker needed):

   ```powershell
   dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet build <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
   dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentHubSeamTests"
   dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
   ```

   Expected: Agent.Core.Tests 19 (16 + 3), S1 5/5, S2 4/4.

   **Revert checks.** These are temporary edits: restore each one and record the result in the report.
   1. Restore `throw;` in the general catch: `…CertificateStoreThrows…` fails.
   2. Restore `new Uri(...)`: `…InvalidEndpoint…` fails.
   3. Delete the `OperationCanceledException` clause: `…CallerCancelledDuringConnect_Throws` fails.

   Before committing, `git -C <wt> status --porcelain` lists only the three files.

6. **Commit** locally on `s2/p24f-connect-result`: one commit, no push. Stage these paths explicitly:
   - `src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs`
   - `tests/Dhadgar.Agent.Core.Tests/Communication/ControlPlaneClientTests.cs`
   - `src/Agents/Dhadgar.Agent.Core/README.md`

   The body says: "ConnectAsync rethrew any exception outside the four StartAsync cases (certificate store faults, malformed endpoint), although it returns Result (D-S24, F8). wave1-ruling-1 N2, D-S227." End with the campaign's two trailer lines.

**Security-advisor focus:**
- Caller cancellation still propagates.
- No exception message (which could contain store paths or subject names) reaches the `Result` error or the state message. The log keeps the exception, which is acceptable at Error level.
- State is never left at `Connecting`.
- No `HubConnection` or `X509Certificate2` leaks on the new failure paths. The previous instances are disposed at the top of the next `ConnectAsync` and in `DisposeAsync`, as on the existing `ConnectFailed` path.

## D2 to D6 (details)

- **D2 → F22.** Not a wave 1 blocker: the long-polling flow runs the same middleware on every request. An in-process WebSocket test would need a new `WebSocketFactory` hook in `ControlPlaneHubTransport` (C7, agent code) and still could not go through YARP on `TestServer`. Real Kestrel in the UAT is the cheaper and more faithful place for it. So P19 gains **S-hub-ws** (`work-packets.md` P19 step 9): a WebSockets-only `HubConnection` with the enrolled certificate, through the Gateway, plus one heartbeat that must be acknowledged, with `LastHeartbeat` set. P15 step 1 now writes "long-polling transport" unless the recorded run's S-hub-ws passed.
- **D3.** The reviewer's claim is understated. Because `UseAuthorization` (`Program.cs:320`) short-circuits before `UseRateLimiter` (`:323`), a 401 on an agent route is counted by **no** limiter, `global` included. This is not new in this wave and is not a defect of this campaign's packets. It is added to the P15 D-S220 follow-up issue text, with the option to move rate limiting ahead of authorization with an IP partition for unauthenticated traffic. D-S220 stays an open owner decision that blocks nothing.
- **D4, D5, D6.** No change (see Summary).

## Ledger changes made by this ruling

- `README.md`:
  - findings F19-F22;
  - decisions D-S226 (the P07-ruling-1 leftover: the CA scope per lookup, and 503) and D-S227 (`ConnectAsync`);
  - D-S212 names the hub as an `IsUsable` caller, and D-S220 gains the rate-limiter order;
  - S3's "Proves" includes the hub connection in grace;
  - packet table: P01-P07 and P17f Integrated, P24f Ready, P08 and P16 marked amended;
  - wave 1 merge order and gate now include P17f and P24f.
- `work-packets.md`:
  - wave 1 merge order and gate;
  - C3a caller list, and C5's 503 row (the P07-ruling-1 leftover);
  - pointer sections for P17f and P24f;
  - P08 steps 1, 4 and 3, and its expected red;
  - P09 step 3 (keep `CertificateTrustStatus`);
  - P16 rules, steps 6, 6b and 8a, tests, revert check and docs;
  - P19 S-hub-ws;
  - P15 status wording (WebSockets, concurrency) and the D-S220 issue text.
- `RESUME.md` is rewritten for the new next actions.

## How wave 1 closes

1. `ops`: `git worktree add C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p24f -b s2/p24f-connect-result campaign/sprint2-agent-nodes-w1`. Then copy `wave1-review-1.md` from the w1 worktree's `.claude/reports/` to this folder's `worknotes/`, and copy the P17f and P07 reports too, which RESUME says are not copied yet.
2. One Sonnet `packet-coder` applies **P24f** (section above).
3. **Ruling check:** one fresh `packet-reviewer` plus one `security-advisor`, both on `c1a1497..<P24f sha>`. The reviewer also checks that the `work-packets.md` amendments listed above match this ruling: P08, P09 step 3, P16 steps 6, 6b and 8a with its tests, P19 S-hub-ws, P15. A ruling check that is not `ship` is BlockedDecision (skill §4).
4. At `ship`, the Sonnet `general-purpose` merge agent merges `s2/p24f-connect-result` into w1. It then reruns S1 (`--filter FullyQualifiedName~AgentHubSeamTests`) and S2, and records the new tip.
5. The wave 1 gate holds. The orchestrator asks the owner about D-S222, D-S223 and D-S220 (as RESUME already plans), then starts wave 2 with {P08, P09}.
