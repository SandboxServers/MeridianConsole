# P20 ruling 1: the agent's key goes with its certificate, the parent key is undone only when empty, and the Temp entry is dropped

- **Packet** P20 (UAT agent legs U2-U4 and U9, process mode). **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p20`, branch `s2/p20-uat-process`, head `40ec06d`, base `08275a1`.
- **Trigger** pass 1 ended `decision` (P20-review-1 D1). P20-security-1 ended `fix` with N1-N6.
- **Inputs** P20-review-1 (N1-N8, D1-D5) and P20-security-1 (N1-N6) under the worktree's `.claude\reports\sprint2-agent-nodes\`. Spec: P20 (with A1) in [work-packets.md](../work-packets.md), D-S232 to D-S237, [P18-ruling-1](P18-ruling-1.md).
- **Decision** [D-S238](../README.md#decisions) (new). No owner decision blocks; see "Owner awareness" at the end.

## Ruling

**Re-spec, in this packet.** Both reviews found the same defect class from different sides: P20 appends three manifest entries that the P18 allowlist refuses (`CleanupEngine.Reject`, `DeleteGuarded`). On the user's first `-Mode Process` run, U9 would fail with undo failures, the status would be `footprint-left` and `cleanup` could not recover, because it refuses the same entries. No test runs P20's entries through the real engine. This ruling decides the three entries, adds that test, and folds in every other fix note from both reports.

### Finding R1 (D1, security N1): the agent's CNG key cannot match the name rule

The agent imports its PFX with `MachineKeySet | PersistKeySet` (`src/Agents/Dhadgar.Agent.Windows/Windows/WindowsCertificateStore.cs:472-495`), so Windows picks the key container name. The agent never names the key, so its name will never start with `meridian-uat-`. `Reject` refuses the `cngKey` entry. `X509Store.Remove` does not delete the key, so the machine key stays in `C:\ProgramData\Microsoft\Crypto\Keys`.

**Decision (D-S238 (1)): delete the agent's key through its certificate, not by name.** The `cngKey` name rule is **not** loosened.

- `RemoveCertificate`, for a certificate in `My` that passes `AgentCertificateMatcher.IsAgentClientIdentity`, opens the certificate's own private key, checks its provider and that it is a machine key, deletes the key, and only then removes the certificate.
- `AgentLegs` stops appending `cngKey` entries. The `certificate` entry is the only machine entry for the agent's certificate.
- **Why this does not widen trust.** A `certificate` entry could already remove any agent client certificate from `LocalMachine\My` (P18). The key reached through such a certificate is the agent's own key: it is bound to a certificate issued under the agent CA name with a Nodes SPIFFE SAN. Preflight requires no agent certificate in `My` at the start, so any agent certificate there was created by the run. A planted manifest can still only reach agent certificates and their keys, which is what it could reach before. Loosening the name rule to "any machine key under the two providers" would let a planted manifest delete arbitrary machine keys. P18 closed that, and it stays closed.
- **Order.** The key is deleted first, then the certificate. If the key delete fails, the certificate stays and the undo fails, so a rerun of `cleanup` finds the key again through it. If the key is already gone (`NTE_BAD_KEYSET`, `NTE_NOT_FOUND`), that counts as success and the certificate is removed.
- **Accepted gap.** A key the agent created but never linked to a certificate in `My` is neither cleaned nor probed. The agent creates the key and imports the certificate together (`ExportAndReimportCertificate`), so there is no such key on the success path. P21 checks the C1 key itself (U6, P14 A2).
- The `cngKey` kind and its rule stay for driver-named `meridian-uat-*` keys.

### Finding R2 (review N5, security N2): the registry parent key is refused

`RegistryToken.Write` appends `registryKey HKLM\SOFTWARE\Meridian Console` as the spec asks (step 2), but `IsAgentKey` accepts only `...\Agent` and its children. On a clean machine the undo is refused, and the parent key the driver created is left behind. The probe does not report it.

**Decision (D-S238 (2)): allow exactly the parent path, and undo it only when it is empty.**

- `Reject` accepts `HKLM` plus `IsAgentKey(path)` **or** ordinal-ignore-case equality with `AgentWindowsFacts.RegistryParentKeyPath`. There is no prefix match, so `SOFTWARE\Meridian Console2`, `SOFTWARE\Meridian Console\Other` and a trailing backslash are refused.
- The parent is undone through a new `IMachineActions.DeleteEmptyRegistryKey`, which deletes the key only when it has no values and no subkeys. It never calls `DeleteSubKeyTree` on the parent, so a key another product wrote into later is not wiped. A non-empty parent the run created is a failure that names it. That is honest: the run created it and is leaving it.
- The `...\Agent` key keeps its tree delete. `IsAgentKey` is unchanged.

### Finding R3 (review N6, security N3): the Temp entry is a spec error

P20 step 3 names `%ProgramData%\Meridian Console\Temp`. That was the shipped `Files:TempDirectory` before P26 fixed it (F24, merged in w1); it is no longer in the agent's layout: `appsettings.json` sets `Temp` to `C:\ProgramData\Meridian Console\Agent\Temp`, and `Package.wxs` puts `Servers` and `Temp` under `AgentDataFolder` (`...\Agent`). The recursive delete of `AgentDirectories.MachineAgentDirectory` already covers the agent's machine Temp. In process mode `AgentPublishConfig` points `TempDirectory`, `ServerBasePath` and `StateDirectory` at the run folder anyway.

**Decision (D-S238 (3)): drop the Temp entry.** `DeleteGuarded`, `AgentDirectories` and `UatRunner.RequiresElevation` are **not** widened. Both reports' suggestion to add `MachineTempDirectory` is not taken: it would add a machine path that no producer creates. No `directory` entry for `%ProgramData%\Meridian Console` is added either, because the agent creates nothing there in process mode. P22 re-checks this for service mode (see "Effect on later packets").

### Finding R4 (security N1 verify, review N8): no test ran P20's entries through the real engine

Each producer now exposes its entries as an `internal static` list that `Write` and `Start` append, and the new `AgentCleanupEntriesTests` pushes every one of them, in real append order, through the real `CleanupEngine`. A packet that later changes a producer changes the list the test reads, so this seam cannot drift silently again.

### Notes not taken

- Security N3 and review N6 (add `MachineTempDirectory` and widen `DeleteGuarded`): replaced by R3.
- Security N1 (b) (append `cngKey` only when the name starts with `meridian-uat-`): replaced by "never append `cngKey` from `AgentLegs`". The agent never creates a driver-named key, so a conditional append would be dead code.
- Review N8's entry list includes `MachineTempDirectory` and builds entries by hand. Replaced by the producer lists (R4), so the test cannot pass while a producer appends something else.
- Review D2: the `security-review` label stays (campaign PR). D3, D4 and D5: no change, as both reports say.
- Security "not fixed" (registry token window, lexical `RunPaths.Validate`, U9 scanning `results.*` before `PersistAsync`): closed by D-S237 (3) or the spec. The `SecretScan.Run` enumeration outside `try` **is absorbed** (step 12), because it is two lines and protects the `finally`.

## Implementer spec (packet-coder, apply in `s2/p20-uat-process`)

**Repo rules for this apply:**

- `Result`/`Result<T>` for every IO, registry and crypto failure. **Never `Result<T>.Success(null)`** and no `Result<X?>` (D-S232, D-S237 R-NONULL).
- `using` on every `IDisposable`: `X509Store`, `X509Certificate2`, `ECDsa`, `CngKey`, `RegistryKey`, `FileStream`, `StreamReader`, `ChildProcess`, `AgentProcess`.
- No change under `src/`. No package versions in any `.csproj`.
- Keep the P18 allowlist default-deny. The only new allow is the exact parent registry path (step 2). Never add a prefix match.
- xUnit v2, plain `Assert`, names `Subject_Condition_Expectation`. Windows-only tests are `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test")`. Tests that start child processes carry `[Collection("Console")]`.
- Tests touch only `CurrentUser` keys and stores, `HKCU`, and temp folders. Never `LocalMachine`, never `HKLM`.
- **Never** run `run`, `-CleanupOnly` or `-AcceptMachineChanges` (D-S225). Only `-StackOnly`.
- Nullable is on and CI builds with warnings as errors.

