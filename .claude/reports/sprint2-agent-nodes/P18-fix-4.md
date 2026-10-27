# P18 review fix, pass 4

Applied fix notes N1-N4 from `.claude/reports/sprint2-agent-nodes/P18-ruling-check.md`. Formatting only (blank lines and trailing newlines).

## Changes

- N1 `scripts/uat/README.md`: blank line inserted before `## Parameters`.
- N2 `tests/Dhadgar.Agent.Windows.Uat.Tests/CleanupEngineTests.cs`: blank line before `private static readonly RegistryValueEntry TokenValue`; file now ends with a single `\n` (verified last bytes `7D 0A`).
- N3 `tests/Dhadgar.Agent.Windows.Uat.Tests/RunPathsTests.cs`: blank line before `[Fact]` of `StackImages_AreTheNodesAndGatewayReleaseBuilds`; file now ends with a single `\n`.
- N4 `tests/Dhadgar.Agent.Windows.Uat/Cleanup/IMachineActions.cs`: blank line between the `ProcessImage` record's closing `}` and the `IMachineActions` `/// <summary>`.

Note: after my edits, the tool reported that `CleanupEngineTests.cs` and `RunPathsTests.cs` changed on disk. I re-read the regions the notes name; the fixes were present and I did not revert or alter anything else.

## Checks

- `dotnet build tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
- `dotnet test tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj --no-restore`: Passed! - Failed: 0, Passed: 73, Skipped: 0, Total: 73.

Elevated UAT modes were not run.
