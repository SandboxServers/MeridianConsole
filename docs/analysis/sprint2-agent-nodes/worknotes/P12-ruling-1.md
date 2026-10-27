# P12 ruling 1: Nodes.Tests clock failures, the throwing-stop test, and what blocks the merge

Inputs (all report paths are in the P12 worktree, `.claude/worktrees/sprint2-agent-nodes-p12`):

- `.claude/reports/sprint2-agent-nodes/P12-review-3.md` (verdict `fix`: N1-N3, pass 3, so the loop ends here);
- `.claude/reports/sprint2-agent-nodes/P12-review-2.md`, `P12-fix-2.md`, `P12-security-3.md` (verdict `ship`);
- the P12 worktree at `bd5a260`, branch `s2/p12-agent-startup`;
- P12, P13 and P14 in [work-packets.md](../work-packets.md); D-S212, D-S215, D-S216, D-S231.

## Ruling

| Item | Ruling |
| --- | --- |
| 13 `Dhadgar.Nodes.Tests` failures (`[Enrollment.InvalidValidity]`) | **Blocks the merge. P12 owns them.** They are a test-harness defect that P12's own enrollment validity check exposed, not an environment problem and not a Nodes defect. Fixed by the fix pass below (review-3 N1-N3, tightened). **Decision D-S234.** |
| Production validity check in `EnrollmentService.EnrollAsync` (`:262-268`) | **Kept unchanged.** It is security pass 1 N2 and is correct. Do not relax it, widen the skew or remove it. |
| Throwing-stop test (review-2 N1 verify step) | **Accepted gap, closed. No owner.** `HubConnection.StopAsync` swallows its transport faults, so no seam can make it throw, and `DisconnectAsync` takes `_connectionLock` with the same token first, so a cancelled token never reaches `StopAsync`. The `finally` at `ControlPlaneClient.cs:228-250` was checked by review pass 3 and security pass 3. Adding a hub-connection abstraction to test six lines is not worth it. |
| Security-3 residual: `SetState` runs before the dispose and `StateChanged` is unguarded | **Accepted, no change.** There are no `StateChanged` subscribers in `src`; a throwing subscriber still leaves the hub null and the state `Disconnected`, so the next `ConnectAsync` rebuilds. |
| Security-3 residual: `DisconnectAsync` does not detach the `Closed`/`Reconnecting`/`Reconnected` handlers | Pre-existing, outside P12. No change. |
| Duplicate validity predicate (`EnrollmentService.cs:263` and `:509`) and the Windows store's stricter `NotBefore <= now` filter | **Not in P12. Folded into P14 as amendment A3. Decision D-S235.** |
| Process drift (branch `s2/p12-agent-startup`, commit subject, trailer) | **Accepted as is.** The branch is local only and merges into w1 (D-S215); history is not rewritten. The w1 merge commit subject is `merge: P12 agent bootstrap (#101)`, matching P11's. |

Open owner decisions: **none**.

## Why

| Fact | Evidence |
| --- | --- |
| The agent rejects a certificate whose `NotBefore` is more than 5 minutes after its own clock. | `EnrollmentService.cs:262-268`, `MaxClockSkewMinutes = 5` at `:46` |
| The real Nodes CA stamps `NotBefore = now` from the injected `TimeProvider`. | `CertificateAuthorityService.cs:460-461` |
| In the "Nodes Integration" collection that `TimeProvider` is one `FakeTimeProvider`, started at `DateTimeOffset.UtcNow` and shared by every test class. | `NodesWebApplicationFactory.cs:34`, `:89-90` |
| `AgentRenewalSeamTests` advances it by 61, 5, 56 and 61 minutes. After that, every certificate the CA issues is more than 5 minutes ahead of the system clock. | `AgentRenewalSeamTests.cs:124`, `:180`, `:198`, `:228` |
| `AgentSeamHarness.CreateEnrollmentService` defaults `timeProvider` to null, so `EnrollAgentAsync` (`:117`), `AgentEnrollmentContractTests.CreateAgent` (`:108`) and `GatewayNodesHarness.EnrollThroughGatewayAsync` (`:111-113`) run the agent on the system clock. | `AgentSeamHarness.cs:91-106` |
| The renewing agents already take the Nodes clock, which is why renewal itself passes. | `AgentRenewalSeamTests.cs:87`, `:163`, `:218`, `:245`; `GatewayAgentRenewalSeamTests.cs:132-150` |
| The failures are order dependent: the contract tests pass alone and with every other class, and 5 of 6 fail when paired with `AgentRenewalSeamTests`. At the P11 base, with no enrollment check, the suite was 482 of 482. | `P12-review-3.md`, "The 13 failures are caused by P12" |

