# P18 ruling 1: the process probe, the process allowlist and the driver's threat model

- **Packet** P18 (UAT driver skeleton). **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18`, head `1f1320c`.
- **Trigger** pass 3 ended `fix` from both `packet-reviewer` (P18-review-3 N1) and `security-advisor` (P18-security-3 N1, N2).
- **Inputs** P18-review-1..3, P18-security-1..3, P18-fix-1..2 under the worktree's `.claude\reports\sprint2-agent-nodes\`.
- **Decision** [D-S237](../README.md#decisions) (new). No owner decision is blocking; see "Owner awareness" at the end.

## Ruling

**Re-spec, in this packet.** P18 cannot merge as it stands, and the defect is bigger than the one both reviewers named.

### Finding R1 (blocker, missed by all three passes): the process probe throws on the normal path

`IMachineActions.GetProcessImagePath` returns `Result<string?>` and uses `Success(null)` to mean "the process is gone or its PID was reused". The shared `Result<T>` **throws** on a null success (`src/Shared/Dhadgar.Shared/Results/Result.cs:150-155`, `InvalidOperationException("A successful result must have a value.")`). This is the same trap D-S232 ruled on for P11.

What happens on a real machine:

1. `WindowsMachineActions.cs:99`, `:106` and `:110` all call `Result<string?>.Success(null)`. Line 99 throws inside the `try`; the `catch (InvalidOperationException)` at `:108` catches it and calls `Success(null)` again at `:110`, which throws out of the method.
2. `CleanupEngine.UndoAsync` calls `Reject` for processes in the quiesce loop (`CleanupEngine.cs:54`, inside a `case ... when` guard) with no `try`. The exception leaves `UndoAsync`.
3. `UatRunner.CleanupGuardedAsync` catches it and reports `footprint-left` with **nothing undone**: no container removed, no registry key, store or certificate deleted.

"Gone" is the normal state of a child at cleanup time (`ChildProcess.StopAsync` and the job object kill children before the manifest is replayed, and `-CleanupOnly` runs after a crash). So from P19 on, every run with a `process` entry would leave its whole footprint behind. The unit tests never see it, because `RecordingActions` never returns a null image (its default is `Environment.ProcessPath`).

Both reviewers' N1 (`MainModule?.FileName` can be null) is the same line. With R1 it throws rather than fails open, but the fix is the same: no `Success(null)` anywhere.

### Finding R2 (spec gap): P19's Nodes and Gateway would be refused

The process rule allows an image only under the run folder (or the dotnet host). The UAT contract (work-packets "Stack settings") starts Nodes and Gateway from `src\Dhadgar.Nodes\bin\Release\net10.0\Dhadgar.Nodes.exe` and `src\Dhadgar.Gateway\bin\Release\net10.0\Dhadgar.Gateway.exe`, which are outside the run folder. Each would become a `CleanupFailure` in every run, which means exit 3. The dotnet-host arm has no producer: no packet starts a child through `dotnet`.

### Finding R3 (recurring notes): the threat model was never written down

Three passes carried the same items: PATH lookup of `sc.exe`, `docker`, `dotnet` and `git` by the elevated driver; lexical-only `RunPaths.Validate`; owner-SID checks on processes (security-3 N2). They recur because the spec never says whom the driver defends against. The answer is fixed by how the tool runs. The user starts `scripts\uat\Invoke-AgentWindowsUat.ps1` elevated, and it runs `dotnet build` on the repo and then the driver from `tests\...\bin\Release`. Any code running as the user can already change the repo, its MSBuild targets or the driver binary that the elevated shell builds and runs. A guard inside the driver cannot stop that code. The guards exist to stop **driver bugs, stale or foreign manifests and PID reuse** from touching anything the run did not create. D-S237 (3) records this. PATH pinning and owner-SID checks are closed. The junction notes stay as re-checks only where they are about the driver's own correctness (P22, the machine agent folder).

### Finding R4 (recurring spec conflict): the agent README pointer

P18's "Docs owed" asks for a line in `src/Agents/Dhadgar.Agent.Windows/README.md`, but P18's rule forbids `src/` edits beyond `InternalsVisibleTo`. P26 is editing that README in parallel, and P20 already owes a "Verifying on Windows" paragraph in it. **Ruling: the pointer moves to P20**, so P18 does not touch the agent README. Work-packets is amended.

### Notes not taken

- Security-3 N2 (owner SID, refuse the driver's own PID): the SID part is closed by D-S237 (3). Deleting the dotnet-host arm removes the "any `dotnet.exe` on the machine" vector entirely. Refusing `Environment.ProcessId` is kept, because it protects against a driver bug.
- Review-3's `WindowsMachineActionsTests` with the own process and a reused PID: taken, as Windows-only `[SkippableFact]` (R-XUNIT), plus a no-such-PID case that pins R1.
- The three guards the fixer did not mutation-check (`CertStoreEntry`, the `RegistryValueEntry` name rule, the CNG provider arm) are checked in this pass, so the note stops recurring.
- Commit trailer model name: cosmetic, ignored.

## Spec for the implementer (one Sonnet `packet-coder`, "ruling 1 apply")

Work only in `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18`. Edit only the files named below. Commit locally; no push.

### Repo rules that apply

- `Result` / `Result<T>` from `Dhadgar.Shared.Results` for every machine action. **Never `Result<T>.Success(null)`**: the shared type throws on it. Use a typed value (here `ProcessImage.NotRunning`).
- Exceptions are caught at the boundary and turned into a `Failure`. No new exception escapes `CleanupEngine.UndoAsync`.
- `using` on every `Process`.
- Nullable on; build with `-p:TreatWarningsAsErrors=true`.
- No package versions in a `.csproj`; no new packages.
- xUnit v2, plain `Assert`, names `Subject_Condition_Expectation`; Windows-only tests are `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test")` (copy `ChildProcessTests.cs`).
- Never run `run`, `-CleanupOnly` or anything with `-AcceptMachineChanges` (D-S225).

