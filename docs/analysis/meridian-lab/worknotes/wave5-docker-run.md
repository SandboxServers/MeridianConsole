# Wave 5 boundary Docker and live-gate run (D-ML74)

Status: BLOCKED (context) after the Docker sets. Live gates (section 3) were not started. Campaign tip 3b24617f, worktree meridian-lab, Docker Linux engine (context desktop-linux, server 29.1.3). Not committed (ops commits).

Scratch logs (directory): C:\Users\Steve\AppData\Local\Temp\claude\C--Users-Steve-source-projects-MeridianConsole\13a71c0c-7736-4538-b4b0-ff44919e5023\scratchpad (w5-batch1.log, w4-set.log)

## Checks

| # | Check | Filter | Result |
| --- | --- | --- | --- |
| 0 | Build Lab tests, Release, TreatWarningsAsErrors | dotnet build tests/Dhadgar.Lab.Tests | Build succeeded (0 errors) |
| 1 | Wave 5 Docker and E2E batch | FullyQualifiedName~HunterDockerTests / TwinLatencyDockerTests / MutationDockerTests / SelfCheckRecoveryTests / SelfCheckE2ETests / LabSelfTestE2ETests, Seam=S-L12 | Failed: 5, Passed: 2, Total: 7 (exit 1; SelfCheckRecovery 16m15s, SelfCheckE2E 30m35s) |
| 2 | Wave 4 Docker set | BlastRadiusDockerTests / FaultSmokeE2ETests / ToxiproxyTimeoutToxicDockerTests / Seam=S-L7 / Seam=S-L11 | Failed: 1, Passed: 4, Total: 5 (exit 1; EnrollHistorySeamTests 10m10s) |
| 3 | Live gates (hunt, fuzz 50 enroll, chaos, soak, mutation, twin, coverage_report) | not started (context) | pending |

Batch 1 passed: HunterDockerTests.ThreeIterations_AgainstRealLab_FinishWithResultAndBundle (2m06s); SelfCheckE2ETests.SelfCheck_Lm1_IsCaught (30m35s).

## Failures (name, message, suspected cause; no fixes made)

Wave 4 set, failure 6. EnrollHistorySeamTests.EnrollRace_IsLinearizable_LostResponse_IsNotUnknown (S-L11, 10m10s). Message: System.InvalidOperationException : lab_lease_release failed: {"error":{"code":"Lab.LeaseRequired","message":"no lease is held; acquire one first"}}. Suspected cause: the test's teardown releases a lease that is no longer held (the run outlived or dropped its lease); the linearizability assertion itself was not reached, so this is not the D-ML49 lost-response case. Needs a look at the lease lifetime in the history run; not investigated further.

Wave 4 set passed: BlastRadiusDockerTests.DecoyContainers_AreRefused_AndKeepRunning; FaultSmokeE2ETests.FaultSmoke_PostgresCut_DbPathFailsThenRecovers (F67 cut held); ToxiproxyTimeoutToxicDockerTests.TimeoutToxic_AddedToAnOpenConnection_HoldsTheNextRequest; ToxiproxySeamTests.ProxyFaults_ReachGatewayNodesDbAndRabbitPaths_ThenResetClearsToxics.


1. MutationDockerTests.M1_TokenNoConcurrency_IsDetected (6 s). Message: Assert.False() Failure, expected False, actual True at MutationDockerTests.cs:62 (Assert.False(call.IsError) after the mutation_run dispatch). The error payload is not in the log. Suspected cause: mutation_run refused the call before the job started (a lease, a build or a target precondition). Needs the error body; not captured by the test.
2. TenantTwinSeamTests.TenantIsolationEnroll_Holds (S-L12, expected red per plan). Message: twin_run failed: {"error":{"code":"Lab.NotFound","message":"no such spec"}}. Suspected cause: the tenant-isolation-enroll experiment spec is not found by the daemon (spec id or shipped file missing). Expected red; record only.
3. TwinLatencyDockerTests.EnrollLatencyInvariance_Holds (L53). Message: twin_run failed: {"error":{"code":"Lab.Unavailable","message":"stack_reset_data failed"}}. Suspected cause: the stack reset step failed on the Docker host (no stack or a container reset failure); the reset error detail is not surfaced.
4. LabSelfTestE2ETests.LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten (S-L6, L25 ingest assertions). Message: unexpected invariant skip: I7, source soak, reason "no soak is running", row nodes-up. Suspected cause: the lab-selftest run treats the I7 soak invariant as a skip the test does not allow (a skip that is not on the allow list); product or spec issue, not Docker.
5. SelfCheckRecoveryTests.Recovery_RestartShowsNoToxicLeaseOrI5Orphan (16m15s). Message: [Lab.Timeout] the lab daemon did not answer /status in time. Suspected cause: the test needs the user's daemon stopped (D-ML90) and the daemon it starts did not answer /status; the test's own daemon start or the port was not ready. Not investigated further.

