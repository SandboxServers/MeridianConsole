# ml-wf-r11-boundary4 (R11-1): wave 5 boundary pass 4 fixes

Ruling: `wave5-boundary4-ruling.md` section 5. Fix notes: `wave5-boundary-review-4-R1.md` (N1) and `-R3.md` (N1).

## Changes

- (a) R1-N1: `tests/Dhadgar.Lab.Tests/Uat/ShippedSpecsRegistryValidateTests.cs` validates every top-level `*.toml` except `known-issues.toml`, plus the catalog block. Asserts that `lab-selftest.toml` and `enroll-probe.toml` are in the list. Class summary updated. No `blocked spec` line: the test is green on `enroll-probe.toml`.
- (b) R3-N1 (D-ML139): `src/Tools/Dhadgar.Lab/README.md` CLI paragraph states the 8789-only rule and `[Lab.Refused]`. `LabCli.cs` `--port` descriptions for `status` and `uat run` say only 8789 is accepted. `LabHosts.IsDaemonEndpoint` doc comment reworded (comment only).
- (c) README paragraph on `enroll-probe.toml` after the `lab-selftest.toml` paragraph.
- (d) H1: `FlakeCheckJob.PlanAsync` refuses a `rows` entry longer than 200 characters or outside `^[a-z0-9-]+(/[a-z0-9-]+)?\z` with `[Lab.InvalidArgument]`, without echoing it. Tests `PlanAsync_RowArgTooLong_IsInvalidWithoutEcho` and `PlanAsync_RowArgWithNewline_IsInvalidWithoutEcho` added to `FlakeCheckJobTests.cs`.
- (e) H2: `HuntModelTool.ReadReproAsync` caps the repro at 64 KiB and catches IO failures. `ReplayAsync` uses it. New tests in `tests/Dhadgar.Lab.Tests/Hunt/HuntReproReadTests.cs`.
- (f) `ml-l27b.md`: superseded note appended.

## Check results

- `dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded, 0 Error(s).
- `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded, 0 Error(s).
- Targeted filter (`ShippedSpecsRegistryValidateTests|FlakeCheckJobTests|HuntReproReadTests`): Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21.
- Full non-Docker run (`RequiresDocker!=true`): see the coder report.
- `git grep -n "only 8789" -- src/Tools/Dhadgar.Lab`: shows `LabCli.cs:51` and `LabCli.cs:67`. The README line does not contain the phrase "only 8789" (the verbatim text in (b) reads "talk only to `http://127.0.0.1:8789`"), so the pattern does not match it. The README line is present and was checked by `git grep -n "8789" -- src/Tools/Dhadgar.Lab/README.md`.

## Left

Nothing in R11-1. The ruling's next step is the packet-reviewer and security-advisor ruling check (not part of this packet).
