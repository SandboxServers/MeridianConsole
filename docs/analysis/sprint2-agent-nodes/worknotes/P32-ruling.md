# P32 ruling: the UAT build must not rewrite tracked lock files (D-S241)

Date: 2026-10-27. Author: architect. Trigger: the UAT runner's elevated `-Mode Process -AcceptMachineChanges` at w1 `0dc3dd6` exited 2 with "agent run needs a clean tree (D-S239)". The refusal came after the wrapper's own build and before any machine change. `-Preflight` had exited 0, and the next `-Preflight` left the tree clean again. No machine change was made.

## Ruling

**Option (c) with (b)'s locked mode.** The UAT build moves into a new `scripts/uat/Build-AgentWindowsUat.ps1`. It restores every project first, with `--locked-mode` and without a runtime identifier, and then builds or publishes it with `--no-restore`. The wrapper calls this script and keeps no `dotnet` command of its own. The D-S239 clean-tree guard does not change: the wrapper still checks before the build and the driver still checks after it. P32 adds no allowlist, no exclusion and no reset of dirty files. The lock files and CI do not change.

Rejected:

| Option | Why not (each was run in a scratch clone at `0dc3dd6`) |
| --- | --- |
| (a) Commit lock files that cover `win-x64` | They then disagree with every RID-less restore. A plain `dotnet build` rewrites them back, the other way round. CI's `dotnet restore Dhadgar.sln --locked-mode` (RID-less, Linux) fails with NU1004 on `Agent.Core`, `Contracts` and `Shared`: "runtime identifiers have changed … lock file's runtime identifiers win-x64". Making it stable would mean adding `<RuntimeIdentifiers>` to `Contracts` and `Shared`, which every service references. That is too much change for a UAT script defect. |
| (b) alone, `publish -r win-x64 -p:RestoreLockedMode=true` | The publish fails with NU1004 in the same three projects. The tree stays clean, but nothing is built. |
| Allowlisting the three lock files in the tree check | The brief forbids weakening D-S239, and a lock file is a build input. |

## Findings (evidence)

