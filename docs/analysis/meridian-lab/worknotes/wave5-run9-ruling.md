# Wave 5 boundary: ruling after run 9

Architect, 2026-11-01. Campaign tip `2bb86769` (R8 merged at `6899985d`), pushed to `origin/campaign/meridian-lab`; 415 commits ahead of `origin/main` and 13 behind.
Inputs: the run 9 record (`.claude/reports/meridian-lab/wave5-docker-run-9.md`, gitignored; facts copied below), the [run 8 diagnosis](wave5-run8-diagnosis.md), README D-ML108, D-ML109, D-ML115, D-ML118 and D-ML125, and the run 9 lab home (`scratchpad\r9-home-1`: audit, `logs/nodes.jsonl`, `jobs/6313df2e0ae3/repro/repro.toml`).

## 1. Ruling

The wave 5 and 5E boundary is **not closed yet**. Run 9 turned every live red into either a recorded result or green, but two things are still owed:

1. The boundary wave review `ship`. Pass 3 `ship` (`wave5-review-3.md`) predates every fix since `08571257`: F, G, H, FGH-fix, R7 and R8. The fixFGH loop and the R7 and R8 packet loops reviewed packets, not the wave. No `security-advisor` pass ran on R8 (D-ML129).
2. The hunt gate. It is unmet as worded, so it is parked as KG-W5-13 with follow-up L27b (D-ML127, owner).

D-ML125's hard stop holds. No further Docker fix wave runs at this boundary. Review fix notes are not a fix wave. A confirmation run on the final tip (D-ML130) only checks, and a red there is parked, not fixed.

## 2. Gate by gate

| Gate item (D-ML108) | Evidence | Status |
| --- | --- | --- |
| Build and non-Docker suite green | Run 9 builds at `6899985d` are green. Run 9 did not run the non-Docker suite on the merged R8 tip; each R8 packet ran it on its own branch | Build met; suite owed in the confirmation run |
| Hunter Docker test (A1) | Run 8 pass at `62ce8be2`; not rerun. R8-1 changed the hunter | Carried; rerun in the confirmation run |
| SelfCheckE2E LM1 (A5), recovery (A4) | Run 8 pass; not rerun. R8-3 changed every `stack_up` | Carried; rerun |
| S-L6 (A6) | Run 8 pass; not rerun | Carried; rerun |
| Mutation test state assertion (A3) | Run 9 pass | Met |
| S-L7 (A11) | Run 9 pass, once. The run 8 diagnosis asked for three runs; A10 was not run | Met once; two more runs plus A10 in the confirmation run |
| Hunt plus replay | Job `6313df2e0ae3` `succeeded`, state `not_reproduced`, ops `[Enroll(1), Mint(60)]`, `first_diverging_op` -1; the replay tool was not run | **Unmet** (section 3) |
| Gate MD: M1, M4, M5 | M1 `detected` alone (hist E1/E6 `enroll-race`); M4 `detected` alone (`inv:I1\|nodes\|unhandled`); M5 `detected` (`inv:I4\|nodes\|vault-value:tok-1`; `enroll-gateway` ran and failed on I4) | Met |
| Coverage bar | Run 8: `invariant_check` hit 1, the other four kinds `not_collected`. R8 changed no coverage code | Met (carried); KG-W5-3 stays |
| S-L12 red (A7) | Run 8: red as expected. R8 did not touch twins | Met (carried) |
| Gate LS at 5E | Run 8: LM1 `caught`, recovery passing | Met (carried); rerun A4 and A5 |
| Twin (informational) | Pass; held true, base `275d01`, variant `6f0688` | Met |
| Chaos (informational) | `succeeded`; 152 loops; 304 mint and probe audit lines; 0 violations; faults started 22, refused 12, healed 2 | Met: enroll is now exercised (KG-W5-14 not live) |
| Soak (not a gate, D-ML112) | Run 8 PASS-WEAK; not rerun. R8 changed no soak code | Open as KG-W5-2 until L29b; no rerun needed |
| Fuzz (informational) | 28 of 50 got HTTP 500, as in run 8 | KG-W5-1; D-ML107 owner |
| Wave review `ship` | Not run since pass 3 | **Owed** (D-ML129) |

Soak and coverage need no rerun. Neither code path changed after run 8, and neither gap closes until its follow-up packet lands: L29b for soak, and L52b (after L25g) for coverage.

## 3. The hunt: what run 9 found (F114, F116)

The brief's "nothing found in budget" is wrong. The hunt found a divergence; then it shrank it to a case that cannot fail. The audit and `logs/nodes.jsonl` show the chain:

1. Case *k* (21:43:58.47 UTC) ran Mint, Mint, then a `fault_proxy_disable` at 21:43:59.58. Its `finally` sent `fault_reset` at 21:44:02.40.
2. Case *k+1* (21:44:02.44) ran reset, Mint, then PgUp (no fault, so no call), then Enroll(0). `nodes_enroll_probe` ran at 21:44:03.19. Nodes logged EF "transient failure" (NpgsqlException "Exception while reading from stream", EndOfStreamException) and then "Unhandled exception (fallback handler)", and answered 500. The pooled connection was killed by case *k*'s cut. Nodes has no `EnableRetryOnFailure` (`src/Dhadgar.Nodes/Program.cs:46`), and the hunt resets data but not Nodes' connection pool. This is the product half of F114.
3. The model expected a success, so `Compare` set `mismatch`, and `mismatch ??= await CheckStateAsync(...)` (`Hunt/ModelHunter.cs:336,389`) skipped the check. Case *k+1* returned without an `invariants_check`, so the I1 at 21:44:03.289 stayed in the checkpoint window. `invariants_check` advances the checkpoint on every call (`Tools/Invariants/InvariantsCheckTool.cs:44,57`).
4. CsCheck's next case, *k+2* at 21:44:03.30, was `[Enroll(1), Mint(60)]`. Enroll(1) has no token, so it is skipped (`ModelHunter.cs:270`). The Mint check's window held the carried I1, so the case failed with "I1 outside a PgDown window". Nothing smaller failed in the remaining 121 cases, so CsCheck kept *k+2*.
5. The replay ran on a clean window and did not diverge: `not_reproduced`. The sample never recorded the divergence detail it saw (`ModelHunter.cs:206`), so the real case, *k+1* after a cut, appears nowhere in the result. This is the lab half of F114.

A second defect blocks the gate even with F114 fixed (F116). The repro writer prints no-argument ops as `PgDown()`, `PgUp()` and `RestartNodes()`: `EnrollmentModel.cs:54` `Kind + "()"`, written at `ModelHunter.cs:574`. The replay parser's `OpItem` regex `(\((?<arg>\d+)\))?` needs a digit inside the brackets (`Tools/Hunt/HuntModelTool.cs:196`). So `hunt_model_run replay=` refuses any repro with a cut or a restart (`[Lab.InvalidArgument] repro has an unknown op`), and F114's real case has a cut. No test parses a written repro with `ReproParser`. `Hunter_PlantedReuseBug_ShrinksAndReplays` only checks that the file exists.

**Ruling on the wording.** `not_reproduced` meets neither arm of the gate: "replays to the same failure" or "finds none in its budget and says so". The gate stays unmet. Under the hard stop, the hunt is parked as KG-W5-13, reworded to F114 and F116, and L27b is the defined follow-up (section 6.1). L27b runs before the campaign review, which reruns the live hunt (D-ML74). Its acceptance is either a live hunt that ends `diverged` and whose `repro.toml` replays `reproduced` through `hunt_model_run replay=`, or a hunt that ends `passed`. A planted-bug hunt runs in-process (ModelHunterTests), not live: product mutants already cover planted product bugs, and a planted lab bug in a live hunt would need new infrastructure for no gain.

The product half of F114 (a 500 on the first request after a database blip) goes to the owner as D-ML128. Until it is ruled, a live hunt may well end `diverged` on it, and that meets the gate: the gate asks for a reproducible failure, not a pass.

## 4. F115: `results_flake_check` now refuses dependent rows

R8-2 put `RowFilterCheck.Check` in `uat_run` (`Tools/Uat/UatRunTool.cs:182-189`). `FlakeCheckJob` reruns the source run's FAIL rows (or the named rows) as `rows = plan.Rows` (`Results/FlakeCheckJob.cs:105,118,155`). Most catalog rows depend on `stack-up` or `gateway-up`, so a flake check of any such failed row now ends `[Lab.RerunFailed]`. Before R8-2 it was a hollow SKIP. `FlakeCheckJobTests` uses a fake `invoke`, so no test sees the refusal, and the R8-2 caller list named only `SoakJobTests`. Fix: R9-1 (section 6.2). The live gap is KG-W5-16.

## 5. Review coverage since pass 3 (`08571257`)