### Step 1: `tests/Dhadgar.Agent.Windows.Uat/AgentWindowsFacts.cs`

Add, after `RegistryKeyPath`:

```csharp
/// <summary>Parent of the agent's key. Cleanup deletes it only when the run created it and it is empty (D-S238).</summary>
public const string RegistryParentKeyPath = @"SOFTWARE\Meridian Console";
```

### Step 2: `tests/Dhadgar.Agent.Windows.Uat/Cleanup/CleanupEngine.cs`

1. Replace the `RegistryKeyEntry` arm of `Reject` (line 109) with:

   ```csharp
   RegistryKeyEntry k => string.Equals(k.Hive, HklmHive, StringComparison.Ordinal)
       && (IsAgentKey(k.Path) || IsParentKey(k.Path)),
   ```

2. Add next to `IsAgentKey`:

   ```csharp
   /// <summary>Exactly the parent key; no prefix match, so siblings and children of the parent are refused (D-S238).</summary>
   private static bool IsParentKey(string path) =>
       path.Equals(AgentWindowsFacts.RegistryParentKeyPath, StringComparison.OrdinalIgnoreCase);
   ```

3. In `Undo`, insert **before** the existing `RegistryKeyEntry k when !k.ExistedBefore` arm:

   ```csharp
   RegistryKeyEntry k when !k.ExistedBefore && IsParentKey(k.Path) =>
       ("registryKey", $"{k.Hive}\\{k.Path}", _actions.DeleteEmptyRegistryKey(k.Hive, k.Path)),
   ```

4. Leave the `CngKeyEntry` arms (lines 117-119 and 175) and `DeleteGuarded` unchanged. Update the `Reject` summary to read: "... Add a new kind here when a packet adds one. The agent's own CNG key is deleted through its certificate, never by name (D-S238)."

### Step 3: `tests/Dhadgar.Agent.Windows.Uat/Cleanup/IMachineActions.cs`

Add after `DeleteRegistryKey`:

```csharp
/// <summary>
/// Deletes the key only when it has no values and no subkeys. Missing counts as success; a non-empty key is a
/// failure and is left in place (D-S238).
/// </summary>
Result DeleteEmptyRegistryKey(string hive, string path);
```

### Step 4: `tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs`

1. Add `DeleteEmptyRegistryKey` and its testable core:

   ```csharp
   /// <inheritdoc />
   public Result DeleteEmptyRegistryKey(string hive, string path)
   {
       if (!string.Equals(hive, "HKLM", StringComparison.Ordinal))
       {
           return Result.Failure($"unsupported hive {hive}");
       }

       using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
       return DeleteEmptySubKey(hklm, path);
   }

   /// <summary>Deletes <paramref name="path"/> under <paramref name="baseKey"/> only when it is empty. Tests pass HKCU.</summary>
   internal static Result DeleteEmptySubKey(RegistryKey baseKey, string path)
   {
       try
       {
           using (var key = baseKey.OpenSubKey(path, writable: false))
           {
               if (key is null)
               {
                   return Result.Success(); // Already gone.
               }

               if (key.SubKeyCount > 0 || key.ValueCount > 0)
               {
                   return Result.Failure($"registry key {path} is not empty; left in place");
               }
           }

           baseKey.DeleteSubKey(path, throwOnMissingSubKey: false);
           return Result.Success();
       }
       catch (InvalidOperationException)
       {
           return Result.Failure($"registry key {path} is not empty; left in place"); // A subkey appeared after the check.
       }
       catch (UnauthorizedAccessException ex)
       {
           return Result.Failure($"registry delete denied: {ex.Message}");
       }
       catch (System.Security.SecurityException ex)
       {
           return Result.Failure($"registry delete denied: {ex.Message}");
       }
       catch (IOException ex)
       {
           return Result.Failure($"registry delete failed: {ex.Message}");
       }
   }
   ```

