# wf-d worknote: wave5-fix-D (mutants and self-check)

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
