# wave5-docker-run 11 (confirmation run, D-ML140, section 6 of wave5-boundary4-ruling.md)

Status: COMPLETE (continuation 1 of 3 finished the run; ruling section 6 has no further segments). Started on campaign tip 7351c6f7 (branch campaign/meridian-lab), worktree .claude/worktrees/meridian-lab, non-elevated, win32.
Scratchpad prefix: r11-*. Token never printed. Lease ids never printed.

## Step 0: preconditions (06:3x, before any test)
- Port 8789: no listener. dotnet/meridian-lab processes: none before the test run.
- Mutex: no lab daemon running, so the single-instance mutex is free.
- Docker Desktop: server 29.1.3, running.
- `docker ps -a --filter label=meridian-lab=1`: 0 containers.
- Observation (recorded, not acted on): one empty network `meridian-lab-net` with label `meridian-lab=1` and no attached containers exists. Not a listed precondition. Left alone; teardown checks the lab network at the end.
- Dev-stack containers (postgres-630acec0, messaging-630acec0, cache-630acec0, postgres-db24fb3e, messaging-db24fb3e, ...) are present and untouched. They are not lab-labelled.

## Step 1: builds and the non-Docker suite
| Check | Result |
| --- | --- |
| `dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true` | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true` | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ... --no-build --filter "RequiresDocker!=true"` | Passed!  Failed 0, Passed 1491, Skipped 5, Total 1496 (Duration 10 m 5 s). Log: scratchpad `r11-nondocker.log` |
| `--filter "FullyQualifiedName~Hunter_SameSeed_SameFirstOpList"` (run alone, after an assertion failure in the suite run) | Failed Hunter_SameSeed_SameFirstOpList [1 m]. Assertion failure, not a timeout: Expected `["Mint(60)","Enroll(0)","EnrollAgain(0)"]`, Actual `["Mint(60)","Enroll(0)","Enroll(0)"]` (ModelHunterTests.cs line 68). Recorded raw. Not rerun, because the procedure reruns only on a timeout. |
| I1FromThirdOp check (test `Hunter_ShrinkNotReproduced_OriginalReproduces_ReportsDivergedAndShrinkUnstable`, which sets `I1FromThirdOp = true`) | Passed 1, Failed 0, Duration 30 s. Ran and passed. |

Note on step 1: the full non-Docker run reported 0 failures, but the isolated run of Hunter_SameSeed_SameFirstOpList failed. Both are recorded; the run-to-run difference is not diagnosed.

## Step 2: Part A (serial, once each unless noted; `dotnet test --no-build`)
| ID | Filter | Result | Time | Run 10 |
| --- | --- | --- | --- | --- |
| A1 | `FullyQualifiedName~HunterDockerTests` | Passed!  Failed 0, Passed 1, Skipped 0, Total 1 | 2 m 6 s | Passed |
| A4 | `FullyQualifiedName~SelfCheckRecoveryTests` | Passed 1, Failed 0 | 37 s | (not in run 10) |
| A5 | `FullyQualifiedName~SelfCheckE2ETests` | Passed 1, Failed 0 | 24 s | (not in run 10) |
| A6 | `FullyQualifiedName~LabSelfTestE2ETests` | Passed 1, Failed 0 | 1 m 14 s | (not in run 10) |
| A10 | `FullyQualifiedName~ToxiproxyTimeoutToxicDockerTests` | Passed 1, Failed 0 | 40 s | (not in run 10) |
| A11 (run 1 of 2) | `Seam=S-L7` | Passed 1, Failed 0 | 46 s | Passed 1 |
| A11 (run 2 of 2) | `Seam=S-L7` | Passed 1, Failed 0 | 49 s | n/a |

Logs: scratchpad `r11-A1.log` to `r11-A11b.log`. No A4 or A11 red, so KG-W5-7 and KG-W5-15 are not triggered by this run.

## Part B segments (one fresh daemon per segment; port 8789; token in scratchpad `r11-B-token.txt`, never printed)

### Segment 1: CLI probe and R9-1 flake-check probe (daemon pid 65520, home `r11-home-1`)
Setup: build `7351c6f7-dirty-20261102130041`; `stack_up` wipe `network present`; nodes and gateway up; lease released.