2. Replace the loop body in `RemoveCertificate` (lines 217-226) so each match goes through a testable helper:

   ```csharp
   foreach (var cert in matches)
   {
       var removed = RemoveMatchedCertificate(cert, storeName, location == StoreLocation.LocalMachine, store.Remove);
       if (removed.IsFailure)
       {
           return removed;
       }
   }
   ```

3. Add the two helpers below `IsDriverCertificate`:

   ```csharp
   private const string MyStoreName = "My";
   private const string KeyStorageProviderName = "Microsoft Software Key Storage Provider";
   private const string PlatformCryptoProviderName = "Microsoft Platform Crypto Provider";

   /// <summary>
   /// Removes one matched certificate. An agent client certificate in <c>My</c> has its own private key deleted first
   /// (the agent's key has a Windows-assigned name, so it is reached through its certificate, never by name; D-S238).
   /// If the key delete fails, the certificate stays so a rerun can find the key again.
   /// </summary>
   internal static Result RemoveMatchedCertificate(
       X509Certificate2 cert, string storeName, bool requireMachineKey, Action<X509Certificate2> remove)
   {
       // A forged thumbprint in the manifest must not remove an unrelated certificate.
       if (!IsDriverCertificate(cert))
       {
           return Result.Failure($"certificate {cert.Thumbprint} is not an agent or UAT certificate; not removed");
       }

       if (string.Equals(storeName, MyStoreName, StringComparison.Ordinal)
           && AgentCertificateMatcher.IsAgentClientIdentity(cert))
       {
           var keyDeleted = DeleteCertificateKey(cert, requireMachineKey);
           if (keyDeleted.IsFailure)
           {
               return Result.Failure($"certificate {cert.Thumbprint} kept: {keyDeleted.Error}");
           }
       }

       remove(cert);
       return Result.Success();
   }

   /// <summary>
   /// Deletes the persisted CNG key of <paramref name="cert"/>. Only an ECDSA CNG key under one of the two Microsoft
   /// providers is deleted, and only a machine key when <paramref name="requireMachineKey"/> is true. A key that is
   /// already gone counts as success.
   /// </summary>
   internal static Result DeleteCertificateKey(X509Certificate2 cert, bool requireMachineKey)
   {
       if (!cert.HasPrivateKey)
       {
           return Result.Success();
       }

       try
       {
           using var ecdsa = cert.GetECDsaPrivateKey();
           if (ecdsa is not ECDsaCng cng)
           {
               return Result.Failure("private key is not an ECDSA CNG key; not deleted");
           }

           using var key = cng.Key;
           var provider = key.Provider?.Provider;
           if (provider is not (KeyStorageProviderName or PlatformCryptoProviderName))
           {
               return Result.Failure($"private key provider {provider ?? "(none)"} is not allowed; not deleted");
           }

           if (requireMachineKey && !key.IsMachineKey)
           {
               return Result.Failure("private key is not a machine key; not deleted");
           }

           key.Delete();
           return Result.Success();
       }
       catch (CryptographicException ex) when (ex.HResult is HResultBadKeyset or HResultNotFound)
       {
           return Result.Success(); // Key does not exist: already gone.
       }
       catch (CryptographicException ex)
       {
           return Result.Failure($"certificate key delete failed: 0x{ex.HResult:X8} {ex.Message}");
       }
   }
   ```

   `IsDriverCertificate` stays `private static`. The helpers are `internal static` so `WindowsMachineActionsTests` reaches them (`InternalsVisibleTo` exists).

4. If `KeyStorageProviderName` or `PlatformCryptoProviderName` already exist under another name in this class, reuse them instead of adding new ones. Do not reference `CleanupEngine`'s private constants.

### Step 5: `tests/Dhadgar.Agent.Windows.Uat/Agent/RegistryToken.cs`

1. `ParentKeyPath` becomes `public const string ParentKeyPath = AgentWindowsFacts.RegistryParentKeyPath;`. `AgentKeyPath` becomes `AgentWindowsFacts.RegistryKeyPath`, and `TokenValueName` becomes `AgentWindowsFacts.EnrollmentTokenValueName`. The values are unchanged; this keeps one source.
2. Add the producer list, and make `Write` append exactly that list in order (one loop, early return on the first failed append, then `CreateSubKey` and `SetValue` as today):

   ```csharp
   /// <summary>The entries <see cref="Write"/> appends, in order, before it changes the registry (R-CLEANUP, D-S238).</summary>
   internal static IReadOnlyList<CleanupEntry> ManifestEntries(bool parentExisted, bool agentExisted, bool valueExisted) =>
   [
       new RegistryKeyEntry(HklmHive, ParentKeyPath, parentExisted),
       new RegistryKeyEntry(HklmHive, AgentKeyPath, agentExisted),
       new RegistryValueEntry(AgentKeyPath, TokenValueName, valueExisted),
   ];
   ```

### Step 6: `tests/Dhadgar.Agent.Windows.Uat/Agent/AgentProcess.cs`

1. Remove the Temp entry (R3). Replace `WriteFirstStartEntries`' list with a producer list:

   ```csharp
   /// <summary>
   /// The entries the agent may cause on its first start, in order: its event source, its CA store (absent by preflight)
   /// and the machine agent folder (which holds the agent's machine Temp and Servers; D-S238).
   /// </summary>
   internal static IReadOnlyList<CleanupEntry> FirstStartEntries(bool eventSourceExisted, bool agentDirectoryExisted) =>
   [
       new EventSourceEntry(AgentWindowsFacts.EventLogSourceName, eventSourceExisted),
       new CertStoreEntry(AgentWindowsFacts.CaStoreName, ExistedBefore: false),
       new DirectoryEntry(AgentDirectories.MachineAgentDirectory, agentDirectoryExisted),
   ];
   ```

   `WriteFirstStartEntries` appends `FirstStartEntries(SafeEventSourceExists(AgentWindowsFacts.EventLogSourceName), Directory.Exists(AgentDirectories.MachineAgentDirectory))` in a loop. Fix the summary ("... and the machine agent folder").