1. **What the build does.** For `-Mode Process` and `Service`, the wrapper (`Invoke-AgentWindowsUat.ps1` lines 116-147 at `0dc3dd6`) runs `dotnet build` on the driver, Nodes and Gateway (RID-less, implicit restore). It then runs `dotnet publish src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -r win-x64 -o <runDir>\agent` with an implicit restore. `-Mode Msi` runs the same publish without `-o`, then `dotnet build` on the `.wixproj`. No command passes a lock-file option.
2. **Lock-file settings.** `Directory.Build.props:24` sets `RestorePackagesWithLockFile=true` for every project. No project sets `RestoreLockedMode`. Only `Dhadgar.Agent.Windows` and `Dhadgar.Agent.GameServerWrapper` declare `<RuntimeIdentifiers>win-x64;win-arm64;linux-x64</RuntimeIdentifiers>`, so only their lock files have `net10.0/<rid>` sections. The agent's three `ProjectReference`s, `Agent.Core`, `Contracts` and `Shared`, are RID-agnostic libraries whose lock files hold only `net10.0`.
3. **The cause.** `publish -r win-x64` sets `RuntimeIdentifier=win-x64` as a global property. The implicit restore walks the `ProjectReference` graph with the same global properties, so NuGet restores the three libraries for `win-x64` too. It then rewrites their lock files with a new `net10.0/win-x64` section, because they are not in locked mode. The build itself still builds them RID-agnostic. The next RID-less restore (the driver build in `-Preflight`) removes the section again, which is why the tree was clean afterwards.
4. **The diff, reproduced** in a scratch clone at `0dc3dd6` with the wrapper's exact publish command (exit 0): `Agent.Core` +12 lines, a `"net10.0/win-x64"` section with `System.Diagnostics.EventLog` 10.0.14 and `Tmds.LibC` 0.2.0, both `Transitive`. `Contracts` and `Shared` each gained `"net10.0/win-x64": {}`. These are the same three files the owner's run reported. No version changed. The section only describes the RID-specific graph.
5. **The fix, verified.** Starting from deleted `bin/` and `obj/` folders, `dotnet restore <agent> --locked-mode` (RID-less) followed by `dotnet publish <agent> -c Release -r win-x64 --no-restore -o <dir>` exits 0 and leaves `git status` empty. The output matches the old publish: the same 6 files and the same `Dhadgar.Agent.Windows.exe`, 95,401,726 bytes. The RID-less restore already records every pack the self-contained publish needs, because the agent declares `RuntimeIdentifiers`: `downloadDependencies` lists `Microsoft.NETCore.App.Runtime.win-x64`, `Microsoft.AspNetCore.App.Runtime.win-x64` and the rest, and the assets file has a `net10.0/win-x64` target. The drafted `Build-AgentWindowsUat.ps1` below was also run as a whole. `-Verb run -Mode Process` exits 0 and the tree stays clean. `-Verb run -Mode Msi` exits 0, the tree stays clean and exactly one `MeridianConsoleAgent.msi` (36 MB) is built. The Msi leg needed a short `subst` drive, because the long scratch path made `wix.exe` fail with WIX0216 0x57, and the unchanged command fails the same way at that path. Locked restores of the driver, Nodes, Gateway, agent and installer all pass at `0dc3dd6`.
6. **Mutation, verified.** The same script with the Process publish reverted to the old self-restoring form (no restore step and no `--no-restore`) exits 0, and `git status` lists exactly the three lock files.
7. **CI is not affected, now or after P32.** `_dotnet-build-test.yml` runs on `ubuntu-24.04` with `CI=true`. It restores `Dhadgar.sln` RID-less in locked mode (`.github/actions/setup-dotnet-env`), builds with `--no-restore`, and never publishes with `-r` or builds the installer. The committed lock files already pass that restore, and P32 changes no lock file, project or workflow. The new plan tests are Windows-only and are skipped on Linux CI. `UatScriptTests` (text pins, retargeted) and `ScriptParseTests` (which picks up the new script by glob) still run there.
8. **Side effect, intended.** A lock file that is out of step with its project now fails the UAT build (NU1004, wrapper exit 4, before the driver starts, so with no machine change) instead of being silently rewritten. On a clean tree at a reviewed SHA this cannot happen, because CI's locked restore already holds the committed lock files to it. On a developer's dirty tree, the developer restores unlocked, commits and runs again. The README says so.
9. **Wider hazard, not fixed here.** Any manual `dotnet publish src\Agents\Dhadgar.Agent.Windows -r win-x64`, the command the `.wixproj` header comment and P23's own check list give, dirties the same three files. P32 fixes the UAT path, which is the one with a clean-tree guard. P15 files a follow-up issue: "RID publish of the Windows agent rewrites the lock files of RID-agnostic references". A repo-wide fix (for example `RuntimeIdentifiers` on `Agent.Core`) needs its own CI proof.

## Effect on the plan

- **P32** (below) merges into w1 from `0dc3dd6`. After it merges, the orchestrator dispatches the UAT runner again at the new w1 tip, starting with `-Mode Process`. The runner brief (D-S239 § (2)) does not change.
- The UAT run at `0dc3dd6` counts as **blocked dirty**, not failed: it was refused before any machine change. Its run folder holds no `launch.json` or manifest, so it needs no cleanup.
- **Wave 2U rule added:** packet checks never run `dotnet publish -r` or `dotnet build -r` on the agent directly. They use `scripts\uat\Build-AgentWindowsUat.ps1`, or the wrapper's `-Preflight`/`-StackOnly`.
- P15: add the follow-up issue from finding 9. List D-S241 with the D-S239 items at the PR approval. It does not change the residual risks R1-R8.

---

## P32: the UAT build keeps the tree clean (locked, RID-less restore, then `--no-restore`)