1. **Why P12 owns this.** P12 added the check, and P12's check list omitted `Dhadgar.Nodes.Tests`, which holds the direct seam tests (D-S216) that build the real agent `EnrollmentService`. A red Nodes suite cannot merge into w1: it would sit under every later packet and under the campaign PR.
2. **Why a required parameter.** Review-3's three call-site edits fix today's failures. Making `timeProvider` required on the harness turns the next caller that forgets the clock (P13 and later seam tests) into a compile error instead of an order-dependent failure.
3. **Why a deterministic test.** The suite only fails when xUnit happens to run `AgentRenewalSeamTests` first. The new test advances the shared clock itself, so it fails on every run if the harness goes back to the system clock.

## What P12 must do now (fix pass 3, same worktree and branch)

Worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12`, branch `s2/p12-agent-startup`, head `bd5a260`. Edit only the three files named below. No production code changes. Do not stage the untracked `docs/analysis/sprint2-agent-nodes/` folder in the worktree.

**Rules:**

- **R-NULL:** nullable is enabled; warnings are errors.
- **R-XUNIT:** follow `Dhadgar.Nodes.Tests` conventions (`[Collection("Nodes Integration")]`, `Assert.*`, no `Assert.Skip`).
- **R-USING:** `using var` on every factory and certificate the test creates.
- **No package versions in `.csproj`;** no `.csproj` change is needed.
- Do not touch `src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs`. Its validity check stays exactly as it is.

### Step 1. `AgentSeamHarness`: the clock is required

File `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/AgentSeamHarness.cs`.

1. Replace `CreateEnrollmentService` (`:88-106`, doc comment included) with:

   ```csharp
   /// <summary>
   /// Creates the real agent enrollment service over the given HTTP client factory and certificate store.
   /// </summary>
   /// <param name="httpClientFactory">Routes the agent's requests to the host under test.</param>
   /// <param name="store">The agent's certificate store.</param>
   /// <param name="timeProvider">
   /// The Nodes host's clock. The agent checks the issued certificate's validity on this clock, and the Nodes CA
   /// stamps NotBefore from the same one, so a seam test must pass the host's TimeProvider, never the system clock (D-S234).
   /// </param>
   public static AgentEnrollmentService CreateEnrollmentService(
       IHttpClientFactory httpClientFactory,
       ICertificateStore store,
       TimeProvider timeProvider)
   {
       ArgumentNullException.ThrowIfNull(timeProvider);

       return new AgentEnrollmentService(
           store,
           httpClientFactory,
           Options.Create(new AgentOptions()),
           NullLogger<AgentEnrollmentService>.Instance,
           new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance),
           new ClientCertificateProvider(store),
           new AgentIdentityProvider(),
           enrollmentTokenCleanup: null,
           timeProvider: timeProvider);
   }
   ```

2. In `EnrollAgentAsync`, replace line 117:

   ```csharp
   var agent = CreateEnrollmentService(new TestServerClientFactory(factory.Server), store);
   ```

   with:

   ```csharp
   var agent = CreateEnrollmentService(
       new TestServerClientFactory(factory.Server),
       store,
       factory.Services.GetRequiredService<TimeProvider>());
   ```

   `Microsoft.Extensions.DependencyInjection` is already imported (`:13`).

### Step 2. `AgentEnrollmentContractTests`: pass the clock, and pin it with a test

File `tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs`.

1. In `CreateAgent` (`:104-109`), replace the `return` line with:

   ```csharp
   return AgentSeamHarness.CreateEnrollmentService(
       new TestServerClientFactory(factory.Server),
       store,
       factory.Services.GetRequiredService<TimeProvider>());
   ```

   `Microsoft.Extensions.DependencyInjection` is already imported (`:7`).

2. Add this test after `RealAgent_CannotReuseAnEnrollmentToken` (`:97`):

   ```csharp
   [Fact]
   public async Task RealAgent_EnrollsWhileNodesClockRunsAhead()
   {
       // D-S234: the Nodes CA stamps NotBefore from the host's fake clock, and the agent rejects a certificate
       // whose NotBefore is more than 5 minutes ahead of its own clock. The harness must give the agent the
       // host's clock; on the system clock this enrollment fails with [Enrollment.InvalidValidity].
       _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(10));
       using var factory = CreateFactoryWithRealCa();

       var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());

       Assert.NotEqual(Guid.Empty, enrolled.NodeId);
       Assert.NotNull(store.ClientCertificate);
   }
   ```

   `EnrollAgentAsync` resolves through the existing `using static ...AgentSeamHarness;` (`:9`). `CreateFactoryWithRealCa()` resolves to the class's own private method, as in the two existing tests.

### Step 3. `GatewayNodesHarness`: pass the clock

File `tests/Dhadgar.AgentSeam.Tests/GatewayNodesHarness.cs`, `EnrollThroughGatewayAsync` (`:111-113`). Replace:

```csharp
var agent = AgentSeamHarness.CreateEnrollmentService(
    new TestServerClientFactory(GatewayServer, stripApiPrefix: false),
    store);
