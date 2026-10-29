# wf-c worknote: wave5-fix-C (chaos, history, Docker test harness)

This is the wave5-fix-C worknote (ruling name `wave5-fix-C.md`).

## Pass 2 additions (ruling line 495)

- **D-ML103 rule:** one serial gate for every Docker, stack, port-8789 or mutex test. Every listed class carries `[Collection(LabDockerGate.Name)]`, and `LabDockerGateTests` is a reflection guard that fails when a listed class loses it. Docker tests use `ttl_s = 3600`.
- **Moved classes:** the Docker-gate collection list covers the LabHome lane (TwinLatency, Mutation, LabSelfTest, TenantTwin) and the other Docker classes that touch the stack, port 8789 or the mutex.
- Pass 2 (review N4): the cleanup sweeps in `TwinLatencyDockerTests` and `TenantTwinSeamTests` match label AND name prefix `^meridian-lab-`. `TenantTwinSeamTests` also has the `MERIDIAN_LAB_S6_WIPE` pre-check.

Packet: `wave5-fix-C` in `worknotes/wave5-ruling-1.md`. Rulings: D-ML101, D-ML102, D-ML103, D-ML104 (L05b is a follow-up, not this packet).
Worktree state: all changes below are UNCOMMITTED in `.claude/worktrees/ml-wf-c` (branch `ml/wf-c-tools`). No check has run yet.

## Done (uncommitted, unchecked)

- C1 chaos: `ChaosPlan.MaxFaultSeconds = 900` for fault `duration_s` (message says 900); `ChaosTools.cs` and `SoakTools.cs` return `[Lab.InvalidArgument] the repo root is not configured` first; `ChaosJob.HealAllAsync` catches `Exception` per heal, adds `_healFailed`, writes a `heal_failed` timeline entry (tool name only; RunState has no logger), outcome gains `heal_failed`.
- C1 tests: `ChaosPlanTests.Plan_FaultDurationOver900_IsRejected`, `Plan_FaultDurationAt900_IsAccepted`; `ChaosJobTests.Heal_FirstThrows_SecondStillRuns_AndHealFailedIsOne`; new `tests/Dhadgar.Lab.Tests/Chaos/ChaosToolsTests.cs` (`ChaosRun_EmptyRepoRoot_IsRefused`, `SoakRun_EmptyRepoRoot_IsRefused`, with hand-written fakes; no mocking library in the project).
- C2 history: `HistoryRunJob` ctor gains `int barrierPort = LabPorts.Barrier` (field `_barrierPort`), barrier start uses it, endpoint uses `proxy.BoundPort`, `CleanupCap` is `internal`.
- C2 tests: `HistoryRunJobTests` passes `barrierPort: 0` at all three constructions; the 31 s test uses `HistoryRunJob.CleanupCap + 1 s`; test 1 asserts `HealCancelled` false; new `RunAsync_BarrierPortHeld_StillBindsAnEphemeralPort` (uses `System.Net`, `System.Net.Sockets`, `System.Globalization`).
- C3: `.gitattributes` lines 4-5 are one line (`# Twin spec tests ... keep LF on Windows autocrlf checkouts.`). Check: `git check-attr eol tests/Dhadgar.Lab.Tests/Twins/TwinRunnerTests.cs` should print `eol: lf`.
- C4 item 1: `[Collection(LabDockerGate.Name)]` on MutationDockerTests, EnrollHistorySeamTests, TenantTwinSeamTests, TwinLatencyDockerTests, FaultSmokeE2ETests, LabSelfTestE2ETests.
- C4 item 2: attribute added to BlastRadiusDockerTests, FaultKindsDockerTests, SelfCheckRecoveryTests, SelfCheckE2ETests.
- C4 item 3: new `tests/Dhadgar.Lab.Tests/LabDockerGateTests.cs` (`EveryDockerClass_IsInTheDockerGate`, reflection on `TraitAttribute` and `CollectionAttribute`; `ExplicitlyGated = ["SelfCheckE2ETests"]`). Expected to pass once item 1 and 2 are in and any other trait-tagged class is gated; if it names another class, gate that class too.
- C4 item 4: `SelfCheckRecoveryTests.Recovery_RestartShowsNoToxicLeaseOrI5Orphan` is `[SkippableFact]` with `Skip.If(Dhadgar.Lab.Daemon.ElevationCheck.IsElevated(), ...)` first.
- C4 item 5: `LabSelfTestE2ETests` skips the I7 / soak / "no soak is running" entry (all three fields) before the row assert.

## Left

- C4 item 6: `Mutants/MutationDockerTests.cs`: `LabDaemonOptions` gets `RepoRoot: LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)` and `Time: TimeProvider.System`; lease via `lab_lease_acquire` with `ttl_s = 3600` (replacing 1800 s `LeaseBook.Acquire`); then `lab_build` (checkout = repo root), `stack_up` with `wipe = true`, `svc_start` for `nodes` and `gateway`; line ~62 `Assert.False(call.IsError, call.Payload.ToString());`; `finally` releases the lease and removes lab containers without throwing (copy `EnrollHistorySeamTests`' `RemoveLabContainers`). M1 assertions unchanged.
- C4 item 7: `Twins/TwinLatencyDockerTests.cs`: same setup (`ttl_s = 3600`, `lab_build`, `stack_up` wipe, `svc_start` nodes and gateway) before `twin_run`; `finally` release does not throw.
- C4 item 8: `Seams/EnrollHistorySeamTests.cs`: line ~57 add `["ttl_s"] = 3600`; lines ~110-117 release through a non-throwing `TryReleaseAsync` that writes to test output. Assertions unchanged.
- C4 item 9: `Seams/TenantTwinSeamTests.cs`: `ttl_s = 3600` on its acquire, non-throwing release. Stays red until L56 (S-L12).
- Checks (the brief's): `dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true` with `CI=true`, then `dotnet test ... --no-build --filter "RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests"` on Chaos, History, SelfCheck, LabDockerGate and the non-Docker LabSelfTest, plus the full `LabDockerGateTests`.
- Report: `.claude/reports/meridian-lab/wf-c-coder.md`; commit `fix(lab): wave 5 review fixes C [wave5-fix-C]` only after the checks pass.

## Session 2 progress

C4 items 6-9 written (MutationDockerTests, TwinLatencyDockerTests, EnrollHistorySeamTests, TenantTwinSeamTests). Build (Release, CI=true, TreatWarningsAsErrors) succeeded, 0 warnings, 0 errors. Non-Docker filter (Chaos, Soak, History, LabDockerGate, SelfCheck, LabSelfTest): Passed 86/86. HistoryRunJobTests: first concurrent-copy run exposed that the new held-port test collided with the other copy's listener on 25080; the test now accepts AddressAlreadyInUse as the held precondition. Rebuilt; concurrent runs 1 of 3 passed (21/21 each copy). Next: runs 2 and 3, then the full non-Docker suite, then report and commit.

## Status at commit

All C1-C4 items are written and committed. Build: succeeded, 0 warnings. Affected-area non-Docker tests: 86/86. HistoryRunJobTests: 6 concurrent copies 21/21. Full non-Docker suite: 1301 passed, 14 failed (BarrierProxyTests, port 25000 held by Docker Desktop), 5 skipped. Next step for a fresh session: none in this packet unless the owner frees 25000 and asks for a rerun.

## Next step (superseded)

Continue C4 items 6-9 (read each file in ranges; they are Docker tests, so they only need to compile and pass the non-Docker filter). Then run the build, fix, run the tests, commit.

## Last check

None run yet.
