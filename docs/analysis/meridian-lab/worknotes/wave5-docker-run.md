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

## Continuation 6 (re-verification at fa57cd19)

Setup: Release builds of src/Tools/Dhadgar.Lab, src/Tools/Dhadgar.Lab.SelfCheck and tests/Dhadgar.Lab.Tests (CI=true, TreatWarningsAsErrors): 0 errors. Machine quiet, no labelled containers at start. Docker tests run one at a time with `--no-build`; logs in scratchpad as w5r-*.log (w5r-m1, w5r-rec, w5r-rec2, w5r-rec3, w5r-twin, w5r-twin-nodes, w5r-twin-gateway). TenantTwinSeamTests skipped (red by plan). Part B (live gates) NOT run because part A did not all pass. No daemon was started by me (the tests host their own).

| Test | Result | Full assertion message |
| --- | --- | --- |
| MutationDockerTests.M1_TokenNoConcurrency_IsDetected | FAIL (1 m 26 s) | `Assert.Equal() Failure: Strings differ` / `Expected: "complete"` / `Actual:   "succeeded"` at MutationDockerTests.cs:97 (`Assert.Equal("complete", record.State)`). |
| SelfCheckRecoveryTests.Recovery_RestartShowsNoToxicLeaseOrI5Orphan, run 1 (no Lab__RepoRoot) | FAIL (22 s) | `[Lab.Tool] stack_up failed: {"error":{"code":"Lab.Unavailable","message":"Lab:RepoRoot is not set"}}` at SelfCheckRecoveryTests.cs:33. Fix F's endpoint refusal is gone; the child daemon has no repo root. |
| same, run 2 and 3 (env `Lab__RepoRoot` = worktree; run 3 after removing a leftover meridian-lab-postgres from run 1, so not stale state) | FAIL (43 s, 40 s) | `Assert.Empty() Failure: Collection was not empty` / `Collection: ["I5 reports an orphan after the restart"]` at SelfCheckRecoveryTests.cs:34. Reproducible. |
| TwinLatencyDockerTests.EnrollLatencyInvariance_Holds (Lab__RepoRoot set) | FAIL (1 m 8 s) | `twin relation did not hold:` JSON `{"experiment":"enroll-latency-invariance","held":false,"overall":"fail","twin_row":{"id":"twin/enroll-latency-invariance","grade":"fail","reason":"relation not held: 2 difference(s)"}`, base run b0d559 (overall fail, 2 rows, signatures `twin:enroll-latency-invariance\|/rows/enroll-once/grade`, `.../steps/enroll/status`), variant run e37269 (3 signatures: the same two plus `clause:catalog-twin-enroll/enroll-once/enroll-201\|Unverified`), `diffs`: `/rows/enroll-once/steps/enroll/status` base `ok` variant `null`; `/rows/enroll-once/grade` base `pass` variant `blocked`. known_issues checked, 7 issues. Test line 93. |

Findings versus the earlier diagnoses:
- M1: the earlier lease and `detected_other` problems are past; the test now reaches the job result and fails on a wrong state literal (job states are `succeeded`, test expects `complete`). Test bug; the verdict assertion at line 98 was not reached, so whether M1 is `detected` is still unknown from this test.
- Recovery: child daemon needs Lab:RepoRoot (test environment, not set by the test; works with the env var). With it, the real failure is I5 reporting an orphan after restart (product or lab behaviour, now the true blocker).
- Twin: the nodes `Unhandled`/I1 and I6 process_exit signatures are gone (fix F/G worked). The remaining difference is in the variant world only: its `enroll` step has no status (`null`, grade `blocked`, clause enroll-201 Unverified); the base world enrolls fine. Sampled `docker logs` tail (200 lines, last snapshot, base world) of meridian-lab-nodes and meridian-lab-gateway show one clean enroll: nodes `POST /agents/enroll responded 201 in 442ms`, gateway `POST /api/v1/agents/enroll responded 201 in 474ms`, no exceptions (only the libgssapi_krb5 noise and gateway probe warnings for absent services). The variant's logs were not captured (its stack was gone before the next 3 s sample), so the cause of the blocked variant enroll is not known; the twin_run result JSON is the one in the assertion above (not otherwise retrievable, the test host is gone).

Part B: skipped (A not all pass). Remaining: fix/decide on M1 state literal, Recovery I5 orphan and RepoRoot env, the variant-world blocked enroll; then run B (hunt 4242 budget 300, mutation M1/M4/M5, twin) on a fresh daemon.