2. Add a seam constructor, and have the public constructor chain to it:

   ```csharp
   private readonly ChildSpec _spec;

   public AgentProcess(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest)
       : this(paths, job, redactor, manifest, DefaultSpec(paths))
   {
   }

   /// <summary>Test seam: runs <paramref name="spec"/> instead of the published agent.</summary>
   internal AgentProcess(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest, ChildSpec spec)
   {
       _paths = paths;
       _job = job;
       _redactor = redactor;
       _manifest = manifest;
       _spec = spec;
   }

   private static ChildSpec DefaultSpec(RunPaths paths) => new(
       Name: "agent",
       FileName: Path.Combine(paths.AgentPublish, ExecutableName),
       Arguments: [],
       WorkingDirectory: paths.AgentPublish,
       Environment: new Dictionary<string, string>(StringComparer.Ordinal)
       {
           ["DOTNET_ENVIRONMENT"] = "Production",
       });
   ```

   `Start` passes `_spec` to `ChildProcess.Start`.

3. `StopAsync` (review N1) disposes the child after a successful stop, so `agent.log` is closed for the next start and for U9:

   ```csharp
   public async Task<Result> StopAsync()
   {
       if (_child is null)
       {
           return Result.Success();
       }

       var stopped = await _child.StopAsync();
       if (stopped.IsSuccess)
       {
           _child.Dispose(); // closes agent.log so the next start and the U9 scan can open it
           _child = null;
       }

       return stopped;
   }
   ```

   `ProcessId` returns null after a stop, which is correct.

4. `SawRecoveryWarning` (review N2, security N4) reads with `FileShare.ReadWrite`, so it works while the agent runs:

   ```csharp
   using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
   using var reader = new StreamReader(stream);
   string? line;
   while ((line = reader.ReadLine()) is not null)
   {
       if (line.Contains(RecoveryWarningText, StringComparison.Ordinal))
       {
           return Result<bool>.Success(true);
       }
   }

   return Result<bool>.Success(false);
   ```

   Keep the `File.Exists` check and both `catch` arms.

### Step 7: `tests/Dhadgar.Agent.Windows.Uat/Infra/ChildProcess.cs`

Line 90: `append: false` becomes `append: true` (review N1), so U3's agent log survives the U4 restart. Nothing else changes.

### Step 8: `tests/Dhadgar.Agent.Windows.Uat/Scenarios/StackScenario.cs`

Review N3, security N4: lines 264-276 declare the agent as a disposed local, before `AgentLegsDeps`:

```csharp
using var agentProcess = new AgentProcess(context.Paths, job, context.Redactor, context.Manifest);
var deps = new AgentLegsDeps(
    new RegistryToken(context.Manifest),
    new StoreInspector(),
    agentProcess,
    new NodesAgentLegsDb(() => nodesDb.Create()),
    (label, c) => minter.MintAsync(organizationId, label, c));
```

Keep the existing argument order of `AgentLegsDeps`; only the inline `new AgentProcess(...)` moves. The `return await ...RunAsync(...)` stays inside the method, so the dispose runs before `RunVerbAsync`'s `finally` scans.

### Step 9: `tests/Dhadgar.Agent.Windows.Uat/Scenarios/AgentLegs.cs`

1. **R1.** Delete `_recordedKeys` and the `CngKeyEntry` block in `RecordMachineCertificates`. Add the producer list and use it:

   ```csharp
   /// <summary>The entries recorded for one agent certificate. Its key is deleted with it, through the certificate (D-S238).</summary>
   internal static IReadOnlyList<CleanupEntry> CertificateEntries(StoreCert certificate) =>
       [new CertificateEntry(MachineStoreLocation, MachineStoreName, certificate.Sha1Thumbprint)];
   ```

   `RecordMachineCertificates` appends `CertificateEntries(certificate)` when `_recordedThumbprints.Add(...)` is true. Its summary becomes "Appends the `certificate` entry for every agent certificate seen, before any check uses it; the key goes with the certificate (D-S238)."

2. **Review N4.** Record first, in both evaluators:
   - `EvaluateFirstStartAsync`: the first statements are `var mine = _deps.Store.AgentCertificatesInMy();` and `var recorded = RecordMachineCertificates(mine);` with its failure return. Delete the later `AgentCertificatesInMy()` call and record (lines 322-327) and reuse `mine` below.
   - `EvaluateRestartAsync`: the same at the top. Delete lines 513-518 and reuse `mine`.

3. **Review N2 and N4: the `finally` in `RunAsync`** becomes, in this order:

   ```csharp
   finally
   {
       var stopped = await _deps.Agent.StopAsync();
       if (stopped.IsFailure)
       {
           warnings.Add($"agent stop: {stopped.Error}");
       }

       var warning = _deps.Agent.SawRecoveryWarning();
       if (warning.IsFailure)
       {
           warnings.Add($"agent.log: {warning.Error}");
       }
       else if (warning.Value)
       {
           warnings.Add("agent.log: service recovery could not be configured (expected in process mode)");
       }

       // A certificate the agent created after the last poll is recorded now, so cleanup removes it and its key.
       try
       {
           var recorded = RecordMachineCertificates(_deps.Store.AgentCertificatesInMy());
           if (recorded.IsFailure)
           {
               warnings.Add($"certificate record: {recorded.Error}");
           }
       }
       catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or IOException)
       {
           warnings.Add($"certificate record: {ex.GetType().Name}");
       }
   }
   ```

   Add `using System.Security.Cryptography;` if missing.