- **Branch** `s2/p32-uat-lockfiles`, from w1 `0dc3dd6`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p32` (ops creates it).
- **Depends on** nothing (w1 tip). **Blocks** the UAT runner's next dispatch.
- **Security review: yes.** It edits the elevated UAT wrapper and adds a script that runs elevated in every agent run. The PR keeps the `security-review` label.
- **Commit subject** `fix(uat): restore locked and RID-less, then build with --no-restore, so the UAT build keeps the tree clean (P32)`
- **Report** `<worktree>\.claude\reports\sprint2-agent-nodes\P32.md`.

### Rules (inline)

| Key | Rule |
| --- | --- |
| D-S239 | Do not weaken the clean-tree guard. Do not edit the wrapper's tree-check block (lines 68-92 at `0dc3dd6`), its pathspec, `GitWorkTree`, `Preflight.AgentRunTree` or any C# file in `tests/Dhadgar.Agent.Windows.Uat/`. No allowlist, exclusion or `git checkout` inside any script. |
| No real run | Never pass `-AcceptMachineChanges` or `-CleanupOnly`. Run only `-Preflight`. Never unset `CLAUDECODE`. |
| R-ARGS | Every `dotnet` call is `& dotnet @arguments` with a `[string[]]`, never a built command string. Test processes use `ProcessStartInfo.ArgumentList` and `UseShellExecute = false`. |
| R-PATH | The stale-output delete keeps its repo-prefix check (`GetFullPath`, then `StartsWith($repoPrefix, OrdinalIgnoreCase)`), and it refuses by `throw`. |
| R-NOEXIT | `Build-AgentWindowsUat.ps1` never calls `exit`. The wrapper runs it with `&` in its own session, so an `exit` would return to the wrapper and the wrapper would carry on after a failed build. Failures `throw`, and the wrapper's `catch` turns them into exit 4. |
| R-USING | `using` on every `Process` and `JsonDocument` in the tests. |
| R-CPM | No new packages. No `Version=` in a csproj. `Xunit.SkippableFact` (`[SkippableFact]`, `[SkippableTheory]`) is already referenced. |
| R-NULL | Nullable on; build and test with `-p:TreatWarningsAsErrors=true`. |
| R-XUNIT | xUnit 2.9.3, plain `Assert` (`Assert.Fail` is available), names `Subject_Condition_Expectation`. Windows-only tests are `[SkippableFact]`/`[SkippableTheory]` with `Skip.IfNot(OperatingSystem.IsWindows(), ...)`. |

### Files and members

Base `0dc3dd6`. Touch only these files:

1. **New `scripts/uat/Build-AgentWindowsUat.ps1`**, exactly this content (tested in a scratch clone, findings 5 and 6):

   ```powershell
   #Requires -Version 7.4
   <#
   .SYNOPSIS
       Builds what one Invoke-AgentWindowsUat.ps1 call needs without changing a tracked file (P32, D-S241).

   .DESCRIPTION
       Each project is restored first, in locked mode and without a runtime identifier, and then built or
       published with --no-restore. A publish with -r win-x64 that restores for itself adds a net10.0/win-x64
       section to the lock files of the agent's RID-agnostic references (Agent.Core, Contracts, Shared). That
       dirties the tree, and the driver then refuses the agent run (D-S239). A lock file that does not match
       its project now fails the restore (NU1004) instead of being rewritten.

       Invoke-AgentWindowsUat.ps1 calls this script. -ListSteps prints the plan as JSON and runs nothing.
       The script never calls exit: every failure throws, and the wrapper turns it into exit 4.
   #>
   [CmdletBinding()]
   param(
       [Parameter(Mandatory)][ValidateSet('preflight', 'stack-only', 'run', 'cleanup')][string] $Verb,
       [ValidateSet('Process', 'Service', 'Msi')][string] $Mode,
       [string] $AgentOut,   # run with -Mode Process or Service: the folder the agent is published to
       [switch] $ListSteps
   )

   Set-StrictMode -Version Latest
   $ErrorActionPreference = 'Stop'

   $repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
   $driverProject = Join-Path $repo 'tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj'
   $nodesProject = Join-Path $repo 'src\Dhadgar.Nodes\Dhadgar.Nodes.csproj'
   $gatewayProject = Join-Path $repo 'src\Dhadgar.Gateway\Dhadgar.Gateway.csproj'
   $agentProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj'
   $installerProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj'

   if ($Verb -eq 'run' -and -not $Mode) {
       throw 'Build-AgentWindowsUat: -Verb run needs -Mode'
   }
   if ($Verb -eq 'run' -and $Mode -ne 'Msi' -and -not ($AgentOut -and [System.IO.Path]::IsPathFullyQualified($AgentOut))) {
       throw 'Build-AgentWindowsUat: -Mode Process or Service needs a fully qualified -AgentOut'
   }

   $plan = [System.Collections.Generic.List[object]]::new()

   function Add-DotNet {
       param([Parameter(Mandatory)][string[]] $Arguments)
       $plan.Add([pscustomobject]@{ Kind = 'dotnet'; Arguments = $Arguments; Path = $null })
   }

   function Add-Remove {
       param([Parameter(Mandatory)][string] $Path)
       $plan.Add([pscustomobject]@{ Kind = 'remove'; Arguments = @(); Path = $Path })
   }

   Add-DotNet -Arguments @('restore', $driverProject, '--locked-mode')
   Add-DotNet -Arguments @('build', $driverProject, '-c', 'Release', '--no-restore')
   if ($Verb -eq 'stack-only' -or $Verb -eq 'run') {
       Add-DotNet -Arguments @('restore', $nodesProject, '--locked-mode')
       Add-DotNet -Arguments @('build', $nodesProject, '-c', 'Release', '--no-restore')
       Add-DotNet -Arguments @('restore', $gatewayProject, '--locked-mode')
       Add-DotNet -Arguments @('build', $gatewayProject, '-c', 'Release', '--no-restore')
   }
   if ($Verb -eq 'run' -and $Mode -eq 'Msi') {
       # Stale output from an earlier build or branch is git-ignored and would be harvested into the MSI, so the
       # publish folder and the installer's Release output are deleted first. Both are checked to be under the repo.
       Add-Remove -Path (Join-Path (Split-Path -Parent $agentProject) 'bin\Release\net10.0\win-x64\publish')
       Add-Remove -Path (Join-Path (Split-Path -Parent $installerProject) 'bin\Release')
       # The MSI harvests the default publish folder (bin\Release\net10.0\win-x64\publish), so no -o (P23).
       Add-DotNet -Arguments @('restore', $agentProject, '--locked-mode')
       Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore')
       Add-DotNet -Arguments @('restore', $installerProject, '--locked-mode')
       Add-DotNet -Arguments @('build', $installerProject, '-c', 'Release', '--no-restore')
   }
   elseif ($Verb -eq 'run') {
       Add-DotNet -Arguments @('restore', $agentProject, '--locked-mode')
       Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-o', $AgentOut)
   }

   if ($ListSteps) {
       ConvertTo-Json -InputObject $plan.ToArray() -Depth 4 -AsArray
       return
   }

   $repoPrefix = $repo.TrimEnd('\') + '\'
   foreach ($step in $plan) {
       if ($step.Kind -eq 'remove') {
           $resolvedStale = [System.IO.Path]::GetFullPath($step.Path)
           if (-not $resolvedStale.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
               throw "Refusing to delete ${resolvedStale}: it is not under $repo."
           }
           if (Test-Path -LiteralPath $resolvedStale) {
               Remove-Item -LiteralPath $resolvedStale -Recurse -Force
           }
           continue
       }

       $arguments = [string[]] $step.Arguments
       & dotnet @arguments
       if ($LASTEXITCODE -ne 0) {
           throw "dotnet $($arguments[0]) failed with exit code $LASTEXITCODE"
       }
   }
   ```

   The step order matches the wrapper at `0dc3dd6`: driver, then Nodes and Gateway, then for Msi the stale deletes, publish and installer, otherwise the publish to `-AgentOut`. `cleanup` and `preflight` build only the driver, as today.

2. **`scripts/uat/Invoke-AgentWindowsUat.ps1`**:
   - Delete the variables `$driverProject`, `$nodesProject`, `$gatewayProject` and `$agentProject` (lines 34, 36-38). Keep `$repo`, `$uatRoot`, `$driverExe` and `$installerProject`, which the MSI copy block uses.
   - Delete `function Invoke-DotNet` (lines 47-54).
   - Replace the whole `if (-not $NoBuild) { ... }` block (lines 116-147) with:

     ```powershell
         if (-not $NoBuild) {
             # One build path for every verb (P32, D-S241): locked, RID-less restores, then --no-restore builds,
             # so the build never rewrites a tracked lock file and an agent run keeps its clean tree (D-S239).
             $buildArgs = @{ Verb = $verb }
             if ($verb -eq 'run') {
                 $buildArgs.Mode = $Mode
                 if ($Mode -ne 'Msi') {
                     $buildArgs.AgentOut = Join-Path $runDir 'agent'
                 }
             }
             & (Join-Path $PSScriptRoot 'Build-AgentWindowsUat.ps1') @buildArgs
         }
     ```

   - Change nothing else: not the param block, not the guards (lines 56-99), not the run folder lines, not the MSI copy block (149-162), not the driver call. The wrapper never passes `-ListSteps`.
   - In `.DESCRIPTION`, add one sentence after the exit-code paragraph: "It builds through Build-AgentWindowsUat.ps1 (locked, RID-less restore, then --no-restore; P32), so a build never changes a tracked file."

3. **New `tests/Dhadgar.Agent.Windows.Uat.Tests/PwshScript.cs`**: `internal static class PwshScript`. Move `ResolvePwsh()` and `FindRepoRoot()` out of `ScriptParseTests` unchanged, as `internal static`. Add:

   ```csharp
   /// <summary>Runs pwsh -NoProfile -NonInteractive -File <script> <args> and returns the exit code, stdout and stderr.</summary>
   internal static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string pwsh, string script, IEnumerable<string> arguments, TimeSpan timeout)
   ```

   Its body follows `ScriptParseTests.RunParserAsync`: `ArgumentList`, `using var process`, both streams read concurrently, kill the process tree and rethrow on timeout.

4. **`tests/Dhadgar.Agent.Windows.Uat.Tests/ScriptParseTests.cs`**: call `PwshScript.ResolvePwsh()` and `PwshScript.FindRepoRoot()`, and delete its private copies. Its `RunParserAsync` may call `PwshScript.RunAsync`. Its behaviour does not change.

5. **`tests/Dhadgar.Agent.Windows.Uat.Tests/UatScriptTests.cs`** (text pins, run in Linux CI):
   - Replace `ReadWrapper()` with `ReadUatScript(string fileName)`, the same parent-folder walk with the file name as a parameter.
   - `MsiPublish_DeletesTheStalePublishFolderBeforePublishing` reads `Build-AgentWindowsUat.ps1`. Its delete marker is `Add-Remove -Path (Join-Path (Split-Path -Parent $agentProject) 'bin\Release\net10.0\win-x64\publish')`. Its publish marker is `Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore')`, and it must come after the delete marker. Keep the `Contains("bin\\Release\\net10.0\\win-x64\\publish")` assert and add `Contains("Remove-Item -LiteralPath $resolvedStale -Recurse -Force")`.
   - `StaleDelete_IsRefusedOutsideTheRepo` reads `Build-AgentWindowsUat.ps1` and keeps both asserts.
   - `MsiSelection_AcceptsExactlyOneCandidate` keeps reading `Invoke-AgentWindowsUat.ps1`.
   - Add `private static string StripComments(string script)`. It removes block comments with `Regex.Replace(script, @"<#.*?#>", "", RegexOptions.Singleline)` and then line comments with `Regex.Replace(..., @"(?m)#.*$", "")`. Neither script has a `#` inside a string, so this is safe for these two files. The `.DESCRIPTION` blocks mention "exit 4" and `--no-restore`, which must not count.
   - New `[Fact] Wrapper_BuildsOnlyThroughTheBuildScript`: the raw wrapper text contains `Build-AgentWindowsUat.ps1`. The stripped text contains no `Invoke-DotNet`, no match for `(?m)^\s*&\s*dotnet\b` and no match for `'(build|publish|restore)',`.
   - New `[Fact] BuildScript_NeverCallsExit`: the stripped `Build-AgentWindowsUat.ps1` has no match for `(?im)^\s*exit\b` (R-NOEXIT). At `0dc3dd6` the wrapper's own stale-delete `exit 4` (line 132) leaves with the replaced block, and the build script refuses with `throw`.