### Teardown (continuation 6)
No daemon started by me; no leases held. Removed label-only the leftover meridian-lab-postgres container (run 1) and the network where present; at the end no `meridian-lab=1` containers and no meridian-lab network remain. No prune, dev stack untouched. Nothing committed.

## Continuation 7 (boundary runner, D-ML109, campaign tip f5f8cdc5)

Setup: Docker 29.1.3 up; no `meridian-lab=1` containers; port 8789 free; `Lab__RepoRoot` unset. `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded, 0 errors, exit 0 (53 IL2026 warnings in Dhadgar.Agent.Windows, outside the lab). Docker tests run one at a time with `--no-build`, logs in scratchpad as `w5r7-A<n>-*.log`, index `w5r7-partA-index.txt`.

### Part A (Docker, one at a time)

| # | Test or filter | Result | Time | Note |
| --- | --- | --- | --- | --- |
| A1 | HunterDockerTests | Passed 1, Failed 0 | 2m 06s | |
| A2 | TwinLatencyDockerTests (L53) | Passed 1, Failed 0 | 2m 24s | was red at fa57cd19; green now |
| A3 | MutationDockerTests (M1) | Passed 0, Failed 1 | 1m 22s | `M1 verdict: ... "verdict":"survived" ... "signatures":[] ... "reason":"no signature matched the expected pattern","check_errors":0` (test line 101). Same as D-ML115 case. |
| A4 | SelfCheckRecoveryTests | Passed 1, Failed 0 | 48s | was red at fa57cd19 (I5 orphan) |
| A5 | SelfCheckE2ETests (LM1) | Passed 1, Failed 0 | 40s | |
| A6 | LabSelfTestE2ETests (S-L6) | Passed 1, Failed 0 | 1m 04s | |
| A7 | Seam=S-L12 (TenantTwinSeamTests) | Passed 0, Failed 1 (expected red) | 6s | `twin_run failed: {"error":{"code":"Lab.NotFound","message":"no such spec"}}` (same as before) |
| A8 | BlastRadiusDockerTests | Passed 1, Failed 0 | 3s | wave 4 set |
| A9 | FaultSmokeE2ETests | Passed 1, Failed 0 | 1m 08s | wave 4 set |
| A10 | ToxiproxyTimeoutToxicDockerTests | Passed 1, Failed 0 | 42s | wave 4 set |
| A11 | Seam=S-L7 (ToxiproxySeamTests) | Passed 1, Failed 0 | 50s | wave 4 set |
| A12 | Seam=S-L11 (EnrollHistorySeamTests) | Passed 1, Failed 0 | 51s | was red at Continuation 1 (LeaseRequired); green now |

Part A totals: 12 tests, 10 passed, 2 failed (A3 M1 `survived`, a must-pass item; A7 S-L12, expected red). Gate MD for M1 is not met.

The M1 test deletes its DataRoot in `finally`, so the read-only facts for D-ML115 (barrier arrivals, completions, client outcomes, build served, nodes rows) are gathered in Part B from a daemon whose home is kept, using `mutation_run` M1 again.

### Part B setup (continuation 7, fresh daemon)
- Part A not rerun (per brief). Removed the empty labelled network `meridian-lab-net` (label meridian-lab=1, no containers) label-only; no labelled containers existed.
- Build: `dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true`: 0 Warning(s), 0 Error(s).
- Daemon: fresh home `scratchpad\w5r7-home-B`; token from `meridian-lab.dll token new` saved to scratchpad `w5r7-B-token.txt` (never printed); `Lab__RepoRoot` set on the daemon process only (lab_build needs it; not on the Part A test host). Daemon pid 29500 (see `w5r7-B-daemon.pid`), port 8789.
- Lease `8c6cda8e...` (ttl 3600). lab_build build_id `f5f8cdc5-dirty-20261101171643`; stack_up wipe up (postgres, rabbitmq, redis, toxiproxy running, restarts 0); svc_start nodes up; svc_start gateway up.
- Next: release lease, hunt_model_run enrollment seed 4242 budget 300, then replay if a repro exists.

Gate 1, hunt `hunt_model_run` model enrollment, seed 4242, budget_s 300 (lease released first, OwnLease): job 4824185799f6, state `failed`, result `[Lab.Unavailable]`, after 300 s (started 17:17:44, ended 17:22:44). No repro, so no replay (not run). Audit (`home-B\audit`): the job's `svc_restart` (nodes, job:hunt, 17:17:47) outcome `Lab.Timeout` after 95402 ms; every other hunt step returned ok (stack_reset_data, fault_reset, fault_proxy_disable, db_query x2, invariants_check). No retry (the brief says record raw failures). Cause not classified (the classes R1-R4 are for D-ML115 only).

Gate 2, `mutation_run` M1-token-no-concurrency, M4-no-concurrency-catch, M5-token-logged (no lease held, OwnLease): job 7c5e7eabfd3f, state succeeded (60 s), overall `complete`, restore ok (build f5f8cdc5-dirty-20261101171643).
- M1-token-no-concurrency: verdict `stale`, detector_run_ids [], signatures [], reason "change.patch does not apply at HEAD; refresh it as tools/lab/mutants/README.md says", check_errors 0. No detector run, so the D-ML115 facts (barrier, completions, client outcomes, nodes rows) do not exist for M1 in this run. Different from Part A's `survived`.
- M4-no-concurrency-catch: verdict `survived`, 5 detector run ids (3b61aaa3..., 99197e02..., 975bf86c..., c5b09959..., 29af6b28...), signatures [], reason "no signature matched the expected pattern", check_errors 0. D-ML115 facts for M4 are gathered next (read-only).
- M5-token-logged: verdict `detected_other`, detector run efd92c, signature `inv:I1|nodes|unhandled`, check_errors 0 (not in D-ML115 scope; recorded).

### D-ML115 read-only facts (continuation 7, read-only, nothing changed)

Source: `events_read` on run 3b61aaa3-4614-4d7b-aad6-67492a588ffc (M4, first detector run; saved as scratchpad `w5r7-B-08-m4-events.json`), its `history.json`, `check.json` and `run.json`, the job's `job.json`, the audit file and the nodes and gateway logs.

- Scenario `enroll-race`, clients 4, verdict `linearizable`, grade PASS, `check.json` violations []. The checker passed this run.
- Barrier: 4 `proxy_request_arrived` (one per client), all at 17:23:58.2421 to 17:23:58.2424, spread about 0.2 ms. Not an R1 shape.
- Upstream: 4 `proxy_upstream_completed`, all at 17:23:58.5992 to 17:23:58.5993 (about 357 ms after arrival, spread under 1 ms). Gateway log: four `POST /api/v1/agents/enroll responded 400 in 324-325 ms`.
- Client outcomes: all four `enroll` complete `fail`, `status: 400`. No 201 at all.
- Nodes svc_start: audit `svc_start` (job:mutation) 17:23:52.09 to 17:23:56.01 `ok`. The image in use is `meridian-lab/nodes:mutant-M4-no-concurrency-catch-f5f8cdc5` (created 17:23:48 UTC). Nodes logs show migrations applied and "Now listening" at 17:23:55.58. The build id the svc_start returned is not stored in the job record, so the served build id is not verified; the job reports only the restore build id `f5f8cdc5-dirty-20261101171643`.
- Nodes rows: `final_state` `token` 5b60848e... `used_at` null, `used_by_node_id` null, `revoked` false; `nodes` [] (zero nodes created).
- Classification: none of R1-R4 fits. R1 needs fewer than 4 arrivals or spread over 1 s (not met). R2 needs one 201 and serial upstream (not met: zero 201, completions concurrent). R3 needs a build mismatch or a nodes start failure on the mutant (not evidenced: nodes started on the mutant image and listened; served build id not recorded). R4 needs more than one node or more than one 201 (not met: zero of each). Per D-ML116 this raises the contingent owner question. The record shows the four enrolls were rejected by Nodes with 400 before any token use, so the race was never reached in this run. The cause of the 400 is not diagnosed here.
- The other M4 detector runs (99197e02, 975bf86c, c5b09959, 29af6b28) are recorded by id only; not read.

### D-ML115 M1 (stale in this run, survived in Part A)

- Verdict `stale` at build `f5f8cdc5-dirty-20261101171643` (commit f5f8cdc5, `dirty: true`, build.json). The reason is the job's own check: "change.patch does not apply at HEAD". No detector run, so no D-ML115 facts exist for M1 in this run.
- Read-only comparison: `mutant.toml` `base` = eda18720... (the same base as M4 and M5). The file `src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs` has blob 9ea723b4 at base, at HEAD and in the worktree (`git hash-object --no-filters`), and the patch pre-image is 9ea723b4. `git apply --check` of `change.patch` at HEAD in this worktree returns exit 0. The worktree has uncommitted changes (10 entries in `git status`: the worknote and `packages.lock.json` files), but none touch that file.
- So the patch content matches HEAD. Why the lab's check failed at 17:23 is not established by read-only checks: the dirty build snapshot (`builds/f5f8cdc5-dirty-20261101171643`) holds only the gateway, nodes and probe projects, not the token file. Part A's M1 applied and ran (`survived`) at 11:04 CST. The difference is inside the lab's check, not in the patch text. Not fixed, not retried.

### Remaining gates (fallback session, same daemon pid 29500, still alive; six labelled containers up)

Gate 3, `twin_run enroll-latency-invariance` (no lease held): overall `fail`, `held` null, twin row `skipped` "twin precondition: invariants/I1 Fail"; base run 1e5e60, signature `inv:I1|nodes|unhandled`, known_issues checked (7 issues). Raw failure, not diagnosed. Full result in scratchpad `w5r7-B-09-twin.json`.

Gate 4, `chaos_run` plan `nodes-enroll`, duration_s 600 (no lease), job 699b75c2636d, seed 13540397282229585139: succeeded; faults_started 23, faults_refused 17, faults_healed 8, heal_failed 0, workload_loops 251, workload_errors 0, checkpoints 16, violations 0, check_errors 0. PASS (17 refused faults not examined). Result in scratchpad `w5r7-B-10-chaos.json`.

Gate 5, `soak_run` `catalog/nodes.toml`, duration_s 300 (no lease), job 076d0606ce80, seed 1965194593165994648: succeeded; loops 1, workload_errors 0, samples 1, sample_errors 0, i7 `skipped`. PASS-WEAK (one sample, I7 not judged; same as continuation 4).

Gate 6, `coverage_report` (since daemon start): ran, no error. route_status total 72 hit 0; error_code total 29 hit 0; transition total 22 hit 0; invariant_check hit 1; fault_target hit 0 (total not reported); outside_universe empty. The index does not show the route/error/transition hits from the runs above; not investigated. Result in scratchpad `w5r7-B-12-coverage.json`.

Gate 7, `fuzz_run` target enroll, cases 50 (no lease), job 591a076703f0, seed 14372532009034476853: state `failed` after 60 s (17:49:44 to 17:50:45), result `{"error":"[Lab.InvalidArgument]"}` with no message; run.json overall `fail`. The tool's own argument checks (cases 1 to 5000, replay) would not reject these arguments, so the error came from inside the job; not diagnosed, not retried. (Continuation 3 on an older daemon build ran the same fuzz successfully, sent 50.)

### Teardown (continuation 7)
Lease re-acquired for teardown only and released. `stack_down wipe` (state down, network absent, containers []); no labelled containers and no `meridian-lab-net` remained, so `docker rm -f` was not needed. Daemon pid 29500 stopped (taskkill on that pid only); port 8789 free. No prune, dev stack untouched, no mutant/patch/lab code changed.

Last check result line: none (live gates only; Part A last result: 12 tests, 10 passed, 2 failed).

## Continuation 8 (boundary runner, D-ML109 / D-ML115, campaign tip 62ce8be2)

Setup: campaign tip 62ce8be2 (includes the four wave5-fix-R7 packets). Worktree `.claude/worktrees/meridian-lab`, non-elevated. Full report: `.claude/reports/meridian-lab/wave5-docker-run-8.md`.

Build: `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded, 0 errors, 53 warnings (IL2026 in Dhadgar.Agent.Windows, outside the lab). `dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true`: 0 warnings, 0 errors.