### Step 1. `tests/Dhadgar.Agent.Windows.Uat/Cleanup/IMachineActions.cs`

Add the probe type to the same file (above the interface) and replace the member:

```csharp
/// <summary>
/// What a process probe found. <see cref="NotRunning"/> means no process with that PID and start time runs
/// (gone, or the PID was reused): killing it is a no-op. A probe that cannot read a running process's image
/// returns a failure instead, never a value.
/// </summary>
internal sealed record ProcessImage(bool IsRunning, string Path)
{
    /// <summary>No process with that PID and start time runs.</summary>
    public static ProcessImage NotRunning { get; } = new(false, string.Empty);

    /// <summary>A running process and its full image path.</summary>
    public static ProcessImage Running(string path) => new(true, path);
}
```

```csharp
/// <summary>
/// Probes the process with this PID and start time. Success is <see cref="ProcessImage.NotRunning"/> or a running
/// process with a non-empty image path. A failure means the image of a running process could not be read.
/// </summary>
Result<ProcessImage> GetProcessImage(int pid, DateTime startTimeUtc);
```

Delete `GetProcessImagePath`.

### Step 2. `tests/Dhadgar.Agent.Windows.Uat/Cleanup/WindowsMachineActions.cs`

Replace `GetProcessImagePath` (lines 91-116) with:

```csharp
/// <inheritdoc />
public Result<ProcessImage> GetProcessImage(int pid, DateTime startTimeUtc)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        if (Math.Abs((process.StartTime.ToUniversalTime() - startTimeUtc).TotalSeconds) > 1)
        {
            return Result<ProcessImage>.Success(ProcessImage.NotRunning); // PID reused by another process.
        }

        var image = process.MainModule?.FileName;
        return string.IsNullOrEmpty(image)
            ? Result<ProcessImage>.Failure($"could not read the image of process {pid}")
            : Result<ProcessImage>.Success(ProcessImage.Running(image));
    }
    catch (ArgumentException)
    {
        return Result<ProcessImage>.Success(ProcessImage.NotRunning); // No such process.
    }
    catch (InvalidOperationException)
    {
        return Result<ProcessImage>.Success(ProcessImage.NotRunning); // Exited between lookup and read.
    }
    catch (Win32Exception ex)
    {
        return Result<ProcessImage>.Failure($"could not read the image of process {pid}: {ex.Message}");
    }
}
```

The method then has exactly three `NotRunning` returns (reused, no such process, exited) and two failures (null or empty image, unreadable).

### Step 3. `tests/Dhadgar.Agent.Windows.Uat/Infra/RunPaths.cs`

