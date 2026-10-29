# wf-d worknote: wave5-fix-D (mutants and self-check)

This is the wave5-fix-D worknote (ruling name `wave5-fix-D.md`).

## Pass 2 additions (ruling line 575)

- **D-ML102 elevation preflight text:** `SelfCheckRecovery.RunAsync` returns `[Lab.Elevated] selfcheck --recovery needs a non-elevated session` when the session is elevated. Nothing is copied or built first. The recovery test skip reason is `selfcheck --recovery needs a non-elevated session (D-ML102)`.
- **D-ML102 `[Lab.` stderr first-line text:** an early child exit reports `[Lab.ProcessFailed] the lab daemon exited with code <n> before answering /status: <line>`, where `<line>` is the first child stderr line that starts with `[Lab.`, control-stripped and cut to 300 characters, or `no [Lab.] line`.
- **Pass 2 (security N1, N2):** `MutantBuilder.PatchTouchesOnly` is public and fails closed on `diff --git` lines and quoted names; `SelfCheckRunner.PatchScopeRow` runs the same check before the lab-mutant `git apply`.
- **Pass 2 (security N3):** `SelfCheckRunner.RunProcessAsync` removes `MERIDIAN_LAB_TOKEN` from every child environment.
- **Pass 2 (security N6):** `MutantSpecLoader` refuses build files (`*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.wxs`, `Directory.*`, `Dockerfile`) as mutant targets.

Branch `ml/wf-d-tools`. Packet spec: `worknotes/wave5-ruling-1.md` section "wave5-fix-D".

## Done

- Part 1 (570d2ea5): D1 source (detector allow-list, patch scope check) and forced fixture change in `MutantBuilderTests.cs:37`.
- Part 2 (73d5544c): D1 tests.
- Part 3 (a4f1d699): D2 source in `Mutants/MutationRunJob.cs` and `Tools/Mutants/MutationRunTool.cs`, forced fixture changes in `MutationRunJobTests.cs`.
- Part 4 (3beb5349): D2 tests in `MutationRunJobTests.cs`: `Detected_when_invariants_check_reports_the_expected_inv_signature`, `Invariants_check_error_is_unreachable`, `Detector_error_text_reaches_the_reason`, `Signatures_reads_ingest_signature_texts`, `Signatures_reads_a_real_ResultsService_payload`, `Reports_redact_a_vault_value_in_the_title_and_escape_pipes`. Test-only additions: `FakeInvoker.InvariantsError`, `SpecWith` helper.
- Final (60b21a81): D3 in `SelfCheck/SelfCheckRecovery.cs` (elevation preflight, `FirstLabLine` from `[Lab.` stderr, `ExitedEarly`, `ToolFailure`, `StartDaemonAsync` returns `Result<Daemon>`), and the new `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryDiagnosticsTests.cs` (3 tests).

## Left

Nothing in the packet. The report is appended in `.claude/reports/meridian-lab/wf-d-coder.md`.

## Last check

Release build (CI=true, TreatWarningsAsErrors, --no-incremental): Build succeeded, 0 Error(s), 0 Lab warnings.
Filter `RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~Mutant|FullyQualifiedName~MutationRunJobTests|FullyQualifiedName~SelfCheckRecoveryDiagnosticsTests)`: Passed 70, Failed 0.
Filter `RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests&(FullyQualifiedName~Mutant|FullyQualifiedName~SelfCheck)`: Passed 93, Failed 0.