### Part A (Docker, one at a time)

| # | Test or filter | Result | Note (vs continuation 7) |
| --- | --- | --- | --- |
| A1 | HunterDockerTests | Pass | same |
| A2 | TwinLatencyDockerTests (L53) | Pass | same |
| A3 | MutationDockerTests (M1) | Pass | flipped green (was `survived`) |
| A4 | SelfCheckRecoveryTests | Pass | same |
| A5 | SelfCheckE2ETests (LM1) | Pass | same |
| A6 | LabSelfTestE2ETests (S-L6) | Pass | same |
| A7 | Seam=S-L12 (TenantTwinSeamTests) | Fail, expected red: `twin_run failed: {"error":{"code":"Lab.NotFound","message":"no such spec"}}` | same |
| A8 | BlastRadiusDockerTests | Pass | same |
| A9 | FaultSmokeE2ETests | Pass | same |
| A10 | ToxiproxyTimeoutToxicDockerTests | Pass | same |
| A11 | Seam=S-L7 (ToxiproxySeamTests) | Fail: `[Lab.Docker] could not create meridian_lab_nodes` at line 97; not retried | flipped red (was green) |
| A12 | Seam=S-L11 (EnrollHistorySeamTests) | Pass | same |

Part A totals: 12 tests, 10 passed, 2 failed (A7 expected red, A11 new red).

