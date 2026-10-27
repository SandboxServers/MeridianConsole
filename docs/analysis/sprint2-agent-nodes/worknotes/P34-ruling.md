# P34 ruling: the MSI refuses Windows 11, and a second cleanup fails on a file that is gone

Architect ruling, 2026-10-27. Input: the UAT runner's three runs at w1 `6530253` (D-S239 § (2) brief). Decisions **D-S242** and **D-S243**; fix packets **P34** and **P35**.

| Run | Mode | Result |
| --- | --- | --- |
| `20261027-182612-65dd` | Process | pass; S-hub-ws passed over WebSockets |
| `20261027-183248-3fd7` | Service | pass |
| `20261027-183915-637c` | Msi | **fail at U1** (U11a: `msiexec /i` exit 1603); U9 cleanup clean, 16 undone |
| `20261027-184100-d9eb` | Preflight after the Msi run | pass (`footprintEmpty` pass) |

The run folders are under `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1\.uat\agent-windows\`. I only read them; no wrapper or `msiexec` was run.

## (1) The Msi failure: an installer bug

### Evidence

From `20261027-183915-637c\logs\msi-install.log`:

```text
MSI (s) ... PROPERTY CHANGE: Modifying VersionNT property. Its current value is '400'. Its new value: '603'.
MSI (s) ... PROPERTY CHANGE: Modifying VersionNT64 property. Its current value is '400'. Its new value: '603'.
Product: Meridian Console Agent -- Meridian Console Agent requires Windows 10 or later.
Action ended 13:40:26: LaunchConditions. Return value 3.
Property(S): WindowsBuild = 9600
Property(S): VersionNT = 603
Property(S): VersionNT64 = 603
Property(S): Privileged = 1
Property(S): CONTROL_PLANE_ENDPOINT = https://localhost:55204/
Property(S): ENROLLMENT_TOKEN = **********
```

The machine is Windows 11 build 26300: `results.md` says so, and the registry's `CurrentBuildNumber` is `26300`. The failing row is `Installed OR WindowsBuild >= 10240` (`Package.wxs:93-95`), with the message "[ProductName] requires Windows 10 or later."

### Root cause

The bug is in the installer. The UAT driver and the environment are both fine.

- Windows Installer's server session (`MSI (s)`, the msiexec service that runs every `/qn` install) has no Windows 10 compatibility manifest. Windows therefore gives it the Windows 8.1 values, `VersionNT = 603` and `WindowsBuild = 9600`, on every Windows 10 and 11 machine.
- So `WindowsBuild >= 10240` is false on every machine the product supports, and the MSI can never install. It never ran under msiexec before: P26 and P23 only built it (F25). CLAUDE.md's rule ("use `WindowsBuild >= 10240`, not `VersionNT >= 603`") produced this bug, so the rule itself is wrong.
- Everything the driver passed is correct. The endpoint is `https://` (that row is TRUE), `Privileged = 1`, `VersionNT64` is set, and the token is masked. AppSearch's note 1402 for the agent key is the normal "no previous install" case.
- An in-process evaluator **cannot reproduce this**. `OpenPackage` from pwsh reports `WindowsBuild = 26300` and `VersionNT = 1000`, because pwsh is manifested. In process, the old row evaluates TRUE. A test of the old MSI in process proves nothing about the OS row. The proof of the server-side value is the UAT Msi rerun.

### Fix (D-S242)

The build number is read from the registry. AppSearch reads `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber` (REG_SZ, 64-bit view) into the public secure property `WINDOWS_BUILD_NUMBER`. The row becomes `Installed OR WINDOWS_BUILD_NUMBER >= 10240`.

- **The comparison is numeric.** Windows Installer compares a property that holds a numeric string as an integer. An absent value is empty, so the row fails closed.
- **A user cannot spoof it.** AppSearch overwrites any command-line value whenever the registry value exists, which is on every Windows NT 6.x+ machine. A user who forces the property on a machine without the value only bypasses a compatibility check on their own machine. No security boundary depends on this row.
- **The other rows are unchanged.**
  - `VersionNT64` is still set (to 603) on 64-bit Windows, so the 64-bit row stays correct.
  - `Privileged`, the endpoint row and the downgrade row work as before.