CLI probe (token from file, not echoed):
1. `meridian-lab status --json`: exit 0. Body pid 65520 = daemon pid. Lease held false. Stack `up`. nodes and gateway running.
2. `meridian-lab status --port 9000`: exit 1, output `[Lab.Refused] the target is not a lab endpoint`.
3. `meridian-lab uat run --specs <worktree>\docs\guides\uat-specs\lab-selftest.toml --plan-only --json`: exit 0. Output (one line): `{"run_id":"61881b","seed":12594627839921711943,"plan_only":true,"overall":"incomplete","rows":[],"events":0,"truncated":false,"bundle":null,"ingest":null}`. No bundle path.

R9-1 probe:
4. `uat_run` (no lease, spec `lab-selftest.toml`, seed 12499590173714643239, no rows filter, plain run): run `e68d57`, overall `pass`, bundle `20261102-7351c6f7-dirty-20261102130041-e68d57`. Row grades: stack-up PASS, nodes-up PASS, gateway-up PASS, token-mint PASS, enroll-direct PASS, enroll-gateway PASS, enroll-ingress PASS, token-single-use PASS, nodes-log PASS. Seed 12499590173714643239.
5. `results_flake_check` run `e68d57`, rows `["enroll-gateway"]`, reruns 1, no lease: job `ec7b4116d450` **succeeded**. Result: seed 12499590173714643239, rows `["lab-selftest/enroll-gateway"]`, runs `[5be0f8 overall pass]`, flakes `[]`, state `stable`.
6. `results_flake_check` run `e68d57`, rows `["lab-selftest/enroll-gateway"]`, reruns 1, no lease: job `53ae4e1dbcf4` **succeeded**. Result: seed 12499590173714643239, rows `["lab-selftest/enroll-gateway"]`, runs `[1e0e5c overall pass]`, flakes `[]`, state `stable`.
   - Neither call returned `[Lab.InvalidArgument]` or `[Lab.NotFound]`. Both returned a job id.
