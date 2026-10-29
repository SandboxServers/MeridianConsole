# wf-g worknote (wave5-fix-G)

## Done
- Item 1: `tests/Dhadgar.Lab.Tests/Mutants/MutationDockerTests.cs` InvokeAsync puts `lease_id` in the arguments when leaseId is not null (context lease kept). Other Docker test files already pass lease_id in arguments (TwinLatencyDockerTests, FaultSmokeE2ETests, LabSelfTestE2ETests), so no further edits.
- Item 2: `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckRecovery.cs` CallToolAsync guard is `LabHosts.IsDaemonEndpoint(uri)`. Grep of src/Tools shows no other IsLabEndpoint call on a daemon URL (8789); the others are service, barrier, infra or toxiproxy URLs.
- Tests: `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryGuardTests.cs` (non-Docker, pure).

## Left
- Nothing. Commit is the last step.

## Last check
- dotnet test (non-Docker filter, LabHosts|SelfCheck): Passed 61, Failed 0.