- **Executed check (architect, 2026-10-27, Windows 11 build 26300).** I patched a scratch copy of the run's MSI with exactly what the WiX change compiles to: a RegLocator `('WindowsBuildNumberSearch', 2, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'CurrentBuildNumber', 18)` (Type 18 = raw + 64-bit), an AppSearch row and the new LaunchCondition. Then I ran [P34-msi-conditions.ps1](P34-msi-conditions.ps1) against it:

  ```text
  WINDOWS_BUILD_NUMBER = '26300'; registry CurrentBuildNumber = '26300'
  [TRUE] NOT WIX_DOWNGRADE_DETECTED
  [TRUE] REMOVE="ALL" OR NOT CONTROL_PLANE_ENDPOINT OR (CONTROL_PLANE_ENDPOINT ~<< "https://")
  [TRUE] VersionNT64
  [TRUE] Privileged
  [TRUE] Installed OR WINDOWS_BUILD_NUMBER >= 10240
  forced WINDOWS_BUILD_NUMBER = '9600' -> FALSE
  forced WINDOWS_BUILD_NUMBER = '10239' -> FALSE
  forced WINDOWS_BUILD_NUMBER = '10240' -> TRUE
  forced WINDOWS_BUILD_NUMBER = '26300' -> TRUE
  forced WINDOWS_BUILD_NUMBER = '' -> FALSE
  failures = 0
  exit=0
  ```

  The unpatched MSI from the run fails the same script with exit 1 and three findings: the row uses `WindowsBuild`; the row `Installed OR WINDOWS_BUILD_NUMBER >= 10240` is missing; and AppSearch set `WINDOWS_BUILD_NUMBER` to `''`.
- **Remaining risk.** Only the UAT Msi rerun proves that the server session reads `CurrentBuildNumber`. Its MSI log must show `Property(S): WINDOWS_BUILD_NUMBER = 26300`. Registry values are not version-shimmed, so I expect it to pass.

**Rejected alternatives:**

- `VersionNT >= 603`: it cannot tell Windows 8.1 from Windows 10.
- Dropping the row: the agent needs Windows 10 APIs, and failing at install is better than failing at service start.
- A custom action: it adds code to a security-review installer for a one-line registry read.
- `CurrentMajorVersionNumber` (DWORD): a raw DWORD search returns `#10`, which needs string handling in the condition.

## (2) A second `-CleanupOnly` reports `footprint-left`

### Evidence

`20261027-183915-637c\cleanup-results.json`, written by the runner's extra `-CleanupOnly` after U9 had already cleaned the run:

```json
{ "status": "footprint-left", "undone": 15,
  "failed": [ { "kind": "file", "identity": "...\\20261027-183915-637c\\tls\\gateway.pfx",
                "message": "file delete failed: Could not find a part of the path '...\\tls\\gateway.pfx'." } ],
  "remaining": [], "rootAdded": [], "rootRemoved": [] }
```

### Root cause

`WindowsMachineActions.DeleteFile` (`tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs:505-520`) is not idempotent once the file's folder is gone.

- The manifest records `directory ...\tls` (entry 1) before `file ...\tls\gateway.pfx` (entry 2). The engine undoes entries in reverse order, so the first pass deletes the file and then the folder.
- On a second pass, `File.Delete` on a path whose parent folder is missing throws `DirectoryNotFoundException`. That type is a subclass of `IOException`, so the existing `catch (IOException)` turns it into a failure.
- `File.Delete` stays silent only when the file is missing and its folder still exists.

Every other action already treats "already gone" as success:

- `DeleteDirectory` checks `Directory.Exists`.
- `RemoveContainer`, `KillProcess`, `StopService` and `DeleteService` succeed on "no such" errors.
- The registry deletes use `throwOnMissingSubKey: false`.
- `RemoveCertificate` and `UnregisterCertStore` succeed on "not found".

`scripts/uat/README.md:250` already promises "Running cleanup twice is safe."

### Fix (D-S243)

`DeleteFile` returns `Result.Success()` on `DirectoryNotFoundException`. If the folder is gone, the file is gone, so the entry is already undone.

The fix is entirely inside the machine action, which runs only **after** `CleanupEngine.DeleteGuarded` has validated the path. The following do not change:

- `CleanupEngine.cs`, its `Reject` allowlist and `DeleteGuarded`
- `RunPaths.Validate`
- the manifest kinds
- the producers

A missing file outside the run folder is still refused by the engine before any action runs (D-S238 and R-CLEANUP unchanged). The `catch (IOException)` for sharing violations and every other I/O fault stays.

**Rejected alternatives:**

- A `File.Exists` check before the delete: it races with the delete, and it would hide a parent that is a file instead of a folder.
- Changing the order of the engine or the manifest: the engine is out of bounds (D-S238), and order only hides the missing-parent case.
- Treating every `IOException` as success: that would report a locked PFX as undone.

## Decisions

| ID | Decision | Reason |
| --- | --- | --- |
| D-S242 | The MSI decides "Windows 10 or later" from the registry: AppSearch reads `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber` (`Type="raw"`, `Bitness="always64"`) into the public secure property `WINDOWS_BUILD_NUMBER`, and the launch condition is `Installed OR WINDOWS_BUILD_NUMBER >= 10240`. No launch condition uses the `WindowsBuild` property or `VersionNT >=`. This supersedes CLAUDE.md's WiX rule (`WindowsBuild >= 10240`) for this repo; the owner amends CLAUDE.md and the agent and skill files (owner action O1). | The msiexec server reports `WindowsBuild = 9600` and `VersionNT = 603` on Windows 10 and 11, so the old row blocked every supported machine (Msi run `20261027-183915-637c`). The registry value is the real build. Verified in a scratch MSI with the executed evaluator. |
| D-S243 | `WindowsMachineActions.DeleteFile` treats a missing parent folder (`DirectoryNotFoundException`) as already undone. The cleanup allowlist (`CleanupEngine.Reject`, `DeleteGuarded`, `RunPaths.Validate`) does not change and still runs first; any other I/O fault (for example a locked file) still fails. | A second `-CleanupOnly` on a cleaned run reported `footprint-left` because the first pass had removed `tls\` along with `gateway.pfx`. Every other undo action is already idempotent. |

## Packets

Both packets branch from w1 `6530253`. Their file sets are disjoint, so they run in parallel and merge in either order (default: P34 first). Both are security-review packets: P34 is the installer; P35 is a machine-action guard in the elevated UAT driver.

### Inline rules for both packets

| Key | Rule |
| --- | --- |
| R-RESULT | Return `Result` / `Result<T>` (`Dhadgar.Shared.Results`) and never throw for I/O. Never `Result<T>.Success(null)`. |
| R-USING | `using` on every `IDisposable` (`FileStream` in tests). |
| R-CPM | No `Version=` in a `.csproj`. No new packages. |
| R-NULL | Nullable on. Build with `-p:TreatWarningsAsErrors=true`. |
| R-XUNIT | xUnit v2. Plain `[Fact]` for tests that only read sources (these run on Linux CI). Real-filesystem tests use `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test")`. Names follow `Subject_Condition_Expectation`. |
| R-UAT | **No machine-changing run (D-S239).** Never pass `-AcceptMachineChanges` or `-CleanupOnly`, never run `msiexec`, never write HKLM. Build the agent and the MSI only through `scripts\uat\Build-AgentWindowsUat.ps1`, never with a bare `dotnet publish -r` (D-S241). If `git status` shows a dirty lock file after a check, restore it with `git checkout -- <file>` and report it. |
| R-SCOPE | Edit only the files your packet names. Anything else is `blocked spec`. |
| R-DOCS | Doc tables have spaces around pipes, and every code block names its language. |

### P34: the MSI reads the Windows build from the registry (fix packet)