| Change | Packet review | Security | Wave review |
| --- | --- | --- | --- |
| fix-F, fix-G, fix-H, FGH-fix (`f5f8cdc5`) | fixFGH review-3 `ship` | security-2 `ship` | none |
| R7-1 to R7-4 (`62ce8be2`) | all `ship` (R7-1 and R7-4 at pass 2; R7-2 and R7-3 at pass 1) | not recorded | none |
| R8-1 hunt-pgdown | review-1 `ship` | none | none |
| R8-2 filter-deps | review-1 `fix`, fix-1, review-2 `ship` | none | none |
| R8-3 pg-ready | review-1 `fix`, fix-1, review-2 `ship` | none | none |

The reports are gitignored and live only in the packet worktrees: for R8, `.claude/worktrees/ml-wf-r8-<name>/.claude/reports/meridian-lab/ml-wf-r8-<name>-*.md`. No `wf-r7` or `wf-r8` worknote is committed. Before L47 removes the worktrees, their facts must be copied into `worknotes/`; the step is in section 7.

## 6. Packets written now

Shared rules for both packets (packet-coder does not read CLAUDE.md):

- R-RESULT: tool and IO failures return `Result.Failure` with a `[Lab.*]` code. Never throw for them. Never `Success(null)`; use `Result.Success()`.
- R-PATH: every spec or bundle path goes through `IPathValidator` (`UatSpecPaths.ResolveDir` / `ResolveSpec`, `BundleFolders`).
- R-USING: `using` on every `IDisposable`.
- R-CPM: no package versions in `.csproj` files.
- Nullable is on, and CI treats warnings as errors.
- Tests use xUnit `[Fact]`, follow the `Method_Case_Result` names of the target test class, and use `FakeTimeProvider` where the class already does.

Checks run in this order. RID-less restore only: never `publish -r`, which dirties lock files.

```powershell
dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Category!=Docker"
```

### 6.1 L27b: hunt case isolation and a replayable repro (F114, F116)

- **Branch / worktree / commit:** `ml/l27b-hunt-isolation` / `ml-l27b` / `fix(lab): isolate hunt cases and make every repro replayable`
- **Depends on:** the boundary close (D-ML126). **Security review:** yes (lab tool that drives faults and restarts).
- **Size:** small, about 25k tokens.
- **Files:** `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`, `src/Tools/Dhadgar.Lab/Tools/Hunt/HuntModelTool.cs` (`ReproParser`), and `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`. Add a test class `tests/Dhadgar.Lab.Tests/Hunt/ReproRoundTripTests.cs`.

Steps:

1. **Case-start checkpoint.** In `RunCaseAsync`, after the second `fault_reset` and before the op loop, call `invariants_check` with `{}`. On failure, return `Infrastructure("[Lab.Unavailable] invariants_check failed at case start")`. Count the violations it returns as `carried`. Add `int Carried = 0` as the last positional member of `CaseResult`; `Passed(int known)` keeps its signature. Carried violations never make a case diverge.
2. **Pool isolation.** Add a field `private bool _cutSinceRestart;`. At the very start of `RunCaseAsync`, before `stack_reset_data`: if `_cutSinceRestart` is set, call `svc_restart` with `{ "service": Service }`. On failure, return `Infrastructure(...)`. Then clear `_cutSinceRestart`. In the `finally`, after `HealAsync()`, set `_cutSinceRestart = true` when `ranPgDown` is set. This does not change D-ML119: no case is in a cut at its start. Inside one case, PgDown, PgUp and Enroll still reach the stale pool, so the real product behaviour stays findable and deterministic.
3. **Keep the original failure.** In `Sample`, add the locals `List<HuntOp>? original` and `string? originalDetail`. On the first `result.Diverged`, set `original = [.. ops]` and `originalDetail = result.Detail` before throwing. Extend `SampleFindings` with `Original`, `OriginalDetail` and `Carried`, the sum of `result.Carried`.
4. **Replay the original when the shrink does not reproduce.** In `RunAsync`, if `!replay.Diverged`, `findings.Original` is set and it is not sequence-equal to `Shrunk`, run `RunCaseAsync(findings.Original)` once. If that diverges, report it: `state = "diverged"`, `ops = original`, `first_diverging_op` and `detail` from that replay, and add `["shrink_unstable"] = true`. Write the repro with the original ops. Always add `["original_ops"]` (a JSON array of op strings) and `["original_detail"]` to the failed payload. Add `["carried"]` to both the passed and the failed payloads.
5. **Repro round trip (F116).** In `ReproParser`, change `OpItem` to `^(?<kind>Mint|Enroll|EnrollAgain|PgDown|PgUp|RestartNodes)(\((?<arg>\d*)\))?$`. An empty `arg` parses as 0. Do not change the writer, so existing repros stay readable.