6. **New `tests/Dhadgar.Agent.Windows.Uat.Tests/UatBuildPlanTests.cs`** (Windows plus pwsh: skip when `!OperatingSystem.IsWindows()` or `PwshScript.ResolvePwsh()` is null). It runs `scripts\uat\Build-AgentWindowsUat.ps1 ... -ListSteps` and parses stdout with `using var doc = JsonDocument.Parse(...)` into `private sealed record PlanStep(string Kind, IReadOnlyList<string> Arguments, string? Path)`. The JSON property names are `Kind`, `Arguments` and `Path`. `-AgentOut` is `Path.Combine(Path.GetTempPath(), "p32-plan", "agent")`; `-ListSteps` never creates it. Timeout 2 minutes.

   ```csharp
   private static void AssertLockSafe(IReadOnlyList<PlanStep> plan)
   {
       var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
       foreach (var step in plan.Where(s => s.Kind == "dotnet"))
       {
           var verb = step.Arguments[0];
           var project = step.Arguments[1];
           switch (verb)
           {
               case "restore":
                   Assert.Contains("--locked-mode", step.Arguments);
                   Assert.DoesNotContain(step.Arguments, a => a is "-r" or "--runtime"
                       || a.Contains("RuntimeIdentifier", StringComparison.OrdinalIgnoreCase));
                   restored.Add(project);
                   break;
               case "build":
               case "publish":
                   Assert.Contains("--no-restore", step.Arguments);
                   Assert.True(restored.Contains(project), $"{verb} {project} has no locked restore before it");
                   break;
               default:
                   Assert.Fail($"unexpected dotnet verb '{verb}'");
                   break;
           }
       }
   }
   ```

   | Test | Asserts |
   | --- | --- |
   | `BuildPlan_EveryVerbAndMode_RestoresLockedWithoutRidBeforeEachBuild` (`[SkippableTheory]`: `("preflight", null)`, `("cleanup", null)`, `("stack-only", null)`, `("run", "Process")`, `("run", "Service")`, `("run", "Msi")`) | exit 0; `AssertLockSafe(plan)`; at least one `dotnet` step |
   | `BuildPlan_RunProcess_PublishesTheAgentForWinX64IntoAgentOut` | exactly one `publish` step; its project ends with `Dhadgar.Agent.Windows.csproj`; it has `-r` followed by `win-x64` and `-o` followed by the given `-AgentOut`. This pins the RID publish, so the theory above can never pass just because `-r` was dropped. |
   | `BuildPlan_RunMsi_DeletesStaleOutputBeforePublishAndBuildsInstallerLast` | the two `remove` steps come before the `publish`; their paths end with `bin\Release\net10.0\win-x64\publish` and `Installer\bin\Release`; the `publish` has no `-o`; the last step is `build` of `Dhadgar.Agent.Windows.Installer.wixproj` |
   | `BuildPlan_PreflightAndCleanup_BuildOnlyTheDriver` | for `preflight` and `cleanup`, the only `dotnet` steps are restore and build of `Dhadgar.Agent.Windows.Uat.csproj` |
   | `BuildPlan_RunProcessWithoutAgentOut_Fails` | exit code not 0; stdout holds no JSON array |