```

with:

```csharp
var agent = AgentSeamHarness.CreateEnrollmentService(
    new TestServerClientFactory(GatewayServer, stripApiPrefix: false),
    store,
    NodesBase.TimeProvider);
```

`NodesBase.TimeProvider` is the same `FakeTimeProvider` the Nodes host registers (`:91-92`).

### Step 4. Grep

Grep `tests/` for `CreateEnrollmentService(`. The only call sites must be the three above. If the build reports another caller, pass that host's `TimeProvider` the same way and list the file in the report.

### Step 5. Revert check (do it, then undo it)

1. In `EnrollAgentAsync`, temporarily pass `TimeProvider.System` instead of `factory.Services.GetRequiredService<TimeProvider>()`.
2. Run `dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~RealAgent_EnrollsWhileNodesClockRunsAhead"`. It must **fail** with `[Enrollment.InvalidValidity]`.
3. Restore the line from Step 1 and run the same filter. It must pass.
4. Record both outcomes in the report.

### Step 6. Checks (in order)

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentEnrollmentContractTests|FullyQualifiedName~AgentRenewalSeamTests"
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p12\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
```

Expected:

| Check | Expected |
| --- | --- |
| Filtered Nodes run | 0 failed (it was 5 failed of 6 at `bd5a260`) |
| Full `Dhadgar.Nodes.Tests` | 483 of 483 (482 plus the new test) |
| `Dhadgar.AgentSeam.Tests` | 5 of 5 |
| `Dhadgar.Agent.Core.Tests` | 118 of 118 |

Have Docker running if the Nodes integration fixtures need it. If any other test fails, report it with its message; do not change production code to make it pass.

### Step 7. Commit

Stage only the three test files. Commit subject:

`test(nodes): agent seam harness enrolls on the Nodes host clock (P12-ruling-1, D-S234)`

**Security review:** not needed for this pass. The diff is test-only, and security pass 3 is `ship`. The orchestrator runs one `packet-reviewer` pass on `bd5a260..<new head>` to confirm the counts above. That run also closes security-3's last "Not fixed" bullet (the failures were confirmed, caused by the harness, and fixed).

## Merge

After that review is `ship`, P12 merges into w1 directly after P11, as P11-ruling-1 set out. The orchestrator puts the `security-review` label on the campaign PR (D-S215), not on a P12 PR.

## Ledger changes (applied with this ruling)

- README: **D-S234** (seam tests run the agent on the Nodes host's clock; packets that change agent auth or communication code run both seam suites) and **D-S235** (one agent-certificate validity predicate, with the 5-minute skew).
- work-packets.md § P12: a "Ruling 1" note, and `Dhadgar.Nodes.Tests` added to its checks.
- work-packets.md § P13: amendment A1 (clock budget for a real agent host on the system clock).
- work-packets.md § P14: amendment A3 (D-S235), plus `Dhadgar.Nodes.Tests` and `Dhadgar.AgentSeam.Tests` in its checks.

## Owner decisions

None.