4. **Review N7 (R-NONULL).** `IAgentLegsDb.TokenAsync` becomes `Task<Result<TokenRow>> TokenAsync(Guid tokenId, CancellationToken ct)`. In `NodesAgentLegsDb.TokenAsync` (lines 95-110), a missing row returns `Result<TokenRow>.Failure("[Db.EnrollmentTokens] token not found")` and a found row returns `Result<TokenRow>.Success(row)`. Callers:
   - line 312: `if (token.Value.UsedAt is null || token.Value.UsedByNodeId != nodeId)`;
   - line 486: `if (token.Value.UsedAt is not null)`.

### Step 10: `tests/Dhadgar.Agent.Windows.Uat/Agent/AgentPublishConfig.cs`

Security N5. Replace the endpoint check at lines 70-73:

```csharp
if (config.Endpoint is not { IsAbsoluteUri: true } endpoint
    || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
    || !string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
    || endpoint.IsDefaultPort
    || endpoint.Port <= 0
    || endpoint.UserInfo.Length > 0)
{
    return Result.Failure("[AgentConfig.Endpoint] endpoint must be https://localhost:<port> (R-LOCALURL)");
}
```

The agent reaches the stack through the Gateway on `https://localhost:<port>` in every mode (D-S222), so `http://127.0.0.1` is not allowed for the agent endpoint.

### Step 11: `tests/Dhadgar.Agent.Windows.Uat/Probe/StoreInspector.cs`

- Security N6, in `ChainsTo`: add `chain.ChainPolicy.DisableCertificateDownloads = true;`.
- In `DescribeKey`: read the key once, `using var key = cng.Key;`, and build `CngKeyInfo` from `key` (`key.KeyName ?? string.Empty`, `key.Provider?.Provider ?? string.Empty`, `key.IsMachineKey`). Each `cng.Key` access returns a new handle.

### Step 12: `tests/Dhadgar.Agent.Windows.Uat/Results/SecretScan.cs`

Move `var failures = new List<string>();` above the enumeration, and wrap it:

```csharp
if (Directory.Exists(paths.Logs))
{
    try
    {
        files.AddRange(Directory.EnumerateFiles(paths.Logs, "*", SearchOption.AllDirectories));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        failures.Add($"logs: could not be listed ({ex.GetType().Name})");
    }
}
```

### Step 13: `tests/Dhadgar.Agent.Windows.Uat.Tests/RecordingActions.cs`

Add `public Result DeleteEmptyRegistryKey(string hive, string path) => Record($"DeleteEmptyRegistryKey:{hive}\\{path}");`.

### Step 14: test changes

| File | Test | What it pins (fails when reverted) |
| --- | --- | --- |
| `AgentCleanupEntriesTests.cs` (new) | `UndoAsync_EveryEntryP20Appends_IsUndoneWithoutFailure` | the seam: R1, R2, R3 together (spec below) |
| `CleanupEngineTests.cs` | `UndoAsync_RegistryParentKeyNotExistedBefore_DeletesOnlyIfEmpty` | entry `RegistryKeyEntry("HKLM", AgentWindowsFacts.RegistryParentKeyPath, false)`: `Calls` is exactly `["DeleteEmptyRegistryKey:HKLM\SOFTWARE\Meridian Console"]`, `Failed` empty. Fails if the `Reject` allow or the `Undo` parent arm is removed (the second shows as `DeleteRegistryKey:`) |
| `CleanupEngineTests.cs` | `UndoAsync_RegistryParentKeyExistedBefore_IsSkipped` | same entry with `true`: `Calls` empty, `Failed` empty, `Undone == 0` |
| `CleanupEngineTests.cs` | `UndoAsync_RegistryKeyNearParent_IsRefused` (`[Theory]`) | `@"SOFTWARE\Meridian Console2"`, `@"SOFTWARE\Meridian Console Other"`, `@"SOFTWARE\Microsoft"`, `"SOFTWARE"`, `@"SOFTWARE\Meridian Console\Other"`, `@"SOFTWARE\Meridian Console\"`, each with `existedBefore: false`: `Calls` empty, one failure whose message contains `refused`. Fails if `IsParentKey` becomes a prefix match |
| `CleanupEngineTests.cs` | `UndoAsync_CngKeyWithWindowsAssignedName_IsRefused` | `CngKeyEntry($"{Guid.NewGuid():N}_{Guid.NewGuid():D}", "Microsoft Software Key Storage Provider", true)` (the shape of a Windows-assigned name): `Calls` empty, one failure. Pins that the name rule was not loosened |
| `WindowsMachineActionsTests.cs` (new, Windows-only) | five key tests | spec below |
| `WindowsMachineActionsTests.cs` | four `DeleteEmptySubKey` tests | spec below |
| `AgentProcessTests.cs` (new, Windows-only, `[Collection("Console")]`) | `StopThenStart_ReopensLogKeepsItAndReadsWhileRunning` | spec below: pins the dispose on stop, `append: true` and `FileShare.ReadWrite` |
| `AgentLegsTests.cs` | `HappyPath_PassesU2ThroughU4` | change `Assert.Equal("agent.stop", world.Calls[^1])` to `Assert.Equal(["agent.stop", "agent.log.read", "store.my"], world.Calls[^3..])` |
| `AgentLegsTests.cs` | `RunAsync_ReadsRecoveryWarningAfterStop` | happy path with `world.RecoveryWarning = Result<bool>.Success(true)`: the index of `agent.log.read` is after the last `agent.stop`, and `warnings` contains the "expected in process mode" text. Fails if the `finally` order is reverted |
| `AgentLegsTests.cs` | `RunAsync_RecoveryWarningReadFails_AddsWarning` | `world.RecoveryWarning = Result<bool>.Failure("[AgentProcess.Log] agent.log could not be read")`: a warning starts with `agent.log: [AgentProcess.Log]`. Fails if the failure is ignored again |
| `AgentLegsTests.cs` | `U3_RecordsCertificateBeforeOtherConditionsHold` | `world.ClearRegistryOnStart = false`, so U3 times out on the registry condition (use the short poll options the file already uses for failing U3 cases). The manifest at `_paths.Manifest` then holds exactly one `CertificateEntry` and no `CngKeyEntry`. Fails if the record moves back after the checks (zero entries) or the `cngKey` append returns |
| `AgentLegsTests.cs` | `U3_FailsWhenTokenRowIsMissing` | `world.OnStart = w => w.ClearTokens()`, so `TokenAsync` misses T1: U3 fails with a message containing `[Db.EnrollmentTokens] token not found`. Before the change it fails with `InvalidOperationException` text |
| `AgentPublishConfigTests.cs` | `Apply_EndpointNotLocalhostHttps_Fails` (`[Theory]`) | `https://example.test:5443/`, `http://localhost:5443/`, `https://localhost/`, `https://user@localhost:5443/`, `https://127.0.0.1:5443/`: each returns a failure whose error starts with `[AgentConfig.Endpoint]` |
| `ManifestOrderTests.cs` | existing | line 53: the expected machine-entry count `2` becomes `1`; line 76: `Assert.Single(...OfType<CngKeyEntry>())` becomes `Assert.Empty(...OfType<CngKeyEntry>())` |

