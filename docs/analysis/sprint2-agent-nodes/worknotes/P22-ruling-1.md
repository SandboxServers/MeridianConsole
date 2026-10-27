# P22 ruling 1: `AddCertificate` adds only to `LocalMachine\Root`, and the three pass-1 tests get their mutation checks

- **Packet** P22 (UAT service mode, U10, run-scoped localhost TLS root). **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p22`, branch `s2/p22-uat-tls-root`, head `20b32cd`, base `62f930a` (w1 tip).
- **Trigger** pass 2 ended `decision` on review N3 (P22-review-2, "Not fixed"). Original wording: P22-review-1 N3. P22-security-1 N1 is applied.
- **Decision** [D-S240](../README.md#decisions) (new, architect). No owner decision blocks.
- **Amendment 1** (2026-10-27). The first apply (report `P22-ruling-apply` (a local report in the P22 worktree, not committed)) stopped at step 1, row M1: the mutation left the test green and nothing was changed. The cause and the fix are in [Amendment 1](#amendment-1-why-m1-stayed-green). The new step 1A strengthens the U8 test and adds its U6 twin, and step 1 now runs after it. The production code is right and does not change.

## Ruling

**Permit the one-method edit (option 1). Do not accept the gap.**

- P22 says the TLS leaf and the decoy CA, both in `LocalMachine\Root`, are the only machine certificate additions the UAT makes (D-S222 (A)). `AddCertificate` came in with P22 (`62f930a..705a615`), and today it accepts any store location and name. A later caller could add to `CurrentUser\Root`, `AuthRoot` or `TrustedPeople`. None of those has a manifest arm, and `RootSnapshot` does not watch them.
- P18 keeps the allowlist tight (D-S237, D-S238): the driver refuses what its spec does not name. A writer that is wider than its spec is the same defect class, seen from the producer side. The fix is one guard and one test, and no caller changes.
- Pass 2 found nothing else open. The coder did not run the mutation checks for the three tests it added in pass 1, so this apply runs them too.

**D-S240.** `WindowsMachineActions.AddCertificate` refuses every target except `LocalMachine\Root`, compared ordinally and before anything is parsed or opened. The `HasPrivateKey` refusal stays. The test of the guard uses a certificate that keeps its private key, so even with the guard removed the test can never open a store.

## Amendment 1: why M1 stayed green

The production code is right: the U6 and U8 `finally` blocks stop the agent with `CancellationToken.None` (security N1). The fake is right too: `RenewalWorld.StopAsync` honours its token, because it calls `ct.ThrowIfCancellationRequested()` before `OnStop`. Under M1 the cancelled token does reach the stop, and the stop aborts before the agent stores its certificate. The test cannot see that, for this reason:

1. `OnStop` runs at every stop, U6's included. U6's stop (with `CancellationToken.None`) already replaces the certificate in `My` and records the new one in the manifest through `RecordAfterStop(…, "U6")`.
2. Under M1, U8's stop throws `OperationCanceledException` before `OnStop`, so `My` still holds the certificate from U6's stop. That certificate is alone in `My` and is already in the manifest.
3. The test asserts only "one certificate in `My`, and its SHA-1 is in the manifest". Both the fixed run and the M1 run satisfy that. `Assert.ThrowsAnyAsync<OperationCanceledException>` passes in both runs too: the fixed run throws from `Task.Delay` in `PollAsync`, and the M1 run throws from the fake's stop.

The fix is a test change only. The test captures the certificate in `My` when U8 starts and asserts that a different one is there at the end, so it fails whenever U8's stop does not reach `OnStop`. The same gap exists for U6 (line 362), which has no cancellation test, so step 1A adds its twin. `RenewalWorld` does not change.

## Permission (narrow)

The coder may edit only these three places:

1. `tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs`: only the body of `AddCertificate`. Nothing else in the file changes, its `/// <inheritdoc />` included.
2. `tests/Dhadgar.Agent.Windows.Uat.Tests/WindowsMachineActionsTests.cs`: add one test method. Existing tests stay as they are.
3. `tests/Dhadgar.Agent.Windows.Uat.Tests/RenewalLegsTests.cs` (Amendment 1): only what step 1A says. Replace the one method `RunAsync_WhenCancelledDuringU8_StillRecordsACertificateStoredAfterTheStop` and add one method after it. Nothing else changes, the `RenewalWorld` class included.