- **Branch** `s2/p34-msi-build-condition`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p34`, from w1 `6530253`.
- **Commit subject** `fix(agent-windows): read the Windows build from the registry so the MSI installs on Windows 10 and 11 (P34)`
- **Security review** yes (installer).
- **Files (only these):**
  - `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`
  - `tests/Dhadgar.Agent.Windows.Tests/Installation/InstallerPackageTests.cs`
  - `src/Agents/Dhadgar.Agent.Windows/Installer/README.md`
- **Reviewer note:** CLAUDE.md, `packet-reviewer.md`, `security-advisor.md` and the `ship-pr` skill still say "`WindowsBuild`, not `VersionNT`, in WiX". D-S242 supersedes that rule. A finding that asks for `WindowsBuild` back is out of spec.

#### Step 1: `Package.wxs`

Replace lines 93-95 (the comment and the `WindowsBuild` launch row) with the following. Keep the property next to the other properties, above the `Launch` rows:

```xml
    <!-- D-S242: msiexec's server session reports the Windows 8.1 values (VersionNT 603, WindowsBuild 9600)
         on Windows 10 and 11, so a WindowsBuild row refuses every supported machine. Read the real build
         number from the registry instead (REG_SZ, compared as an integer; empty when absent, so it fails closed). -->
    <Property Id="WINDOWS_BUILD_NUMBER" Secure="yes">
      <RegistrySearch Id="WindowsBuildNumberSearch" Root="HKLM" Key="SOFTWARE\Microsoft\Windows NT\CurrentVersion"
                      Name="CurrentBuildNumber" Type="raw" Bitness="always64" />
    </Property>
    <!-- Prevent running on Windows older than Windows 10 (build 10240 = Windows 10 RTM) -->
    <Launch Condition="Installed OR WINDOWS_BUILD_NUMBER &gt;= 10240"
            Message="[ProductName] requires Windows 10 or later." />
```

No other change. `VersionNT64`, `Privileged`, the endpoint row, `MajorUpgrade` and the agent-key `RegistrySearch` stay as they are. The property must be public (uppercase): AppSearch properties must be public (ICE52). If the build reports an ICE error for it, stop with `blocked spec` and quote the error. Do not suppress the ICE.

#### Step 2: `InstallerPackageTests.cs`

All of these are plain `[Fact]`s that read the sources, so they run on Linux CI.

**(a) Replace** `PackageWxs_LaunchConditions_UseWindowsBuildNotVersionNt` (lines 97-106) with:

```csharp
    [Fact]
    public void PackageWxs_WindowsVersionCondition_UsesRegistryBuildNumber()
    {
        var conditions = LoadPackage().Descendants(Wix + "Launch")
            .Select(l => (string?)l.Attribute("Condition") ?? string.Empty)
            .ToList();

        // D-S242: msiexec reports WindowsBuild 9600 and VersionNT 603 on Windows 10 and 11.
        Assert.DoesNotContain(conditions, c => System.Text.RegularExpressions.Regex.IsMatch(c, @"\bWindowsBuild\b"));
        Assert.DoesNotContain(conditions, c => c.Contains("VersionNT >=", System.StringComparison.Ordinal));
        Assert.Contains("Installed OR WINDOWS_BUILD_NUMBER >= 10240", conditions);
    }