7. `results_query` view `runs`: e68d57 seed 12499590173714643239 (overall pass, fidelity exact); 5be0f8 seed 12499590173714643239 (pass, exact); 1e0e5c seed 12499590173714643239 (pass, exact). No seed of 0 or negative.
   - Rerun bundle `rows\lab-selftest\` folders: 5be0f8 holds exactly `stack-up.json`, `nodes-up.json`, `gateway-up.json`, `enroll-gateway.json`, all PASS. 1e0e5c holds exactly the same four, all PASS. The closure file set matches R9-1.

Segment 1 verdict against the pass list: every item met (steps 1-3 as stated; both jobs succeeded; seed, rows, stable and pass; closure set of four PASS; query seeds correct). KG-W5-16 and KG-W5-18 are not triggered by this segment. Partial clause not used (enroll-gateway PASS).

Teardown of segment 1: daemon pid 65520 stopped (Stop-Process); port 8789 has no listener.

### Segment 2: hunt replay consistency probe (daemon pid 51628, home `r11-home-2`, started 07:04:55)
Replay 1 (old home, same daemon, recorded by the previous coder; facts from `r11-home-2\jobs\` and `r11-home-2\audit\audit-20261102.jsonl`):
1. Hunt `d15428729f64`, model `enrollment`, seed `4242`: job state `succeeded`; payload state `diverged`, `first_diverging_op` 6, detail `op 6 EnrollAgain(0): invariant I1 violated outside a PgDown window`.
2. Replay `1a0f0bf1ffc7` (`repro.toml` from `d15428729f64\repro\`): job state `succeeded`; payload state `reproduced`, `first_diverging_op` 6, detail `op 6 EnrollAgain(0): invariant I1 violated outside a PgDown window`. Lease acquired 13:11:19 UTC, released 13:11:28 UTC.
3. Audit of replay 1 (audit lines 1324-1376, from the job's first `fault_reset` to `stack_reset_data`): `fault_reset` ok (1326); `svc_restart` ok, 4264 ms (1328); `nodes_token_mint` ok (1330); `nodes_enroll_probe` ok, 446 ms (1332); `stack_reset_data` ok (1334). Then, after the segment's replay body: `nodes_token_mint` ok (1338), `fault_proxy_disable` ok (1352), `nodes_enroll_probe` ok (1366), `fault_reset` ok (1374), lease released (1376).
   - Not visible in the audit lines: the `svc_restart` target (nodes), the `hunt-warmup` label on the mint, and the HTTP 201 of the probe. The audit line carries only `outcome: ok` for these, so those three points are UNVERIFIED from the audit alone.

Stop and port check (ruling step 3): daemon pid 51628 stopped; port 8789 free.

Replay 2 (new daemon pid 19640, home `r11-home-3`, ruling segment 2 step 4 setup: lease, `lab_build` build `7351c6f7-dirty-20261102131436`, `stack_up` wipe `network present`, `svc_start nodes` and `gateway` up, lease released). Note: the previous coder's "segment 3" is this same segment 2 (steps 4-6); section 6 lists no segment 3.
4. The repro folder was copied from `r11-home-2\jobs\d15428729f64\repro\` to `r11-home-3\jobs\d15428729f64\repro\` (contains `repro.toml`). Then `hunt_model_run` with `replay` `d15428729f64/repro/repro.toml`, no lease held: job `286f5fb444c8` (seed 4242).
5. Job `286f5fb444c8`: job state `succeeded`; payload state `reproduced`, `first_diverging_op` 6, detail `op 6 EnrollAgain(0): invariant I1 violated outside a PgDown window`. Same as replay 1.
6. Audit of replay 2 (`r11-home-3\audit\audit-20261102.jsonl`, lines 16-70, from the first `fault_reset` to `stack_reset_data`): `fault_reset` ok (18); `svc_restart` ok, 6450 ms (20-23); `nodes_token_mint` ok (24-25); `nodes_enroll_probe` ok, 573 ms (26-27); `stack_reset_data` ok (28-29). Then `nodes_token_mint` ok (32-33), `fault_proxy_disable` ok (46-47), `nodes_enroll_probe` ok (60-61), `fault_reset` ok (68-69), lease released (70). The audit order of the first five matches the pass list.
   - As with replay 1, the audit lines do not show the `svc_restart` target, the `hunt-warmup` label, or the probe's HTTP 201. Those three are UNVERIFIED from the audit alone.

Segment 2 verdict: hunt `diverged` at op 6; replay 1 (`r11-home-2`) and replay 2 (`r11-home-3`) both `reproduced` at op 6 with the same detail; each replay audit shows the required sequence. Pass clause met. Early evidence for KG-W5-13 only (the gate stays with the campaign review, D-ML127). No red in segment 2, so KG-W5-13 is not closed by this run and stays parked.

## Teardown (run end)
1. Lease acquired (owner `wave5-live-11-teardown`), `stack_down` wipe=true on daemon pid 19640: `state` `down`, `network` `absent`, `containers` `[]`; lease released (`released: true`).
2. Daemon pid 19640 stopped (Stop-Process). Daemon pid 51628 was stopped earlier (segment 2 step 3).
3. Checks: `docker ps -a --filter label=meridian-lab=1` shows 0 containers. `docker network ls --filter label=meridian-lab=1` shows 0 networks, so the empty `meridian-lab-net` from step 0 is gone (`stack_down` reported `network` `absent`; nothing was pruned by hand). `docker network ls` shows no network with `lab` in the name. Port 8789 has no listener. Dev-stack containers were not touched.

## Summary
- Non-Docker suite: passed 1491, failed 0, skipped 5 (Step 1). The isolated `Hunter_SameSeed_SameFirstOpList` run failed on the assertion (not a timeout), so the rerun rule did not apply. Recorded raw.
- Part A: A1, A4, A5, A6, A10 and A11 twice all passed. No KG-W5-7 or KG-W5-15 red.
- Segment 1 (CLI probe and R9-1 flake-check probe): all pass-list items met. KG-W5-16 and KG-W5-18 not triggered.
- Segment 2 (hunt replay consistency): hunt `diverged` at op 6; both replays `reproduced` at op 6, each with the required audit order. No red. KG-W5-13 stays parked, with early evidence only.
- Teardown clean.
- Open for the architect: the `svc_restart` target, the `hunt-warmup` label and the probe HTTP 201 are not visible in the audit lines (noted in segment 2). Deviation: the previous coder's "segment 3" is segment 2 steps 4-6, because section 6 has only two segments.

Status: COMPLETE. The Part B record is complete, and the teardown is done.