`RenewalLegs.cs`, `IMachineActions.cs`, `RecordingActions.cs`, `CleanupEngine.cs`, `RunPaths.cs`, the producers and all docs are off limits. The mutation steps below change other files temporarily, but each one is restored before the next step, and none of them is committed.

## Step 0: precondition

From the worktree, `git status --porcelain` must list only `?? docs/analysis/sprint2-agent-nodes/`, and `git rev-parse --short HEAD` must print `20b32cd`. If either check fails, stop and report `blocked`.

## Step 1A: pin the stop after a cancel in U8 and U6 (Amendment 1)

In `tests/Dhadgar.Agent.Windows.Uat.Tests/RenewalLegsTests.cs`, replace the whole method `RunAsync_WhenCancelledDuringU8_StillRecordsACertificateStoredAfterTheStop` (its `[Fact]` line through its closing brace) with the two methods below, in this order. The file already has every `using` they need.

```csharp
    [Fact]
    public async Task RunAsync_WhenCancelledDuringU8_StillRecordsACertificateStoredAfterTheStop()
    {
        // Security N1, U8 side: Ctrl+C while U8 polls. The poll is cancelled, and the stop in the finally must still complete
        // (CancellationToken.None), so the certificate the agent stores while it stops is recorded before the run ends.
        // OnStop also runs at U6's stop, so the certificate in My when U8 starts is captured: the stop must replace it.
        using var cts = new CancellationTokenSource();
        string? inMyAtU8Start = null;
        _world.OnRestart = w =>
        {
            inMyAtU8Start = w.My.Count == 1 ? w.My[0].Sha1Thumbprint : null;
            w.Rows.Add(new CertRow(Guid.NewGuid(), "0000000000000000", false));
            cts.Cancel();
        };
        _world.OnStop = w => w.AgentRenewsFromGrace();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _world.Legs().RunAsync(new Dictionary<string, object?>(), cts.Token));

        // The same certificate still alone in My means U8's stop was aborted before the agent stored its certificate.
        var stored = Assert.Single(_world.My).Sha1Thumbprint;
        Assert.NotNull(inMyAtU8Start);
        Assert.NotEqual(inMyAtU8Start, stored);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry certificate
            && string.Equals(certificate.ThumbprintSha1, stored, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringU6_StillRecordsACertificateStoredAfterTheStop()
    {
        // Security N1, U6 side: Ctrl+C while U6 polls. The agent does not renew on start, so the first poll is unmet and its
        // delay throws. The stop in U6's finally must still complete (CancellationToken.None), so the certificate the agent
        // stores while it stops replaces C1 in My and is recorded before the run ends.
        using var cts = new CancellationTokenSource();
        _world.OnFirstStart = _ => cts.Cancel();
        _world.OnStop = w => w.AgentRenewsFromGrace();
        var c1Sha1 = _world.C1Store!.Sha1Thumbprint;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _world.Legs().RunAsync(new Dictionary<string, object?>(), cts.Token));

        // C1 still alone in My means U6's stop was aborted before the agent stored its certificate.
        var stored = Assert.Single(_world.My).Sha1Thumbprint;
        Assert.NotEqual(c1Sha1, stored);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry certificate
            && string.Equals(certificate.ThumbprintSha1, stored, StringComparison.OrdinalIgnoreCase));
    }
```

Then, in order:

1. `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p22\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release --filter "FullyQualifiedName~RunAsync_WhenCancelledDuring"` must report **2 passed**. If either fails, stop, run `git restore -- tests/Dhadgar.Agent.Windows.Uat.Tests/RenewalLegsTests.cs` and report `failed` with the assertion message.
2. `git status --porcelain` must list only ` M tests/Dhadgar.Agent.Windows.Uat.Tests/RenewalLegsTests.cs` and `?? docs/analysis/sprint2-agent-nodes/`.
3. Commit that one file with an explicit path:

