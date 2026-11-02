# ml-wf-r9-flake-deps worknote (R9-1, KG-W5-16, F115)

## Done

- `RowFilterCheck.Close(UatSpec, IReadOnlyCollection<string>)` returns the filter plus its transitive `depends_on` closure in spec file order. Unknown ids pass through so `Check` still reports `[Lab.NotFound]`.
- `FlakeCheckJob.WithSpec(FlakeCheckPlan, UatSpec)` returns the plan with `Rows` closed; `Keys` unchanged; `Result.Failure` when `Check` fails on the closed rows.
- `FlakeCheckTool.ExecuteAsync` (JobTools.cs) loads the source run's spec and calls `WithSpec` after `PlanAsync`.
- Tests: `RowFilterCheckTests.Close_AddsTransitiveDependencies_InFileOrder`, `FlakeCheckJobTests.WithSpec_FailedRowWithDependency_PassesUatRunFilterCheck`, `FlakeCheckJobTests.WithSpec_UnknownRow_IsNotFound`.

## Left

- Nothing in code. Commit of the five source/test files is the last step.

## Next step

- Commit the five files by explicit path with the packet message.

## Last check

- `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-restore --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`: Passed! - Failed: 0, Passed: 1458, Skipped: 5, Total: 1463.
- Build of `tests\Dhadgar.Lab.Tests` (Release, TreatWarningsAsErrors=true): 0 errors. Warnings reported only from `Dhadgar.Agent.Windows` (IL2026, pre-existing, outside this packet).
