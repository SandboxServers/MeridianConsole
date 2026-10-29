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

## Continuation 2 (campaign tip b9e693f0, 2026-10-28)

Step 1, rebuild and clean start:
- No Dhadgar.Lab or meridian-lab process was running (none by command line). Port 25000 was held by Docker Desktop's own backend (com.docker.backend), not a lab process.
- Leftover stack from continuation 1 (no daemon, built from 339c6ca1-dirty images): six containers with label meridian-lab=1 (gateway, nodes, toxiproxy, redis, rabbitmq, postgres) and network meridian-lab-net. Removed label-only (`docker rm -f` on ids filtered by meridian-lab=1; `docker network rm meridian-lab-net`). No prune, no volume removal. The host state directory bound to nodes was left in place.
- Deviation: this teardown ran before the Docker tests, not at step 4. Reason: the orphaned fixed-name containers (toxiproxy holds 25000 and 25443) would collide with every Docker test's own stack, and no daemon owned them to tear down later.
- Build: `dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-incremental -p:TreatWarningsAsErrors=true` with CI=true: Build succeeded, 0 warnings, 0 errors (16.3 s). `dotnet build src/Tools/Dhadgar.Lab/Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded.

Step 2, previously failing Docker tests (one at a time, `dotnet test --no-build -c Release`, logs in the scratchpad as w5-step2-*.log, summary in w5-step2-summary.txt):
| Test | Exit | Result | Detail |
| --- | --- | --- | --- |
| MutationDockerTests.M1_TokenNoConcurrency_IsDetected | 1 | Failed (6 s) | `Lab.LeaseRequired`: "lease_id is required for this tool" (the test does not hold a lease for `mutation_run`; the earlier `Assert.False` was masking this) |
| TenantTwinSeamTests.TenantIsolationEnroll_Holds | 1 | Failed | `twin_run` `Lab.NotFound`: "no such spec" (expected red, unchanged) |
| TwinLatencyDockerTests.EnrollLatencyInvariance_Holds | 1 | Failed (1.3 min) | Total 1 failed; detail not captured in the step 2 summary (see w5-step2-TwinLatencyDockerTests.log) |
| LabSelfTestE2ETests.LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten | 0 | Passed (1.1 min) | I7 skip now accepted |
| SelfCheckRecoveryTests.Recovery_RestartShowsNoToxicLeaseOrI5Orphan | 1 | Failed (1 m 13 s) | `[Lab.Timeout]` daemon did not answer /status; inner cause `[Lab.Refused] the target is not a lab endpoint` (the port the child daemon tried is held by something that is not a lab endpoint, likely Docker's com.docker.backend on 25000 or another listener) |
| EnrollHistorySeamTests.EnrollRace_IsLinearizable_LostResponse_IsNotUnknown | 0 | Passed (46 s) | lease TTL and teardown fixes hold |

Step 2 result: 6 run, 2 passed (LabSelfTest, EnrollRace), 4 failed (M1, TenantTwin expected red, TwinLatency, SelfCheckRecovery). Summary file: scratchpad w5-step2-summary.txt.

Open for step 3: the live gates were not started in this session. The SelfCheckRecovery refusal points at a port conflict on the child daemon's listen port, which must be checked before the next run.

## Continuation 3 (live gates, campaign tip b9e693f0, 2026-10-29)

Setup: daemon started from Release `meridian-lab.exe` (pid 161388 at start) with `Lab__RepoRoot` = worktree and `MERIDIAN_LAB_TOKEN` from the scratchpad token file. Lease `92b6c842...` acquired, `lab_build` succeeded (images `b9e693f0-dirty-20261029035701`), `stack_up` wipe up, `svc_start nodes` and `svc_start gateway` up, then the lease was released (OwnLease tools run with no lease held).

| Gate | Job | Result |
| --- | --- | --- |
| hunt enrollment seed 4242 budget 600 | 9de49e6641fd | succeeded, `diverged` at op 0 `Mint(60)`: `db_query failed`, known 0 (same as continuation 1) |
| hunt replay of 9de49e6641fd repro | 6632091777f4 | succeeded, `reproduced` at op 0, same detail, known 0 |
| fuzz enroll, 50 cases, seed 1613996116619049538 | 5ebd089ca5bc | succeeded, sent 50, known 4; many failures are `status 500` on `POST /api/v1/agents/enroll` (mutations numeric-extreme, truncated-json, duplicate-key, swap-type, empty-body, ...). Failure count not yet tallied; the job result has the full list. |

Not started: chaos `nodes-enroll`, soak, mutation M1/M4/M5, twin `enroll-latency-invariance` (already red in step 2), coverage_report.

Next step (continuation 4): tally the fuzz failures from job 5ebd089ca5bc (the full result, not the truncated log) and check whether the 500s are known issues. Then re-acquire a lease for the Leased steps (mutation_run, twin_run) and run chaos `nodes-enroll` (duration_s 600), soak, mutation M1/M4/M5, twin, and coverage_report. Finally stop the daemon (pid 161388 or whatever pid holds 8789), release any lease, and `stack_down` with `wipe` as needed. Remove any leftover `meridian-lab` containers and the `meridian-lab-net` network.

Last check result line: none in this continuation (live gates only; no dotnet check run).

## Continuation 4 (finisher, campaign tip 08571257, daemon still the b9e693f0 build, pid 161388)

| Gate | Job | Result | Suspected cause / note |
| --- | --- | --- | --- |
| chaos `nodes-enroll`, duration_s 600, seed 9959511619756607412 | 9e4fc0d6428b | PASS (succeeded): faults_started 23, refused 11, healed 3, heal_failed 0, workload_loops 251, workload_errors 0, checkpoints 18, violations 0, check_errors 0 | none; 11 refused faults not examined |
| soak `catalog/nodes.toml`, duration_s 300, seed 4912860869376222741 | 8c06fd8e2de8 | PASS-WEAK (succeeded): loops 1, workload_errors 0, samples 1, sample_errors 0, i7 "skipped" | one loop of the spec ran the whole 300 s (the spec run itself is slow) so only one memory sample was taken and I7 had too few samples to judge; the job took about 9 min wall |
| mutation M1-token-no-concurrency, M4-no-concurrency-catch, M5-token-logged (no lease held; `mutation_run` is OwnLease and returns Lab.LeaseHeld if the caller holds one) | 307d653411f6 | FAIL (verdict not `detected`): all three `detected_other`, job succeeded in 1.3 min, restore ok (b9e693f0-dirty-20261029035701). M1 and M4 signature `inv:I6\|meridian-lab-nodes\|restart`; M5 signatures `inv:I1\|nodes\|unhandled`, `inv:I6\|gateway\|process_exit`, `inv:I6\|meridian-lab-nodes\|restart` | the nodes container restarts/throws unhandled on any mutant build, so the generic I6/I1 signature appears instead of each detector's expected one; same nodes crash seen in the twin run, so the stack itself is unhealthy under these runs (likely the Mint(60) `db_query failed` cause too). Not investigated in the container logs. |
| twin `enroll-latency-invariance` | base ec5290, variant c3e171 | FAIL: overall fail, held null, twin row `skipped` "twin precondition: enroll-once Fail"; both worlds signature `inv:I1\|nodes\|unhandled` and `inv:I6\|gateway\|process_exit`; variant also `clause:catalog-twin-enroll/enroll-once/enroll-201\|Unverified` | enroll-once precondition fails because nodes throws an unhandled exception during enroll in the lab stack (same root as hunt Mint(60) and the mutation crashes) |
| coverage_report (all runs since daemon start) | n/a | RAN (no error): route_status 0/72, error_code 0/29, transition 0/22, invariant_check 3 hit, fault_target total not reported, outside_universe empty | hit counts of 0 on route/error/transition mean the coverage index is not fed by these job kinds (or the ingests did not record route hits); not investigated |

### Teardown (continuation 4)
Lab daemon pid 161388 stopped (taskkill on that pid only; port 8789 free). Lease 2b588a31... released before the mutation job; no lease held at the end (the daemon is gone). Stack removed label-only: six containers with label meridian-lab=1 (`docker rm -f` by id) and network meridian-lab-net; no volumes existed, no prune, dev stack untouched.

### Full non-Docker suite at tip 08571257 (Release, CI=true, TreatWarningsAsErrors, filter `RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests`)
- `dotnet build Dhadgar.sln`: exit 1, only because Dhadgar.Panel and Dhadgar.Scope `npm run build` failed (MSB3073; frontend toolchain in this worktree, not a C# error). Every C# project built; Dhadgar.Lab.Tests and Dhadgar.Gateway.Tests were then rebuilt alone: 0 warnings, 0 errors. Tests ran with --no-build over that output. Logs: scratchpad w5-final-build.log, w5-final-test.log.
- Result: 27 test assemblies; all passed except two tests. Lab.Tests 1373 passed, 5 skipped, 1 failed of 1379 (5 m 35 s); Gateway.Tests 154 passed, 1 failed of 155; Nodes 494 passed; Identity 224; ServiceDefaults 153; Secrets 154; Agent.Windows 430 (1 skipped); Agent.Windows.Uat 292; Agent.Core 134; others all green.
- Failing test 1: Dhadgar.Lab.Tests.LabIsolationTests.LabIsolation_ProductFilesDoNotNameLab. Cause: the sln build created ignored `src/Dhadgar.AppHost/{bin,obj}` whose files contain the worktree path (`.claude/worktrees/meridian-lab`); the scanner flags them. Environmental (build output), not a code defect. After deleting those two ignored output directories (created by this run) the test passes.
- Failing test 2: Dhadgar.Gateway.Tests.AgentCertificateForwardingTests.AgentsRoute_WithForeignIssuer_IsRejected. Passed on rerun (all 12 AgentCertificateForwardingTests pass alone) after rebuilding Gateway.Tests; treat as a parallel/stale-build flake.
- Rerun alone: LabIsolationTests + BarrierProxyTests + HistoryRunJobTests: 44 passed, 0 failed. BarrierProxyTests passed in the full run (port 25000 was free). HistoryRunJobTests heal-once did not flake.

### Assessment of the 4 still-failing Docker tests (one line each)
- M1 LeaseRequired (MutationDockerTests): live run shows `mutation_run` is an OwnLease tool (Lab.LeaseHeld when the caller holds a lease, works with none) while the test passes its own lease_id, so the test and tool disagree on lease ownership; additionally the live M1 verdict is `detected_other` (I6 nodes restart), so even after fixing the lease the assertion `detected` would fail.
- TenantTwinSeamTests "no such spec": the experiment id is not found by `twin_run`; only `twins/enroll-latency-invariance.toml` ships under docs/guides/uat-specs/twins, so the tenant-isolation spec file is absent (expected red).
- TwinLatencyDockerTests: live `twin_run enroll-latency-invariance` reached both worlds but failed the `enroll-once` precondition with `inv:I1 nodes unhandled` plus gateway process_exit; the stack_reset_data failure seen earlier was a fresh-stack variant, the live cause is nodes throwing during enroll (also `libgssapi_krb5.so.2` missing noise in the nodes container).
- SelfCheckRecoveryTests not-a-lab-endpoint: the child daemon probes a port that answers but is not a lab endpoint (Docker Desktop backend holds 25000 on this host, toxiproxy also wants 25000); a port-selection conflict with the host, not a recovery defect.