Tests (each fails when its step is reverted):

- `ReproRoundTripTests.EveryOpKind_WrittenThenParsed_IsTheSameOp`. For each `HuntOpKind`, build the `ops = [...]` line the way `ModelHunter.cs:574` does, from `op.ToString()`, inside a minimal repro text with `kind`, `model`, `run_seed`, `cscheck_seed`, `build_id` and `first_diverging_op`. Parse it with `ReproParser.Parse`. Assert success and the same kinds. Reverting step 5 fails PgDown, PgUp and RestartNodes. This is the writer-parser seam test.
- `ModelHunterTests.Hunter_WrittenRepro_ParsesWithReproParser`. After `Hunter_PlantedReuseBug`'s run, also `ReproParser.Parse` the written `repro.toml`, and assert its ops equal the payload's ops.
- In `FakeLab`, add `StalePoolAfterCut`. After a `fault_proxy_disable` that is later healed by `fault_heal` or `fault_reset`, the pool is stale until `svc_restart`. The first enroll on a stale pool while not cut answers 500, clears the stale flag, and queues one I1 that the next `invariants_check` returns and then clears. Make `invariants_check` return queued I1s in addition to today's rule. Add `StaleI1AtStart` (int): violations queued before the first call.
- `Hunter_StalePoolAfterCut_ReportsAReproducibleCase`: `StalePoolAfterCut = true`, seed `Seed`, 200 iterations, budget 30 s. Assert `fail`, state `diverged`, ops containing PgDown before an Enroll, and that `ReplayAsync(ops)` diverges. Reverting step 1 or step 2 brings back the spurious shrink (`not_reproduced`), which is run 9's result.
- `Hunter_CarriedI1_IsCountedNotDiverged`: `StaleI1AtStart = 1` and no other fault. Assert `pass` and `carried` of 1. Reverting step 1 makes it `fail`.
- `Hunter_CaseAfterCut_RestartsNodesFirst`: replay `[PgDown, PgUp]`, then `[Mint]`, on one hunter. Assert that the second case's first call is `svc_restart`, and that the first case sent no `svc_restart`.
- `Hunter_ShrinkNotReproduced_ReportsOriginal`. Use a `FakeLab` flag `FlakyI1OnCheck = n`: one I1 on the n-th `invariants_check` only. Assert that the failed payload has `original_ops` and a non-null `original_detail`. Reverting steps 3 and 4 drops them.
- Existing tests that count calls (`Replay_*`, `Hunter_Cancelled_*`) may need the new case-start `invariants_check` and the post-cut `svc_restart`. Adjust those counts only.

Docs owed: the hunt section of the lab's tool reference, if one lists the `hunt_model_run` payload (`grep -r "not_reproduced" docs`). Add the `carried`, `original_ops`, `original_detail` and `shrink_unstable` keys.

Live acceptance runs at the campaign review (D-ML74), not in this packet. Run `hunt_model_run` with seed 4242 and `budget_s` 300 on a fresh daemon. Expect either `diverged` with a `repro.toml` that `hunt_model_run replay=<job>\repro\repro.toml` reports `reproduced`, or `passed`.

### 6.2 R9-1: flake check closes its rows over `depends_on` (F115)

- **Branch / worktree / commit:** `ml/wf-r9-flake-deps` / `ml-wf-r9-flake-deps` / `fix(lab): flake check reruns the rows its failed rows depend on`
- **Depends on:** nothing; dispatch it after the boundary review returns. If the R1 review's fix notes touch the same files, apply them in this packet. **Security review:** yes (lab tool).
- **Size:** small, about 15k tokens.
- **Files:** `src/Tools/Dhadgar.Lab/Uat/RowFilterCheck.cs`, `src/Tools/Dhadgar.Lab/Results/FlakeCheckJob.cs`, `src/Tools/Dhadgar.Lab/Tools/Jobs/JobTools.cs` (`FlakeCheckTool`), `tests/Dhadgar.Lab.Tests/Uat/RowFilterCheckTests.cs` and `tests/Dhadgar.Lab.Tests/Jobs/FlakeCheckJobTests.cs`.

Steps:

1. `RowFilterCheck.Close(UatSpec spec, IReadOnlyCollection<string> filter) : IReadOnlyList<string>` returns the filter plus the transitive `depends_on` closure, in spec file order. Loop until no row is added. Unknown ids pass through, so that `Check` still reports `[Lab.NotFound]`.
2. `FlakeCheckJob.WithSpec(FlakeCheckPlan plan, UatSpec spec) : Result<FlakeCheckPlan>` returns `plan with { Rows = RowFilterCheck.Close(spec, plan.Rows) }`. It leaves `Keys`, the rows judged for flakes, unchanged. Return `Result.Failure` when `RowFilterCheck.Check(spec, closedRows)` fails.
3. In `FlakeCheckTool.ExecuteAsync`, after `PlanAsync`, load the plan's spec the way `UatRunTool` does: `UatSpecPaths.ResolveDir(null, ...)`, `ResolveSpec`, the 1 MiB cap, `File.ReadAllTextAsync` in a try/catch on `IOException` or `UnauthorizedAccessException` that returns `[Lab.Unavailable] the spec could not be read`, and `SpecLoader.LoadText`. A spec that does not load is `[Lab.InvalidArgument] the source run's spec does not load`. Then call `WithSpec`, and start the job with the result.
4. Rerun dependency rows are graded, but only `Keys` decide the flake verdict (unchanged code at `FlakeCheckJob.cs:188-204`).

Tests:

- `RowFilterCheckTests.Close_AddsTransitiveDependencies_InFileOrder`: rows a, b (depends on a), c (depends on b). `Close([c])` is `[a, b, c]`, and `Check` passes on it.
- `FlakeCheckJobTests.WithSpec_FailedRowWithDependency_PassesUatRunFilterCheck`. Use a source bundle whose FAIL row `b` depends on `a`, and a spec that loads with `SpecLoader.LoadText`. Then `PlanAsync`, then `WithSpec`. Assert that `RowFilterCheck.Check(spec, plan.Rows)` succeeds and that `Keys` holds only `b`. Reverting step 2 makes `Check` fail. This test runs the same check `uat_run` runs, so it is the seam test for flake check and `uat_run`.
- `FlakeCheckJobTests.WithSpec_UnknownRow_IsNotFound`.

Docs owed: none. The tool description stays true.

## 7. Remaining steps to the campaign PR, in order

| # | Step | Who | Owner decision? |
| --- | --- | --- | --- |
| 1 | Ask the owner D-ML127 (park the hunt gate as KG-W5-13), D-ML128 (Nodes transient retry), D-ML131 (ship cadence); ratify D-ML118 and D-ML125 as executed under option A | orchestrator | yes |
| 2 | Boundary review pass 4 (D-ML129): slices R1, R2 and R3, each with a `packet-reviewer` and a `security-advisor` (six in parallel). Range `08571257..2bb86769`; inputs are this note and the run 9 record | packet-reviewer, security-advisor | no |
| 3 | Fix notes and R9-1 to packet-coder worktrees, review loop per packet, local merge into `campaign/meridian-lab` | packet-coder, packet-reviewer, ops | no |
| 4 | Ruling check if any slice ends on `decision`, or on `fix` after pass 3 | architect | maybe |
| 5 | Confirmation run on the new tip (D-ML130). A red is parked, not fixed | packet-coder (boundary runner) | no |
| 6 | Close waves 5 and 5E in the ledger: statuses, known gaps, and the R7 and R8 report facts copied into `worknotes/wf-r7.md` and `worknotes/wf-r8.md` | architect | no |
| 7 | Restore the eight `tests/*/packages.lock.json` files dirtied by the boundary runs, and delete `tests/Dhadgar.Lab.Tests/TestResults/` | ops | no |
| 8 | Merge `origin/main` into `campaign/meridian-lab`, merge and never rebase (D-38). Conflicts go to a packet-coder, then the build and the non-Docker suite. Push | ops, packet-coder | no |
| 9 | The W8 deltas ruling note against the merged main, before L40 (README gate wave 8) | architect | no |
| 10 | Follow-ups: L27b, L05b, L25g, then L52b, L28b, L29b, L55b (the architect specs each outline before dispatch) | architect, packet-coder, packet-reviewer | no |
| 11 | Waves 6 to 10. Wave 6 waits on L00 and D-ML37/D-ML38 (the owner has Windows containers off) | all | yes (D-ML37, D-ML38) |
| 12 | Campaign review (D-ML74 reruns every Docker test, including the live hunt for KG-W5-13) | packet-reviewer, security-advisor | no |
| 13 | L47 close-out | architect, documentation-writer | no |
| 14 | Push and open one PR with the `security-review` label; merge on the owner's yes | ops; owner | yes |

Under D-ML131 option B, steps 11 and 13 move after the PR. Steps 12 and 14 then cover waves 1 to 5E, together with step 10's L27b and L05b only.