## Notes

- No lab daemon was running at start (port 8789 free, no MERIDIAN_LAB_* env). Live gates need a daemon started by this run.
- Other dotnet processes exist on the host; they are not touched.


## Next step (for the continuing run)

Start the live gates (section 3) on a fresh session. Steps: `dotnet run --project src\Tools\Dhadgar.Lab -- daemon run` with `MERIDIAN_LAB_TOKEN` (from `meridian-lab token new`); call `/mcp` with `Host: 127.0.0.1:8789`, bearer token, `lab_lease_acquire`, then `lab_build` and `stack_up` (both need `lease_id`); then `hunt_model_run` (10-minute budget, seeded, then `replay` if it finds one), `fuzz_run` target `enroll` at 50 cases, `chaos_run` `nodes-enroll` (10 min), soak, `mutation_run` M1, M4 and M5, `twin_run`, and `coverage_report`. Stop the daemon and release the lease at the end. Ports 8789 and the stack ports must be free first.

Last check result line: `Total tests: 5`, `Test Run Failed` (wave 4 set).
Open question for ops: failure 1 (M1) needs the `mutation_run` error payload, which the test does not log.

## Continuation 1 (live gates, 2026-10-28/29)

Start state: no `meridian-lab-*` containers, networks or volumes; port 8789 free; no `MERIDIAN_LAB_*` env. Dev stack (`*-630acec0`, `*-db24fb3e`, `*-5e12db55`, `*-81c9fdc8` containers) untouched. Engine: linux 29.1.3, context desktop-linux.

Setup that worked: `Release` build of `src\Tools\Dhadgar.Lab` (assembly `meridian-lab.exe` under `bin\Release\net10.0`); daemon started with `Lab__RepoRoot` set to the worktree (the daemon refuses `lab_build` without a repo root; `install.ps1` normally writes it). Token and lease ids are in the scratchpad, not here.

| Step | Result |
| --- | --- |
| daemon `/status` | 200, stack down |
| `lab_lease_acquire` | lease held (owner wave5-live) |
| `lab_build` checkout = worktree | succeeded in 18 s; images `meridian-lab/{gateway,nodes,probe}:339c6ca1-dirty-*` (dirty because of untracked files) |
| `stack_up` | state up, network present; postgres, rabbitmq, redis, toxiproxy running (restarts 0) |

Live gate results: pending (update below as each run finishes).

| Gate | Run | Result |
| --- | --- | --- |
| hunt (setup gap first) | `hunt_model_run enrollment seed 4242 budget 600`, job 6fc349245077 with nodes and gateway not started | failed `[Lab.Unavailable]` after the full 600 s budget; setup gap, not a product result. `svc_start nodes` and `svc_start gateway` were needed (the test helper does the same). |
| hunt (rerun) | seed 4242, budget 600, nodes and gateway up, job 7677e4a93384 | job succeeded (10 min). Verdict `diverged` at op 0 `Mint(60)`: `db_query failed`; run.json `overall: fail` |
| hunt replay | `replay 7677e4a93384/repro/repro.toml`, job 6ce22241a12a | `reproduced` at op 0, same detail (2 s) |

Notes: `lab_lease_acquire` IDs and `Lab.LeaseHeld` mean an OwnLease tool (hunt, fuzz, chaos, soak, mutation, twin) must run with no lease held by the caller. The lease is released before each one and re-acquired for the Leased steps. A `lab_build` checkout ID is the worktree root.