### Part B setup (continuation 8, fresh daemon)
- Fresh daemon, daemon pid 38092 (stopped at teardown), port 8789. Lab token saved to scratchpad `r8-B-token.txt` (never printed). Home `scratchpad\r8-home-B`.
- `lab_build` build_id `62ce8be2-dirty-20261101200820`; `stack_up` wipe up; `svc_start nodes` and `svc_start gateway` up.

### Gates (part B, fresh daemon, build 62ce8be2-dirty-20261101200820)

| Gate | Job | Result | Vs continuation 7 |
| --- | --- | --- | --- |
| Hunt enrollment seed 4242, budget 300 s | 770e5546d09e | failed `[Lab.Unavailable]` after 300 s; no repro, no replay; both nodes restarts returned ok | still red; restart now ok |
| M1 (M1-token-no-concurrency) | 0894368b089e | `detected` (run 5ce06e0c) | flipped green (was `stale`) |
| M4 (M4-no-concurrency-catch) | 0894368b089e | `detected` (run bf7df5c6) | flipped green (was `survived`) |
| M5 (M5-token-logged) | 0894368b089e | `detected_other`, signature `inv:I1\|nodes\|unhandled` | same |
| Twin `enroll-latency-invariance` | base 16f315 | FAIL, held null, twin row precondition I1 Fail | same |
| Chaos `nodes-enroll`, duration_s 600, seed 9959511619756607412 | 11c5fc1ade4a | PASS: faults_started 17, refused 17, healed 5, heal_failed 0, workload_loops 251, workload_errors 0, violations 0 | counts changed (was 23/17/8) |
| Soak `catalog/nodes.toml`, duration_s 300 | a93bb35b5979 | PASS-WEAK: loops 1, samples 1, i7 skipped | same |
| coverage_report (since daemon start) | n/a | ran: route_status 0/72, error_code 0/29, transition 0/22, invariant_check hit 1 | same |
| Fuzz `enroll`, cases 50, seed 14372532009034476853 | db67a3da59c3 | succeeded: sent 50, known 8, 28 failure entries, all `status 500` on `POST /api/v1/agents/enroll` | flipped green (was `Lab.InvalidArgument`) |