```text
test(uat): pin the stop after a cancel in U6 and U8 (P22 ruling 1, security N1)
```

## Step 1: mutation checks for the pass-1 tests and the step 1A tests (at the step 1A commit, before step 2)

For each row, in order:

1. Make the mutation.
2. Run `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p22\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release --filter "FullyQualifiedName~<test>"`. It must report **1 failed**. Record the failing assertion's message.
3. Run `git restore -- <file>`. `git status --porcelain -- <file>` must then print nothing.
4. Re-run the same filter. It must report **1 passed**.

If a mutation leaves its test green, stop. Restore the file and report `failed`, naming the row. Do not change a test to make it go red.

| Row | Test (filter) | File | Mutation | Expected red |
| --- | --- | --- | --- | --- |
| M1 | `RunAsync_WhenCancelledDuringU8_StillRecordsACertificateStoredAfterTheStop` | `tests/Dhadgar.Agent.Windows.Uat/Scenarios/RenewalLegs.cs` | Line 597, in the U8 `finally`: `StopAsync(CancellationToken.None)` becomes `StopAsync(ct)` | `Assert.NotEqual(inMyAtU8Start, stored)`: the certificate from U6's stop is still alone in `My` |
| M1b | `RunAsync_WhenCancelledDuringU6_StillRecordsACertificateStoredAfterTheStop` | same as M1 | Line 362, in the U6 `finally`: `StopAsync(CancellationToken.None)` becomes `StopAsync(ct)` | `Assert.NotEqual(c1Sha1, stored)`: C1 is still alone in `My` |
| M2 | `U3_InServiceMode_FailsWhenTheDecoyIsStillInRoot` | `tests/Dhadgar.Agent.Windows.Uat/Scenarios/AgentLegs.cs` | Delete lines 401-407: the two comment lines and the whole `if (_options.DecoyThumbprint is not null ...) { return Unmet("the decoy CA from U10 ..."); }` block | `Assert.Contains("decoy CA from U10", ...)`: the error now names "no agent CA is in LocalMachine\Root" |
| M3 | same as M2 | same as M2 | Cut lines 401-407 and paste them directly after the closing brace of `if (_deps.Store.AgentCasInRoot().Count != 0) { ... }` (old line 412) | Same assertion as M2 |
| M4 | `RemoveMatchedCertificate_TlsLeafAndDecoy_AreRemoved` | `tests/Dhadgar.Agent.Windows.Uat/Service/UatTlsCertificate.cs` | Line 72: `O=Meridian Console UAT {runId}` becomes `O=Other {runId}` | `Assert.True(result.IsSuccess, result.Error)` at line 80, with "not an agent or UAT certificate" |
| M5 | same as M4 | `tests/Dhadgar.Agent.Windows.Uat/Service/DecoyCa.cs` | Line 57: `CN=Meridian Console Agent CA` becomes `CN=Other CA` | Same assertion as M4, for the decoy |

M1 and M1b change the same file. Restore it after M1 and before M1b. `StopAsync(CancellationToken.None)` occurs twice in `RenewalLegs.cs`, so a text edit must include the next lines to hit one site: for M1 the match ends at `state.Evidence["U8.agentStop"]`, for M1b at `state.Evidence["U6.agentStop"]`.

If M1 or M1b stays green, the rule above applies: stop and report `failed`. Do not change the step 1A tests again.

M2 and M3 change the same file. Restore it after M2 and before M3.

## Step 2: the edit

In `WindowsMachineActions.AddCertificate`, insert the guard as the first statement. Everything after it stays unchanged, including the `Enum.TryParse` block, the `HasPrivateKey` refusal and the `try` block:

```csharp
    /// <inheritdoc />
    public Result AddCertificate(string storeLocation, string storeName, X509Certificate2 certificate)
    {
        // P22 (D-S240): the TLS leaf and the decoy CA are the only machine certificate additions the UAT makes,
        // so every other store is refused before anything is parsed or opened.
        if (!string.Equals(storeLocation, "LocalMachine", StringComparison.Ordinal)
            || !string.Equals(storeName, "Root", StringComparison.Ordinal))
        {
            return Result.Failure($"refusing to add a certificate to {storeLocation}\\{storeName}; only LocalMachine\\Root is allowed");
        }

        if (!Enum.TryParse<StoreLocation>(storeLocation, out var location))
        // ... unchanged from here
```