**`LegWorld` changes (in `AgentLegsTests.cs`):**

- `public Result<bool> RecoveryWarning { get; set; } = Result<bool>.Success(false);`. `SawRecoveryWarning()` does `Log("agent.log.read"); return RecoveryWarning;`.
- `TokenAsync` returns `Task<Result<TokenRow>>`: a found row gives `Success(row)`, a missing row gives `Failure("[Db.EnrollmentTokens] token not found")`.
- Add `public void ClearTokens() => _tokens.Clear();` for the missing-row test.
- Update the `Snapshots` and `CountMachineEntries` comments to "certificate lines" (the method may keep counting `cngKey` lines, which must now be zero).

**`AgentCleanupEntriesTests` (the seam test).** Class `AgentCleanupEntriesTests`, using `RunPaths(<temp repo>, "run-1")` as `CleanupEngineTests` does. Build the entries in the order a real process-mode run appends them, using only the producer lists and `ProcessEntry`:

```csharp
var start1 = DateTime.UtcNow.AddMinutes(-5);
var start2 = DateTime.UtcNow.AddMinutes(-1);
var sha1 = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
var certificate = new StoreCert(
    "CN=agent", sha1, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), [AgentCertificateProfile.SpiffeId(Guid.NewGuid())],
    DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), true,
    new CngKeyInfo($"{Guid.NewGuid():N}_{Guid.NewGuid():D}", "Microsoft Software Key Storage Provider", true, true));

var entries = new List<CleanupEntry>();
entries.AddRange(RegistryToken.ManifestEntries(parentExisted: false, agentExisted: false, valueExisted: false)); // U2
entries.AddRange(AgentProcess.FirstStartEntries(eventSourceExisted: false, agentDirectoryExisted: false));       // U3 first start
entries.Add(new ProcessEntry(4242, start1, "agent"));
entries.AddRange(AgentLegs.CertificateEntries(certificate));                                                      // U3 poll
entries.AddRange(RegistryToken.ManifestEntries(parentExisted: true, agentExisted: true, valueExisted: false));   // U4 token T2
entries.Add(new ProcessEntry(4243, start2, "agent"));                                                              // U4 restart

var actions = new RecordingActions();
var agentImage = Path.Combine(_paths.AgentPublish, AgentProcess.ExecutableName);
actions.ProcessImages[4242] = agentImage;
actions.ProcessImages[4243] = agentImage;

var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);
```

Assert `Assert.Empty(report.Failed)`, `Assert.Equal(10, report.Undone)`, and `actions.Calls` equals exactly:

```text
KillProcess:4243
KillProcess:4242
KillProcess:4243
DeleteRegistryValue:SOFTWARE\Meridian Console\Agent\EnrollmentToken
RemoveCertificate:LocalMachine\My\<sha1>
KillProcess:4242
DeleteDirectory:<AgentDirectories.MachineAgentDirectory>
UnregisterCertStore:MeridianConsoleAgent
DeleteEventSource:Meridian Console Agent
DeleteRegistryValue:SOFTWARE\Meridian Console\Agent\EnrollmentToken
DeleteRegistryKey:HKLM\SOFTWARE\Meridian Console\Agent
DeleteEmptyRegistryKey:HKLM\SOFTWARE\Meridian Console
```

The first two lines are the quiesce pass. The U4 registry keys (`existedBefore: true`) are skipped without a call. Build the expected list from the constants (`AgentWindowsFacts.*`, `AgentDirectories.MachineAgentDirectory`), not from string literals. Add one more assert: `Assert.DoesNotContain(entries, e => e is CngKeyEntry)`.

Each revert fails this test:

- without the R2 `Reject` allow, a `registryKey` refusal is in `Failed`;
- with a `cngKey` append restored in `CertificateEntries`, a `cngKey` refusal is in `Failed`;
- with the Temp entry restored in `FirstStartEntries`, a `directory` refusal is in `Failed`.

**`WindowsMachineActionsTests` (new, Windows-only `[SkippableFact]`).** A shared helper creates a real persisted CurrentUser key and an agent-shaped certificate over it:

```csharp
private static (CngKey Key, X509Certificate2 Cert) AgentCertificateWithKey(string keyName, bool agentIdentity = true)
{
    var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, new CngKeyCreationParameters
    {
        Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
        KeyCreationOptions = CngKeyCreationOptions.None,
    });

    using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    var issuerRequest = new CertificateRequest(
        new X500DistinguishedName($"CN={(agentIdentity ? AgentCertificateProfile.CaCommonName : "Some Other CA")}"),
        issuerKey, HashAlgorithmName.SHA384);
    issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddDays(2));

    using var ecdsa = new ECDsaCng(key);
    var request = new CertificateRequest(new X500DistinguishedName("CN=uat-test-agent"), ecdsa, HashAlgorithmName.SHA256);
    var san = new SubjectAlternativeNameBuilder();
    san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(Guid.NewGuid())));
    request.CertificateExtensions.Add(san.Build());
    using var issued = request.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(8));
    return (key, issued.CopyWithPrivateKey(ecdsa));
}
```