### Raw reds (recorded, not diagnosed)
1. Hunt enrollment, job 770e5546d09e: `[Lab.Unavailable]` after the full 300 s budget. Look at: job record 770e5546d09e and the audit file under `r8-home-B`.
2. A11 Seam S-L7: Docker could not create `meridian_lab_nodes`. Look at: the A11 log in scratchpad `r8-*.log`, line 97.
3. M5, job 0894368b089e: `detected_other` (expected a specific detector). Look at: job 0894368b089e (M5 detector run) under `r8-home-B`.
4. Twin `enroll-latency-invariance`, base 16f315: I1 precondition fails, held null. Look at: base run 16f315 under `r8-home-B`.
5. Fuzz enroll, job db67a3da59c3: 28 of the enroll cases returned HTTP 500 from `POST /api/v1/agents/enroll`. Look at: the failure list in job db67a3da59c3.

### Teardown (continuation 8)
- `stack_down` wipe: state down, network absent, containers none.
- Daemon pid 38092 stopped (taskkill on that pid only); port 8789 free.
- No `meridian-lab=1` containers left. The empty labelled network `meridian-lab-net`, left by the failed A11 test, was removed label-only.
- No prune, dev stack untouched, no lab, mutant or product code changed.

Last check result line: Dhadgar.Lab.Tests Release build succeeded, 0 errors (53 IL2026 warnings outside the lab); Part A 12 tests, 10 passed, 2 failed.
Next step: none recorded by this run (raw results only).