7. **`scripts/uat/README.md`**:
   - New section `## How the script builds (P32, D-S241)`, placed before `## Parameters`. Write it in your own words, covering four points. First, the wrapper builds through `Build-AgentWindowsUat.ps1`: each project gets `dotnet restore --locked-mode` without a runtime identifier, then `build`/`publish --no-restore`. Second, the reason: a self-restoring `publish -r win-x64` adds a `net10.0/win-x64` section to the lock files of `Agent.Core`, `Contracts` and `Shared`, the tree turns dirty and the driver refuses the agent run (D-S239). Third, NU1004 means a project's packages changed without its lock file: on a development tree, run `dotnet restore <project>` without `--locked-mode`, commit the lock file and run again; an agent run only reports it (exit 4, before the driver starts, so no machine change). Fourth, `-ListSteps` prints the plan without building.
   - The `-NoBuild` row in Parameters becomes "Skips `Build-AgentWindowsUat.ps1`. The driver exe must already exist."
   - The exit-code row for 4 becomes "Internal error: a failed build or locked restore (before the driver starts, nothing changed), or a driver error (cleanup still ran)".
   - In "MSI mode", change "The script publishes the agent without `-o`" to "The build script publishes the agent without `-o`".

Do not touch any other file. Check 3 enforces it.