The key name is `meridian-uat-test-{Guid.NewGuid():N}`. Every test disposes the certificate and, in `finally`, deletes the key if it still exists (`CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider)` then `CngKey.Open(...).Delete()`, each in `using`). `remove` is a lambda that records the certificate it was given; no test opens a real store.

| Test | Input | Expect |
| --- | --- | --- |
| `RemoveMatchedCertificate_AgentCertificateInMy_DeletesKeyThenRemoves` | agent certificate, `storeName "My"`, `requireMachineKey: false` | success; `CngKey.Exists(name, ...)` is false; `remove` was called once |
| `RemoveMatchedCertificate_MachineKeyRequiredButUserKey_KeepsCertificateAndKey` | same, `requireMachineKey: true` | failure containing `kept` and `not a machine key`; the key exists; `remove` was not called |
| `RemoveMatchedCertificate_AgentCertificateInRoot_KeepsKey` | same certificate, `storeName "Root"` | success; the key exists; `remove` was called |
| `RemoveMatchedCertificate_ForeignCertificate_RefusedAndKeyKept` | `agentIdentity: false` (issuer `CN=Some Other CA`) | failure containing `not an agent or UAT certificate`; the key exists; `remove` not called |
| `RemoveMatchedCertificate_KeyAlreadyDeleted_RemovesCertificate` | delete the key through `CngKey.Open(name, ...)` first, then call with `"My"`, `false` | success; `remove` was called |

For the last test, if this Windows build keeps the key reachable through the certificate after `Delete` (that is, the call succeeds by deleting nothing, or fails with an HResult other than `NTE_BAD_KEYSET` or `NTE_NOT_FOUND`), record the observed HResult in the apply report, add it to the "already gone" filter only if it is `0x80090016`/`0x80090011`-class (key not found), and otherwise drop this one test and say so. Do not widen the catch to every `CryptographicException`.

`DeleteEmptySubKey` tests over `HKCU\Software\MeridianUatTest-{Guid.NewGuid():N}`, opened with `RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)`, with a `finally` that calls `DeleteSubKeyTree(path, throwOnMissingSubKey: false)`:

| Test | Setup | Expect |
| --- | --- | --- |
| `DeleteEmptySubKey_Empty_Deletes` | create the key | success; `OpenSubKey(path)` is null |
| `DeleteEmptySubKey_HasValue_KeepsAndFails` | key with value `x = "1"` | failure containing `not empty`; the key and value exist |
| `DeleteEmptySubKey_HasSubKey_KeepsAndFails` | key with subkey `child` | failure containing `not empty`; the key and the subkey exist |
| `DeleteEmptySubKey_Missing_Succeeds` | nothing created | success |

**`AgentProcessTests` (new, Windows-only, `[Collection("Console")]`).** One test, `StopThenStart_ReopensLogKeepsItAndReadsWhileRunning`:

1. Temp repo `Path.Combine(Path.GetTempPath(), "uat-agentproc-" + Guid.NewGuid().ToString("N"))`; `var paths = new RunPaths(repo, "run-1")`; `Directory.CreateDirectory(paths.RunDir)`; `var manifest = new CleanupManifest(paths.Manifest)`; job from `JobObject.Create()` with `using var jobObject = job.Value`.
2. `using var agent = new AgentProcess(paths, jobObject, new Redactor(), manifest, spec)` where `spec` is the `ChildProcessTests.Spec` shape with `WorkingDirectory: paths.RunDir`: `cmd.exe /c ping -n 30 127.0.0.1`, name `"agent"`.
3. `Assert.True(agent.Start().IsSuccess)`. While it runs: `var read = agent.SawRecoveryWarning(); Assert.True(read.IsSuccess, read.Error); Assert.False(read.Value);` (pins `FileShare.ReadWrite`: with `File.ReadLines` this read fails with a sharing violation).
4. `Assert.True((await agent.StopAsync()).IsSuccess)`. Then `File.AppendAllText(Path.Combine(paths.Logs, "agent.log"), "first-run-marker" + Environment.NewLine)` (pins the dispose on stop: without it this throws `IOException`).
5. `Assert.True(agent.Start().IsSuccess)` (also pins the dispose: without it the second `StreamWriter` fails), then `await agent.StopAsync()`.
6. `Assert.Contains("first-run-marker", File.ReadAllText(Path.Combine(paths.Logs, "agent.log")))` (pins `append: true`).
7. `finally`: `Directory.Delete(repo, recursive: true)`.

The first start appends the first-start entries to this temp manifest only. The test never runs cleanup, so nothing outside the temp folder is touched.

### Step 15: docs

`scripts/uat/README.md`, "Threat model (D-S237)": in the list of items cleanup replays, change "the agent registry key and its `EnrollmentToken` value" to "the agent registry key and its `EnrollmentToken` value, and the `SOFTWARE\Meridian Console` parent key only when the run created it and it is empty". Change "`meridian-uat-*` containers and CNG keys" to "`meridian-uat-*` containers and CNG keys, and the agent's own key, deleted through its certificate in `My` (D-S238)". Rename the heading to "Threat model (D-S237, D-S238)".

## Checks (in order)