## Step 3: the test

Add this method to `WindowsMachineActionsTests`, after `GetProcessImage_NoSuchPid_ReturnsNotRunning`. The file already has the `using` directives it needs:

```csharp
    [SkippableFact]
    public void AddCertificate_NonRootStore_IsRefused()
    {
        // D-S240. The certificate keeps its private key on purpose: if the store guard is ever removed, the existing
        // private-key refusal still returns before any store is opened, so this test can never change the machine.
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=meridian-uat-addcert-test", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.True(certificate.HasPrivateKey);
        var actions = new WindowsMachineActions();

        foreach (var (location, store) in new[]
                 {
                     ("CurrentUser", "Root"),
                     ("LocalMachine", "TrustedPeople"),
                     ("LocalMachine", "AuthRoot"),
                     ("LocalMachine", "My"),
                     ("localmachine", "root"),
                 })
        {
            var result = actions.AddCertificate(location, store, certificate);

            Assert.True(result.IsFailure, $"{location}\\{store}");
            Assert.Contains("only LocalMachine\\Root is allowed", result.Error, StringComparison.Ordinal);
        }
    }
```

Do **not** add a test that calls `AddCertificate("LocalMachine", "Root", ...)` with any certificate. With a regression in the private-key refusal, that test would add to the machine's `Root` in an elevated session.

## Step 4: checks, in order

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p22\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true --no-incremental` must report 0 errors. The IL2026 warnings from `Dhadgar.Agent.Core` are known (README watch-outs) and do not count.
2. `dotnet test` on the same project with `-c Release` must report **204 passed** (202 + the U6 twin from step 1A + the step 3 test), 0 failed and 0 skipped.

## Step 5: commit

Commit only the two files from steps 2 and 3, with explicit paths (`RenewalLegsTests.cs` is already committed in step 1A):

```text
fix(uat): add certificates only to LocalMachine\Root (P22 ruling 1, D-S240)
```

## Step 6: mutation checks for the new test (after the commit)

Use the same procedure as step 1, with the filter `AddCertificate_NonRootStore_IsRefused`. `git restore -- tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs` brings back the committed guard.

| Row | Mutation in `AddCertificate` | Expected red |
| --- | --- | --- |
| M6 | Delete the whole guard `if` block | `Assert.Contains("only LocalMachine\\Root is allowed", ...)`: the private-key refusal answers instead, and no store is opened |
| M7 | Change the guard's `\|\|` to `&&` | Same assertion, on the first case (`CurrentUser\Root`) |

Never mutate the `HasPrivateKey` refusal. With that refusal gone, M6 would open real stores.

After M7, `git status --porcelain` must again list only `?? docs/analysis/sprint2-agent-nodes/`, and `HEAD` must be the step 5 commit.

## Report

The coder's report contains:

- the step 1A and step 5 commit SHAs;
- a table with M1, M1b and M2-M7: the row, the test, red or green under mutation with the failing message, and green after restore;
- the build and test totals from step 4.

## After the apply

- The orchestrator runs packet-reviewer pass 3 against this note (range `20b32cd..<new head>`). The reviewer checks that the diff touches only the three permitted places and that the report shows all eight mutations red.
- security-advisor runs pass 2 on `705a615..<new head>`. It confirms security N1 through the M1 and M1b results and reviews the guard. The advisories A1-A4 from pass 1 stay advisories.
- Still owed to the Service-mode UAT run, and not part of this apply: the two P22 checks on the real layout from [P20-ruling-1](P20-ruling-1.md) § "Effect on later packets". The elevated driver must delete the agent's machine key through its certificate, and the agent must create nothing under `%ProgramData%\Meridian Console` except `Agent`. The UAT runner's step 6 footprint check covers both.
- Docs owed: none. `IMachineActions.AddCertificate`'s summary already names `LocalMachine\Root` as P22's use.
- The PR keeps its `security-review` label (P22 touches `LocalMachine\Root`, a LocalSystem service and a PFX with a private key).