### Tests and why they fail on revert

- If the build script goes back to the old self-restoring publish, the theory fails ("no locked restore before it", or "--no-restore" missing).
- If someone drops `-r win-x64` to make the theory pass, `BuildPlan_RunProcess_PublishesTheAgentForWinX64IntoAgentOut` fails.
- If the wrapper builds inline again, `Wrapper_BuildsOnlyThroughTheBuildScript` fails.
- If an `exit` is added to the build script, `BuildScript_NeverCallsExit` fails.
- `UatScriptTests` keeps the P23 security pins on the delete-before-publish order and on the containment check.

The plan tests are skipped on Linux CI. The executed checks below, any Windows `dotnet test` and the UAT runner's next run cover them. The driver's post-build tree check (D-S239) stays the fail-closed backstop: a regression gets a refused run, never a dirty machine.

### Checks, in order (from the packet worktree, `<wt>`)

1. `dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true`
2. `dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`. Every test passes, and none of the new `UatBuildPlanTests` is skipped (this is Windows with pwsh).
3. `git -C <wt> diff --stat 0dc3dd6..HEAD` after the commit lists only the 7 files above. Any other file is `blocked spec`.
4. **Commit**, then run the executed checks on the committed tree. `$tree` below means `git -C <wt> status --porcelain=v1 --untracked-files=all -- . ':(exclude)docs' ':(exclude).claude'`. `$chk` is `$env:TEMP\p32-check`, outside the repo; delete it first if it exists.
   - **E0** `$tree` prints nothing.
   - **E1** `pwsh -NoProfile -File <wt>\scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Process -AgentOut $chk\agent` exits 0, `$chk\agent\Dhadgar.Agent.Windows.exe` exists, and `$tree` prints nothing.
   - **E2** `pwsh -NoProfile -File <wt>\scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi` exits 0, there is exactly one `MeridianConsoleAgent.msi` under `<wt>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release`, and `$tree` prints nothing. If `wix.exe` reports WIX0216 0x57, that is a path-length limit of the machine and not this packet: rerun the same command once from a `subst` drive mapped to `<wt>` (`subst P: <wt>`, run from `P:\`, then `subst P: /d`) and record both outcomes.
   - **E3** `pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight`: the exit code is not 4, the output has no `Invoke-AgentWindowsUat failed` line, the check table is recorded, and `$tree` prints nothing.
   - **E4 (mutation, executed)**: in `Build-AgentWindowsUat.ps1`, delete the Process branch's `restore $agentProject` line and `'--no-restore', ` from its publish line, then rerun E1 with `-AgentOut $chk\mut\agent`. Expected: exit 0, and `$tree` lists exactly `src/Agents/Dhadgar.Agent.Core/packages.lock.json`, `src/Shared/Dhadgar.Contracts/packages.lock.json` and `src/Shared/Dhadgar.Shared/packages.lock.json` besides the script. Then run `git -C <wt> checkout -- scripts/uat/Build-AgentWindowsUat.ps1 src/Agents/Dhadgar.Agent.Core/packages.lock.json src/Shared/Dhadgar.Contracts/packages.lock.json src/Shared/Dhadgar.Shared/packages.lock.json`. This is the only `checkout` allowed, and only on these four paths. `$tree` must print nothing again.
   - **E5 (mutation, tests)**: make the same edit as E4 and run `dotnet test ... --filter FullyQualifiedName~UatBuildPlanTests`; it must fail. Restore the script with `git checkout -- scripts/uat/Build-AgentWindowsUat.ps1`. Then add a line `& dotnet build $driverExe` inside the wrapper's `try` block and run `--filter FullyQualifiedName~UatScriptTests`; it must fail. Restore with `git checkout -- scripts/uat/Invoke-AgentWindowsUat.ps1`. Finally `$tree` prints nothing.
5. `git -C <wt> grep -n -e "'-r', 'win-x64'" -- scripts` shows only the two `Add-DotNet` publish lines in `Build-AgentWindowsUat.ps1`, both with `'--no-restore'`.

Do not run `-StackOnly` or any `-Mode` through the wrapper. Delete `$chk` at the end.

**Owed to the UAT run:** the runner's next `-Mode Process` at the new w1 tip gets past the driver's post-build tree check, and `agentRun.treeClean = true` appears in `results.json`.

### Docs owed

`scripts/uat/README.md` (step 7) and the wrapper's `.DESCRIPTION` (step 2). Ledger text is the architect's and is done.

### Review after

`packet-reviewer` and `security-advisor`, normal loop. The security brief checks:

1. The tree-check block and the order of the wrapper's guards are unchanged, and the build still runs after them.
2. `Build-AgentWindowsUat.ps1` never calls `exit`, and every failure reaches the wrapper's `catch` (exit 4).
3. The stale delete keeps its containment check.
4. No `dotnet` command is built as a string.
5. `-ListSteps` executes nothing.
6. No lock file, csproj, props or workflow changed.