```powershell
$wt = 'C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p20'
dotnet build "$wt\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj" -c Release -p:TreatWarningsAsErrors=true
dotnet build "$wt\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj" -c Release -p:TreatWarningsAsErrors=true
dotnet test "$wt\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj" -c Release
git -C $wt grep -n -E "Success\(null\)|Result<[A-Za-z]+\?>" -- tests/Dhadgar.Agent.Windows.Uat tests/Dhadgar.Agent.Windows.Uat.Tests
git -C $wt grep -n "new CngKeyEntry" -- tests/Dhadgar.Agent.Windows.Uat/Scenarios
git -C $wt grep -n -e MachineTempDirectory -e "GetDirectoryName(machineDir)" -- tests/Dhadgar.Agent.Windows.Uat
pwsh -NoProfile -File "$wt\scripts\uat\Invoke-AgentWindowsUat.ps1" -StackOnly
```

- `dotnet test` runs on Windows: report zero skipped among the new Windows-only tests.
- The three `git grep` lines must return nothing.
- `-StackOnly` may refuse (exit 2) until the user runs `dotnet dev-certs https --trust`. Record the exit code and the refusal reason; a refusal for that reason does not block the apply.
- **Never** run `run`, `-CleanupOnly` or `-AcceptMachineChanges`.

## Mutation checks (report each result in the apply report)

Apply each mutation alone, run the named test, confirm it fails, and revert.

| Mutation | Test that must fail |
| --- | --- |
| Remove `\|\| IsParentKey(k.Path)` from `Reject` | `UndoAsync_EveryEntryP20Appends_IsUndoneWithoutFailure`, `UndoAsync_RegistryParentKeyNotExistedBefore_DeletesOnlyIfEmpty` |
| `IsParentKey` uses `StartsWith` | `UndoAsync_RegistryKeyNearParent_IsRefused` |
| Delete the `Undo` parent arm | `UndoAsync_RegistryParentKeyNotExistedBefore_DeletesOnlyIfEmpty` |
| `DeleteEmptySubKey` skips the empty check | `DeleteEmptySubKey_HasValue_KeepsAndFails`, `DeleteEmptySubKey_HasSubKey_KeepsAndFails` |
| Call `remove(cert)` before `DeleteCertificateKey` | `RemoveMatchedCertificate_MachineKeyRequiredButUserKey_KeepsCertificateAndKey` |
| Drop the `requireMachineKey` check | `RemoveMatchedCertificate_MachineKeyRequiredButUserKey_KeepsCertificateAndKey` |
| Drop the `storeName == "My"` condition | `RemoveMatchedCertificate_AgentCertificateInRoot_KeepsKey` |
| Restore the Temp entry in `FirstStartEntries` | `UndoAsync_EveryEntryP20Appends_IsUndoneWithoutFailure` |
| Restore a `CngKeyEntry` in `CertificateEntries` | `UndoAsync_EveryEntryP20Appends_IsUndoneWithoutFailure` |
| Remove the dispose in `AgentProcess.StopAsync` | `StopThenStart_ReopensLogKeepsItAndReadsWhileRunning` |
| `append: false` in `ChildProcess` | `StopThenStart_ReopensLogKeepsItAndReadsWhileRunning` |
| `File.ReadLines` back in `SawRecoveryWarning` | `StopThenStart_ReopensLogKeepsItAndReadsWhileRunning` |
| Old `finally` order (read before stop) | `RunAsync_ReadsRecoveryWarningAfterStop` |
| Record call moved back after the checks | `U3_RecordsCertificateBeforeOtherConditionsHold` |
| Endpoint check back to `IsAbsoluteUri` only | `Apply_EndpointNotLocalhostHttps_Fails` |

## Commit

- One local commit on `s2/p20-uat-process`, explicit paths only. Subject: `fix(uat): agent key removed with its certificate, empty parent key undone, Temp entry dropped (P20 ruling 1, D-S238)`.
- Apply report: `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p20\.claude\reports\sprint2-agent-nodes\P20-ruling-apply-1.md`. It lists each step, the checks output, the mutation table with results, the `-StackOnly` outcome, and the key-already-gone HResult if one was observed. It states "R-CLEANUP: the parent-key rule and its refusal and allowed tests are added; the agent's key is reached through its certificate (D-S238)".
- No push, no PR.

## Review after the apply

- One `packet-reviewer` ruling check against this note: `P20-ruling-check-1.md`.
- One `security-advisor` pass over the apply diff: `P20-security-ruling-1.md`. It checks in particular that no name or path rule became a prefix match, and that `RemoveMatchedCertificate` refuses before it deletes anything.
- If both are `ship`, P20 goes to the merge agent. If either is not `ship`, P20 becomes **BlockedDecision** and comes back to the architect with both reports. There is no second apply loop.

## Effect on later packets (amended in work-packets.md)

- **R-CLEANUP** gains two sentences. The `SOFTWARE\Meridian Console` parent key is allowed by exact path only, and is deleted only when empty. The agent's own CNG key is deleted through its `certificate` entry in `My`, never by a `cngKey` entry.
- **P20 step 3** drops `%ProgramData%\Meridian Console\Temp`. **Step 4** records `certificate` entries only.
- **P21 U6**: "Append C3's `certificate` entry (its key is deleted with it, D-S238)". C1's `certificate` entry stays in the manifest; after U6 its undo finds no certificate and succeeds.
- **P22 (service mode)**: the agent runs as `LocalSystem`. P22 must check two things on the real layout:
  - The elevated driver can open and delete the agent's machine key through the certificate. Access denied is an honest undo failure, not something to swallow.
  - The agent creates nothing directly under `%ProgramData%\Meridian Console` other than `Agent`. If it does, P22 adds that exact path with a refusal test and an allowed test (R-CLEANUP), never a prefix.
- **P23 (MSI)**: same check for MSI-installed folders. The MSI's own uninstall owns its files.

## Owner awareness

No blocking decision. One accepted residual, for the owner to see at security approval: an agent key that never got a certificate in `My` is not cleaned by the UAT. On the success path there is no such key; on a mid-enrollment crash one could remain in `C:\ProgramData\Microsoft\Crypto\Keys`, unreported. The alternative is to probe and delete machine keys by provider and creation time, which would loosen the P18 name rule, so it is refused.