Add the repo root and the two stack images P19 starts (paths copied from the UAT contract's "Stack settings" table):

```csharp
public RunPaths(string repoRoot, string runId)
{
    RepoRoot = Path.GetFullPath(repoRoot);
    RunDir = Path.GetFullPath(Path.Combine(RepoRoot, ".uat", "agent-windows", runId));
    StackImages =
    [
        Path.Combine(RepoRoot, "src", "Dhadgar.Nodes", "bin", "Release", "net10.0", "Dhadgar.Nodes.exe"),
        Path.Combine(RepoRoot, "src", "Dhadgar.Gateway", "bin", "Release", "net10.0", "Dhadgar.Gateway.exe"),
    ];
}

/// <summary>The repository root (full path).</summary>
public string RepoRoot { get; }

/// <summary>
/// The only process images outside <see cref="RunDir"/> that cleanup may kill: the Nodes and Gateway builds the
/// stack starts (D-S237). A packet that starts another child outside the run folder adds it here, with a test.
/// </summary>
public IReadOnlyList<string> StackImages { get; }
```

Keep everything else in the file as it is.

### Step 4. `tests/Dhadgar.Agent.Windows.Uat/Cleanup/CleanupEngine.cs`

1. Delete `DotnetHostRelativePath` and `IsDotnetHost`.
2. Replace `ProcessImageAllowed` and its summary with:

```csharp
/// <summary>
/// A process may be killed only if it is not the driver itself and its image is under the run folder or is one
/// of <see cref="RunPaths.StackImages"/>. A process that no longer runs (gone, or its PID reused) is allowed:
/// the kill re-checks the start time and is a no-op. An image that cannot be read is refused (fail closed).
/// </summary>
private bool ProcessImageAllowed(ProcessEntry process)
{
    if (process.Pid == Environment.ProcessId)
    {
        return false;
    }

    var probe = _actions.GetProcessImage(process.Pid, process.StartTimeUtc);
    if (probe.IsFailure)
    {
        return false;
    }

    var image = probe.Value;
    if (!image.IsRunning)
    {
        return true;
    }

    return _runPaths.Validate(image.Path).IsSuccess
        || _runPaths.StackImages.Any(allowed => string.Equals(allowed, image.Path, StringComparison.OrdinalIgnoreCase));
}
```

3. Make the quiesce pass unable to throw: `Reject` must run inside the guarded call. Replace the quiesce `foreach` (lines 46-58) with:

```csharp
// Quiesce: a running service or agent process would hold its key, store or folder.
// Reject runs inside the guard, so a probe that throws cannot abort cleanup; the undo pass records it.
foreach (var entry in entries.Reverse())
{
    if (entry is not (ServiceEntry or ProcessEntry))
    {
        continue;
    }

    QuiesceIgnoringResult(() => Reject(entry) is not null
        ? Result.Success()
        : entry switch
        {
            ServiceEntry service => _actions.StopService(service.Name),
            ProcessEntry process => _actions.KillProcess(process.Pid, process.StartTimeUtc),
            _ => Result.Success(),
        });
}
```

4. Update the class summary's last sentence to: `The manifest is untrusted input: <see cref="Reject"/> refuses any item the driver does not own (D-S237).` No other change in this file.

### Step 5. `tests/Dhadgar.Agent.Windows.Uat.Tests/RecordingActions.cs`

Replace the process probe members with:

```csharp
/// <summary>Image paths of running processes by PID. A PID not listed is not running.</summary>
public Dictionary<int, string> ProcessImages { get; } = [];

/// <summary>PIDs whose image cannot be read: the probe fails.</summary>
public HashSet<int> UnreadableProcesses { get; } = [];

/// <summary>PIDs whose probe throws an <see cref="IOException"/>.</summary>
public HashSet<int> ThrowingProcesses { get; } = [];

public Result<ProcessImage> GetProcessImage(int pid, DateTime startTimeUtc)
{
    if (ThrowingProcesses.Contains(pid))
    {
        throw new IOException($"fake: probe of {pid} threw");
    }

    if (UnreadableProcesses.Contains(pid))
    {
        return Result<ProcessImage>.Failure($"fake: image of {pid} unreadable");
    }

    return Result<ProcessImage>.Success(
        ProcessImages.TryGetValue(pid, out var image) ? ProcessImage.Running(image) : ProcessImage.NotRunning);
}
```

The existing `UndoAsync_ServiceAndProcess_AreQuiescedBeforeAnyDelete` (PID 1, not listed) stays valid: a process that is not running is allowed and `KillProcess:1` is still called first.

### Step 6. Tests in `tests/Dhadgar.Agent.Windows.Uat.Tests/CleanupEngineTests.cs` (append to the class)

Each asserts both the calls and the report. Use the class's `_paths`.

| Test | Arrange | Assert | Fails when |
| --- | --- | --- | --- |
| `UndoAsync_ProcessNotRunning_IsAllowedAndOthersUndone` | entries `[new RegistryValueEntry(@"SOFTWARE\Meridian Console\Agent", "EnrollmentToken", false), new ProcessEntry(8, DateTime.UtcNow, "agent")]`, PID 8 not listed | `actions.Calls` contains `KillProcess:8` and `DeleteRegistryValue:SOFTWARE\Meridian Console\Agent\EnrollmentToken`; `report.Failed` empty | `!image.IsRunning` returns false |
| `UndoAsync_ProcessProbeThrows_DoesNotAbortCleanup` | same two entries with PID 9 in `ThrowingProcesses` | `UndoAsync` returns (no exception); `actions.Calls` is exactly `["DeleteRegistryValue:SOFTWARE\\Meridian Console\\Agent\\EnrollmentToken"]`; one failure with `Kind == "process"` | `Reject` is moved back outside `QuiesceIgnoringResult` |
| `UndoAsync_DriverOwnProcess_IsRefused` | `actions.ProcessImages[Environment.ProcessId] = Path.Combine(_paths.RunDir, "agent", "x.exe")`; entry `new ProcessEntry(Environment.ProcessId, DateTime.UtcNow, "self")` | `actions.Calls` empty; one failure | the `Environment.ProcessId` check is removed |
| `UndoAsync_DotnetHostImage_IsRefused` | `actions.ProcessImages[6] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")`; entry PID 6 | `actions.Calls` empty; one failure | a dotnet-host arm is re-added |
| `UndoAsync_StackImage_IsKilled` (`[Theory]` with `InlineData(0)` and `InlineData(1)`) | `actions.ProcessImages[7] = _paths.StackImages[index]`; entry PID 7 | `actions.Calls` contains `KillProcess:7`; `report.Failed` empty | the `StackImages` arm is removed |

Add to `tests/Dhadgar.Agent.Windows.Uat.Tests/RunPathsTests.cs`:

- `StackImages_AreTheNodesAndGatewayReleaseBuilds`: `new RunPaths(repo, runId).StackImages` equals `[Path.Combine(full repo, "src", "Dhadgar.Nodes", "bin", "Release", "net10.0", "Dhadgar.Nodes.exe"), ...Gateway...]`, and neither validates under `RunDir`. It fails if a path drifts from the UAT contract.

### Step 7. New `tests/Dhadgar.Agent.Windows.Uat.Tests/WindowsMachineActionsTests.cs`

Three `[SkippableFact]`s, each starting with `Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");`. Keep the `#pragma` or `[SupportedOSPlatform("windows")]` pattern `ChildProcessTests.cs` uses, if it uses one.

```csharp
[SkippableFact]
public void GetProcessImage_OwnProcess_ReturnsRunningWithItsImage()
{
    Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
    using var self = Process.GetCurrentProcess();

    var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime());

    Assert.True(result.IsSuccess);
    Assert.True(result.Value.IsRunning);
    Assert.Equal(Environment.ProcessPath, result.Value.Path, ignoreCase: true);
}

[SkippableFact]
public void GetProcessImage_ReusedPid_ReturnsNotRunning()
{
    Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
    using var self = Process.GetCurrentProcess();

    var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime().AddHours(-1));

    Assert.True(result.IsSuccess);
    Assert.False(result.Value.IsRunning);
}

[SkippableFact]
public void GetProcessImage_NoSuchPid_ReturnsNotRunning()
{
    Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

    // Windows process IDs are multiples of 4, so PID 3 never exists.
    var result = new WindowsMachineActions().GetProcessImage(3, DateTime.UtcNow);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value.IsRunning);
}
```

The second and third tests pin R1: with the pass 2 code (`Success(null)`) both throw `InvalidOperationException`.

### Step 8. `scripts/uat/README.md`

Add a section `## Threat model (D-S237)` directly after `## Who runs what (D-S225)`, in plain prose:

- The driver trusts the account that runs it. Anything running as that user can change the repo, the build or the driver that the elevated script builds and runs, so the driver does not try to defend against it.
- Its guards stop driver bugs, a stale or foreign manifest, and PID reuse from touching anything the run did not create. Cleanup replays only these items: the named service, the agent registry key and its `EnrollmentToken` value, agent and UAT certificates in `LocalMachine` `Root`, `My` and `MeridianConsoleAgent`, the `MeridianConsoleAgent` store, the agent event source, `meridian-uat-*` containers and CNG keys, files and folders under the run folder (plus the machine agent folder), and processes whose image is under the run folder or is the Nodes or Gateway build the stack starts.
- A process cleanup cannot read is left running and reported, never killed.

Do not edit `src/Agents/Dhadgar.Agent.Windows/README.md` (P18-ruling-1 R4 moves the pointer to P20).

### Checks (in order)

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release
pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p18\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight
```

The test run must pass with no skips on Windows (63 existing plus the new ones). `-Preflight` must exit 0 or 1 and leave no manifest.

**Mutation checks** (temporary edit, run the test class, then restore the file byte-identical; list each result in the report):

1. Move `Reject` back out of `QuiesceIgnoringResult` (the pass 2 shape) → `UndoAsync_ProcessProbeThrows_DoesNotAbortCleanup` fails.
2. Remove the `Environment.ProcessId` check → `UndoAsync_DriverOwnProcess_IsRefused` fails.
3. Remove the `StackImages` arm → both `UndoAsync_StackImage_IsKilled` cases fail.
4. Return `false` for `!image.IsRunning` → `UndoAsync_ProcessNotRunning_IsAllowedAndOthersUndone` fails.
5. `CertStoreEntry` arm `=> true`, the `RegistryValueEntry` name rule removed, and the CNG provider check removed, one at a time → `UndoAsync_ForeignCertStore_IsRefused`, `UndoAsync_RegistryValueWithForeignName_IsRefused` and `UndoAsync_CngKeyWithForeignProviderOrName_IsRefused` each fail. If one does not fail, fix the test so it does, and say so.

Also run `git grep -n "Success(null)" -- tests/Dhadgar.Agent.Windows.Uat tests/Dhadgar.Agent.Windows.Uat.Tests` and paste the output; it must be empty.

### Commit

One local commit on the worktree's branch, explicit paths only (the files named above), subject:

`test(uat): typed process probe, stack-image allowlist and threat model (P18 ruling 1)`

with the attribution lines from the brief. Report to `<worktree>\.claude\reports\sprint2-agent-nodes\P18-ruling-apply-1.md`.

## Review after the apply

Both run in parallel on the range `1f1320c..<apply sha>`, with this note as the spec:

- **One `packet-reviewer` ruling check** (`P18-ruling-check-1.md`). It confirms steps 1-8, runs the checks and the `Success(null)` grep, and confirms the mutation results in the apply report. It does not re-raise items D-S237 (3) closes (PATH pinning, owner-SID checks, junction resolution in `RunPaths`) or the agent README pointer (R4).
- **One `security-advisor` pass** (`P18-security-ruling-1.md`), because the change is the guard on an elevated process kill. Scope: that no path in `GetProcessImage` or `ProcessImageAllowed` allows a running process whose image is unknown, that `UndoAsync` cannot throw from the quiesce pass, and that the README's threat model matches the code. The same exclusions apply.

If either verdict is not `ship`, P18 becomes **BlockedDecision** (skill §4).

## Effect on later packets (amended in work-packets.md)

- **Wave 2U rules:** new **R-NONULL**: never `Result<T>.Success(null)`; a "nothing there" outcome is a typed value. New **R-CLEANUP**: cleanup kills only processes whose image is under the run folder or in `RunPaths.StackImages`, and deletes only CNG keys named `meridian-uat-*` under the two Microsoft providers. A packet that needs a process elsewhere or another key name or provider adds the rule and its test, or stops with `blocked spec`.
- **Contract, `process` row:** the undo now names the image rule.
- **P19:** `ServiceHost` starts Nodes and Gateway with `FileName = RunPaths.StackImages[0]` and `[1]`. `ServiceEnvironmentTests` asserts it.
- **P20:** owes the agent README UAT pointer (moved from P18).
- **P22:** the machine agent folder junction re-check stays (driver correctness); PATH and owner SID do not.

## Owner awareness (not blocking)

D-S237 (3) accepts one residual: code already running as the user who launches the UAT can reach admin rights through it, as it could through any elevated build of the repo. The owner may overturn this at the security-review approval of the campaign PR. Overturning it would mean running the driver from a signed, admin-only location, which is outside this campaign.