```

The regex is case-sensitive, so it does not match `WINDOWS_BUILD_NUMBER`. `Condition` attribute values come back unescaped (`>=`), as in the old test.

**(b) Add** a test that pins the search exactly:

```csharp
    [Fact]
    public void PackageWxs_WindowsBuildNumber_IsReadFromCurrentVersionInThe64BitView()
    {
        var property = Property(LoadPackage(), "WINDOWS_BUILD_NUMBER");
        var search = Assert.Single(property.Elements(Wix + "RegistrySearch"));

        Assert.Equal("yes", (string?)property.Attribute("Secure"));
        Assert.Equal("WindowsBuildNumberSearch", (string?)search.Attribute("Id"));
        Assert.Equal("HKLM", (string?)search.Attribute("Root"));
        Assert.Equal(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", (string?)search.Attribute("Key"));
        Assert.Equal("CurrentBuildNumber", (string?)search.Attribute("Name"));
        Assert.Equal("raw", (string?)search.Attribute("Type"));
        Assert.Equal("always64", (string?)search.Attribute("Bitness"));
    }
```

**(c) Scope** `PackageWxs_HklmRegistryKeys_UseAgentRegistryKeyPath` (lines 43-54) so that it skips exactly this one read-only OS search:

- In the `Where`, add `&& (string?)e.Attribute("Id") != "WindowsBuildNumberSearch"`.
- Add a comment: `// D-S242: the one read of an OS key; pinned by PackageWxs_WindowsBuildNumber_IsReadFromCurrentVersionInThe64BitView.`

Do not exclude by `Key`. Excluding by `Key` would let a future agent key under another path slip through.

**Fails on revert:**

- With the old row restored, (a) fails on both the `WindowsBuild` regex and the `Contains`.
- With the property removed, (b) fails, because `Property` uses `.Single()`.
- With `Bitness="always64"` dropped, (b) fails.
- Without (c), the existing HKLM test fails as soon as step 1 lands.

#### Step 3: `Installer/README.md`

**(a)** Add a `## Requirements` section after the Prerequisites list:

> The MSI installs on 64-bit Windows 10 (build 10240) or later. The installer reads the build number from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber`. It cannot use the `WindowsBuild` or `VersionNT` properties, because msiexec reports Windows 8.1 values (9600 and 603) for them on Windows 10 and 11 (D-S242).

**(b)** Replace the bare `dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64` in "From the solution root" (line 29) with the UAT build script, `pwsh -NoProfile -File scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi`, and add one sentence: it restores locked and RID-less, so it does not rewrite lock files (D-S241). Keep the code block's language tag.

#### Checks, in order (Windows, non-elevated is fine)

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p34\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
2. `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p34\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~InstallerPackageTests"`, then the same command without `--filter`.
3. **Source mutation.** Temporarily put back `WindowsBuild &gt;= 10240` in the row, run step 2's filtered test and record that (a) fails. Restore the file and confirm `git diff` shows only your change.
4. **Build the MSI.** `pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p34\scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi`. It must exit 0 with no ICE error. Then run `git -C <worktree> status --porcelain=v1 --untracked-files=all` and confirm that only your three files show (D-S241).
5. **Executed check (evaluator).** `pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\docs\analysis\sprint2-agent-nodes\worknotes\P34-msi-conditions.ps1 -MsiPath <worktree>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release\MeridianConsoleAgent.msi`.
   - It must exit 0, and show `WINDOWS_BUILD_NUMBER` equal to the registry value and no `FAIL:` line. `[FALSE] Privileged` in a non-elevated shell is expected and not counted.
   - The script copies the MSI to %TEMP% and deletes the copy. It never installs anything.
   - Exit 2 means `blocked` (quote the output).
6. **Executed mutation.** Run the same script against the MSI the failed run used: `-MsiPath C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1\.uat\agent-windows\20261027-183915-637c\msi\MeridianConsoleAgent.msi`. The script only reads it, from a temp copy. It must exit 1 with exactly the three `FAIL:` lines quoted in this ruling.
7. `git status` clean apart from the commit. Commit locally only.

Paste the output of steps 4-6 into the report. If the MSI is not at that path, find it with Glob under `Installer\bin` and report the real path.

**Owed to the UAT run (in the report).** U11a must install. The MSI log `logs\msi-install.log` must contain `Property(S): WINDOWS_BUILD_NUMBER = <the machine's build>`, and `LaunchConditions` must end with return value 1. U3, U4, U11b and U9 must then pass in Msi mode.

**Docs owed:** step 3 only. `docs/PROJECT-STATE.md` and DL-219 (`docs/analysis/doc-library/seed-backlog.md:90`, which still says `WindowsBuild >= 10240`) go to P15.

### P35: a second cleanup treats a file under a removed folder as undone (fix packet)

- **Branch** `s2/p35-cleanup-idempotent-file`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p35`, from w1 `6530253`.
- **Commit subject** `fix(uat): treat a file whose folder is already gone as undone, so a second cleanup is clean (P35)`
- **Security review** yes (a machine action in the elevated UAT driver; the reviewer confirms the allowlist did not move).
- **Files (only these):**
  - `tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs` (member `DeleteFile` only)
  - `tests/Dhadgar.Agent.Windows.Uat.Tests/WindowsMachineActionsTests.cs`
  - new `tests/Dhadgar.Agent.Windows.Uat.Tests/CleanupIdempotenceTests.cs`
  - `scripts/uat/README.md` (one paragraph)
- **Must not change:** `CleanupEngine.cs`, `RunPaths.cs`, `CleanupEntry.cs`, `IMachineActions.cs`, any producer, or `UatRunner.cs`.

#### Step 1: `DeleteFile`

```csharp
    /// <inheritdoc />
    public Result DeleteFile(string path)
    {
        try
        {
            File.Delete(path);   // silent when the file is missing but its folder exists
            return Result.Success();
        }
        catch (DirectoryNotFoundException)
        {
            // D-S243: the folder is gone, so the file is too (an earlier pass removed the folder). Already undone.
            // CleanupEngine.DeleteGuarded validated the path before this call; that allowlist is unchanged.
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"file delete failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"file delete denied: {ex.Message}");
        }
    }
```

`DirectoryNotFoundException` derives from `IOException`, so its catch must come first. The compiler enforces that order (CS0160).

#### Step 2: tests in `WindowsMachineActionsTests.cs`

All three are `[SkippableFact]` tests that start with `Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");`.

- `DeleteFile_ParentFolderGone_IsSuccess`:
  - Path: `Path.Combine(Path.GetTempPath(), "uat-p35-" + Guid.NewGuid().ToString("N"), "tls", "gateway.pfx")`. Neither folder exists.
  - Assert `result.IsSuccess` with `result.Error` as the message.
  - **Fails on revert** with "file delete failed: Could not find a part of the path".
- `DeleteFile_FileMissingParentExists_IsSuccess`:
  - Use `Directory.CreateTempSubdirectory("uat-p35-")` and a file name inside it that was never created. Assert success, then delete the folder in `finally`.
  - This pins the existing behaviour.
- `DeleteFile_FileLockedOpen_IsFailure`:
  - In a temp subdirectory, create `locked.pfx` with `using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);`. Call `DeleteFile` while the stream is open.
  - Assert `IsFailure`, `Assert.StartsWith("file delete failed", result.Error, StringComparison.Ordinal)`, and that the file still exists.
  - Dispose the stream before the `finally` deletes the folder.
  - **Fails on the widening mutation** (`catch (IOException) { return Result.Success(); }`).

#### Step 3: new `CleanupIdempotenceTests.cs`

This test runs the real `CleanupEngine` with the real `WindowsMachineActions` against a temp repo. The class is `public sealed class CleanupIdempotenceTests : IDisposable`, and its `Dispose` deletes `_repo` recursively if it exists.

```csharp
    private const string RunId = "20261027-183915-637c";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-idem-" + Guid.NewGuid().ToString("N"));

    [SkippableFact]
    public async Task UndoAsync_TlsFolderAndPfxRunTwiceWithRealActions_SecondPassIsClean()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var paths = new RunPaths(_repo, RunId);
        var pfx = Path.Combine(paths.Tls, "gateway.pfx");
        Directory.CreateDirectory(paths.Tls);
        await File.WriteAllBytesAsync(pfx, [1, 2, 3]);
        // The manifest order the driver writes: the folder first, then the file in it.
        CleanupEntry[] entries = [new DirectoryEntry(paths.Tls, ExistedBefore: false), new FileEntry(pfx)];
        var engine = new CleanupEngine(new WindowsMachineActions(), paths);

        var first = await engine.UndoAsync(entries, CancellationToken.None);
        var second = await engine.UndoAsync(entries, CancellationToken.None);

        Assert.Empty(first.Failed);
        Assert.False(Directory.Exists(paths.Tls));
        Assert.Empty(second.Failed);
        Assert.Equal(2, second.Undone);
    }

    [SkippableFact]
    public async Task UndoAsync_MissingFileOutsideRunFolder_IsStillRefused()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var paths = new RunPaths(_repo, RunId);
        var outside = Path.Combine(_repo, "elsewhere", "tls", "gateway.pfx");   // under the repo, outside the run folder

        var report = await new CleanupEngine(new WindowsMachineActions(), paths)
            .UndoAsync([new FileEntry(outside)], CancellationToken.None);

        var failure = Assert.Single(report.Failed);
        Assert.Equal("file", failure.Kind);
        Assert.Contains("refusing to delete outside the run folder", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, report.Undone);
    }
```

The first test reproduces the runner's failure. **On revert**, the second pass has one failure, for `file`. The second test pins that the allowlist still runs before the now-tolerant action. It passes before and after the fix, and it **fails if a later change makes the engine skip validation** for missing files.

`RunId` reuses the failed run's ID only as a string. Nothing touches the real run folder: `_repo` is under `%TEMP%`. If any type the tests use is not visible, use the same `using` lines as `CleanupEngineTests.cs`. The test project already sees the internals; if it does not, stop with `blocked spec`.

#### Step 4: `scripts/uat/README.md`

In "After a crash: `-CleanupOnly`", replace "Running cleanup twice is safe." with:

> Running cleanup twice is safe. An item that is already gone counts as undone, including a file whose folder an earlier pass removed (D-S243). The run-folder and allowlist checks still run first.

#### Checks, in order

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p35\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
2. `dotnet test <same csproj> -c Release --no-build --filter "FullyQualifiedName~DeleteFile|FullyQualifiedName~CleanupIdempotenceTests|FullyQualifiedName~CleanupEngineTests"`, then the same command without `--filter`.
3. **Mutation A (revert).** Delete the new catch. `DeleteFile_ParentFolderGone_IsSuccess` and `UndoAsync_TlsFolderAndPfxRunTwiceWithRealActions_SecondPassIsClean` must fail.
4. **Mutation B (widen).** Change the new catch to `catch (IOException) { return Result.Success(); }` (above the old one; delete the old one so that it compiles). `DeleteFile_FileLockedOpen_IsFailure` must fail.
5. Restore the file. `git -C <worktree> diff --stat 6530253 -- tests/Dhadgar.Agent.Windows.Uat/Cleanup/CleanupEngine.cs tests/Dhadgar.Agent.Windows.Uat/Infra/RunPaths.cs tests/Dhadgar.Agent.Windows.Uat/Cleanup/CleanupEntry.cs` must print nothing (paste it).
6. `git status` clean apart from the commit. Commit locally only.

No wrapper run and no `-CleanupOnly`. The runner's old folder `20261027-183915-637c` is not cleaned again: D-S239 R8 allows one cleanup per runner folder, and it is already clean (preflight `20261027-184100-d9eb`, `footprintEmpty` pass). The real-filesystem test in step 3 is P35's proof.

**Owed to the UAT run (in the report).** The next Msi run's U9 must be `cleanup=clean`. The runner does not re-run `-CleanupOnly` as a test.

**Docs owed:** step 4 only.

## Effect on the UAT sequence

- Process and Service passed at `6530253`. The D-S224 evidence is `20261027-182612-65dd` and `20261027-183248-3fd7`.
- **S-hub-ws is proven.** The Process run passed it over WebSockets, so P15 may make the WebSockets claim citing that run.
- P34 and P35 change only the installer and the UAT cleanup action. Neither touches the agent binary, the stack or the Process/Service legs.
- After both merge, the UAT runner reruns **`-Mode Msi` only**, at the new w1 tip, with the same brief.

The D-S224 "approved SHA" question for the two earlier passes goes to the campaign review.

- **What the earlier passes covered.** They ran at `6530253`. The commits after it touch the installer, the UAT driver's cleanup action, tests and docs, but never the agent, Nodes, the Gateway or the Process/Service legs.
- **My recommendation.** The campaign review accepts them, recording the diff `6530253..<new tip>` restricted to `src/` minus `src/Agents/Dhadgar.Agent.Windows/Installer/`, which must be empty. The alternative is that the runner repeats Process and Service at the new tip, about 10 minutes.
- **Whose call.** This is the orchestrator's call at the campaign review, not an owner decision.

If the Msi rerun fails again, the runner stops and an architect ruling follows, as before.

## Owner actions (not blocking)

| ID | Action |
| --- | --- |
| O1 | Amend the WiX rule, which agents may not edit on an agent's say-so, to D-S242's text. For example: "Detect Windows 10+ with a registry `AppSearch` of `CurrentBuildNumber` (`>= 10240`); msiexec reports `WindowsBuild` 9600 and `VersionNT` 603 on Windows 10/11." The rule appears in `CLAUDE.md:88` and `:168`, `.claude/agents/packet-reviewer.md:20`, `.claude/agents/security-advisor.md:23` and `.claude/skills/ship-pr/SKILL.md:32`. Until then, reviewers apply D-S242 (P34 reviewer note). |

No BlockedDecision.
