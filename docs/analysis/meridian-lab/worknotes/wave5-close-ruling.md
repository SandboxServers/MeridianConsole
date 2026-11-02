# Wave 5 / 5E: boundary-close ruling

Date: 2026-11-02. Author: architect. Inputs:

- Run 11 report: `.claude/reports/meridian-lab/wave5-docker-run-11.md`, on campaign tip `7351c6f7`, recorded in `99bb31e3`.
- [wave5-docker-run](wave5-docker-run.md), Continuation 11.
- [Boundary 4 ruling](wave5-boundary4-ruling.md), sections 6 and 7.
- R11-1's ruling-check reports, `ml-wf-r11-boundary-fixes-review-1.md` and `-security-1.md`.
- Source at `7351c6f7`.

## 1. Ruling

**Waves 5 and 5E close** (D-ML142). The three close conditions of boundary 4 ruling section 7 hold:

| # | Condition | Evidence | Met |
| --- | --- | --- | --- |
| 1 | R11-1's ruling check is `ship` from both reviewers, and R11-1 is merged | review-1 `ship`, security-1 `ship`; merged at `7351c6f7` (`a5ef7fed`) | yes |
| 2 | Section 6 has run; every red is a pass or parked under KG-W5-7, -13, -15, -16, -17, -18 or -20, or is the S-L12 red | Every section 6 clause passed (section 2). The run's two new findings are lab defects. F125 lies inside KG-W5-13's scope, the hunt gate parked by D-ML127. F124 is parked as KG-W5-21 under D-ML126. Neither is a red of a section 6 clause. | yes |
| 3 | This ruling records which KGs the run closed | section 4 | yes |

The run also surfaced two lab defects in the hunter. Both are found by reading the source, not by a failing gate:

- **F124:** the hunt seed fixes only the first case.
- **F125:** a cut-window `EnrollAgain` 500 is flagged as I1.

D-ML126 parks them. Their fixes are follow-up packets **L27d** and **L27e** (section 6). Both are Ready, need no Docker, and must merge before the campaign review, because its live hunt (D-ML74) is the KG-W5-13 gate. Without them that gate either fails on a false positive or passes hollowly.

**L19n is now Ready.** Section 7 of this ruling gives the order of what follows.

## 2. Section 6, clause by clause

| Item | Result | Verdict |
| --- | --- | --- |
| Builds (lab, tests) | 0 warnings, 0 errors | pass |
| Non-Docker suite (`RequiresDocker!=true`) | 1491 passed, 0 failed, 5 skipped (1496) | pass |
| Isolated rerun of two hunter tests | `Hunter_I1FromThirdOp...` passed; `Hunter_SameSeed_SameFirstOpList` **failed** alone (`ModelHunterTests.cs:68`, expected `["Mint(60)","Enroll(0)","EnrollAgain(0)"]`, actual `[...,"Enroll(0)"]`) after it passed in the suite | flaky test, F124 |
| A1 Hunter, A4 recovery, A5 LM1, A6 S-L6, A10 | all pass | pass |
| A11 (S-L7) ×2 | pass, pass | pass; KG-W5-15 not live |
| Segment 1: CLI probe | `status --json` exit 0; `--port 9000` exit 1 `[Lab.Refused]`; plan-only exit 0, no bundle | pass (D-ML139) |
| Segment 1: R9-1 probe | Source run `e68d57`: 9 of 9 rows PASS. Flake jobs `ec7b4116d450` and `53ae4e1dbcf4`: `succeeded`, seed `12499590173714643239`, `stable`, closure of exactly 4 PASS files. | full pass |
| Segment 1: `results_query` seeds | every seed `12499590173714643239` | pass |
| Segment 2: hunt seed 4242 | `d15428729f64`: diverged at op 6, `EnrollAgain(0): invariant I1 violated outside a PgDown window` | see F125 |
| Segment 2: replays | `1a0f0bf1ffc7` (same daemon) and `286f5fb444c8` (new daemon, new home): both `reproduced` at op 6 | pass (F120 fixed by L27c) |
| Segment 2: replay audit order | `fault_reset`, `svc_restart`, `nodes_token_mint`, `nodes_enroll_probe`, `stack_reset_data`. The audit shows the order but not the target, label or 201. | pass by source (section 3, row P1) |
| Teardown | 0 labelled containers, network absent, port 8789 free | pass |

## 3. Classification of every red and finding

| # | Item | Class | Disposition |
| --- | --- | --- | --- |
| F124 | The hunt is not seed-deterministic. `ModelHunter.Sample` (`Hunt/ModelHunter.cs:119,231-268`) hands the run seed to `Check.Sample` as CsCheck's seed string and `budgetSeconds` as `time`. A CsCheck 5.0.0 probe (architect scratch, this session) shows that the seed string fixes only the first iteration; later iterations come from fresh random PCGs. With `time >= 0`, `Check.Sample` runs for the whole budget and ignores `iter` (iter 20, time 1 ran about 22M fake iterations). CsCheck's shrink is random resampling. So the same seed explores different cases and shrinks to different lists. That breaks R-SEED (`work-packets.md:53`) and makes `Hunter_SameSeed_SameFirstOpList` flaky. It also makes each fake-lab hunter test run for its full 30 s budget, which is the likely cause of R10-1's "timeout at 1 m" on the same test. It explains why seed 4242 gave different hunts in runs 9, 10 and 11, why the shrink got only from 14 ops to 13 in 300 s, and why the passed payload's `iterations` is the requested value, not the number of cases run. | lab defect (new) | New KG-W5-21, parked (D-ML126). The fix is L27e (D-ML143). It is also a CI risk on the campaign PR, so L27e must land before that PR. |
| F125 | The I1 excuse covers only `Enroll` in a cut. `CheckStateAsync` (`Hunt/ModelHunter.cs:539`) allows I1 when `model.PgDown && op.Kind == HuntOpKind.Enroll`. `EnrollmentModel.Expect` (`Hunt/EnrollmentModel.cs:103-112`) treats `Enroll` and `EnrollAgain` the same: both are `NotSuccess` in a cut, and both count as KI-F67 `known` at `ModelHunter.cs:411-414`. A cut-window 500 logs "Unhandled exception" (I1, `Invariants/I1UnhandledInvariant.cs:74`). The excuse misses it for `EnrollAgain`. Run 11's original case is `[Mint(60), PgDown, EnrollAgain(7), PgDown, Mint(60), PgDown, EnrollAgain(0), ...]`. Op 2 names an unminted token, so its expectation is `None` and it is skipped (`ModelHunter.cs:350`). Op 6 is therefore the case's first enroll request: it is inside the cut, it got the cut 500 (job `known` 2, `carried` 0), and its own I1 diverged the case. The fake never queues an I1 for a cut 500 (`ModelHunterTests.cs:661,685-688`), so no test sees it. | lab defect (new); a false positive, **not** a product defect and not F114 or NR-1 | Inside KG-W5-13 (reworded, section 4). The fix is L27d. |
| P1 | The segment 2 audit cannot show the `svc_restart` target, the `hunt-warmup` label or the warm-up's 201. | procedure | Met by source plus outcome. `Service` is `"nodes"` (`ModelHunter.cs:44`). `WarmPoolAsync` (`:595-625`) mints with label `hunt-warmup`, probes via `nodes`, and returns `[Lab.Unavailable] nodes warm-up enroll answered N` for any status other than 201, which ends the job as infrastructure. All three jobs ended normally. Future run records should quote the job's own payload, not only the audit, because the audit line carries only the tool and outcome. |
| P2 | At step 0 an empty `meridian-lab-net` network was present; `stack_down` removed it. | procedure (hygiene) | Not a red. Future run briefs add "lab network absent" to the step 0 preconditions and remove it with a lease-held `stack_down`, never a prune. |
| P3 | The `nodes.jsonl` of `r11-home-2` has four "Unhandled exception (fallback handler)" lines on `/agents/enroll` sharing one TraceId. | not a finding | These are the cut-window 500s (KI-F67), one trace per hunt job. |
| P4 | The run's build ids end `-dirty`. | procedure | The likely cause is the untracked `tests/Dhadgar.Lab.Tests/TestResults/` in the campaign worktree. No `packages.lock.json` is modified in the campaign worktree or the main checkout. See section 7, step 3. |

There is no product defect in run 11. F114's product half (NR-1) did not show: no first-request-after-heal 500 appears outside a cut.

## 4. Known gaps: what the run closed, rewords and adds

| ID | Change | Why |
| --- | --- | --- |
| KG-W5-16 | **Closed** | Segment 1 passed in full: closure of 4 files, `stable`, high seed (section 7, condition 3). |
| KG-W5-18 | **Closed for new ingests.** Old rows stay permanent and accepted (D-ML141). | Every `results_query` seed is the unsigned value. |
| KG-W5-15 | **Closed** | A11 passed twice on a fresh daemon (D-ML130). |
| KG-W5-7 | **Closed for 5E** | A4 (`--recovery`) passed, so gate LS's recovery leg at 5E holds. The full gate LS stays at wave 10 (KG-W5-10). |
| KG-W5-13 | **Reworded, still live, parked (D-ML127):** "L27b and L27c fix the original failure, carried violations, repro parsing and the start state; replays reproduce on the same and on a new daemon (run 11). But a cut-window `EnrollAgain` 500 is still flagged as I1 (F125), and a hunt seed does not fix its cases or shrink (F124), so a live `diverged` result is not yet evidence. Until: L27d and L27e merged, then the campaign review's live hunt (D-ML74). A `diverged` result there must be triaged to a product cause. Owner of the fix: L27b, L27c, L27d, L27e." | F124, F125 |
| KG-W5-21 | **New, live, parked (D-ML126):** "The hunter's cases and shrink are not functions of the run seed (F124). `Hunter_SameSeed_SameFirstOpList` is flaky, and `iterations` is not honoured. Until: L27e merged. Owner: L27e." | F124 |
| KG-W5-17, -20, -19 and the rest | unchanged | not touched by run 11 |

One residual risk is recorded with KG-W5-13; it is not a new gap. The I1 monitor reads container log rings. If a cut-window 500's log line arrived after that op's `invariants_check`, the next op's check would see it, and an excuse keyed on the op would miss it. Run 11 does not show this: op 6's I1 is its own. If the campaign review's hunt diverges on the op just after a cut-window enroll, triage looks here first.

## 5. Decisions

| ID | Decision | Status |
| --- | --- | --- |
| D-ML142 | Waves 5 and 5E close on run 11 (`7351c6f7`). The section 7 conditions hold. KG-W5-16, -15 and -7 (for 5E) close, and KG-W5-18 closes for new ingests. F124 and F125 are lab defects parked under D-ML126: F124 as KG-W5-21, F125 inside KG-W5-13. Follow-ups L27d and L27e merge before the campaign review and before the campaign PR. | Accepted (architect) |
| D-ML143 | The hunter stops using `Check.Sample`. Each case comes from a seeded PCG drawn from `LabRandom(runSeed, "hunt:enrollment")`. The hunt runs at most `iterations` cases or until `budget_s` on the hunter's `TimeProvider`, whichever comes first. A divergence is shrunk deterministically by greedy one-op removal, bounded by 200 shrink cases and the same budget. The payload reports `cases_run`, `shrink_cases` and `budget_hit`. Rejected option: amend R-SEED to say a hunt seed fixes only the first case. That leaves the hunt unreplayable by seed, and the flaky test would have to be deleted. | Accepted (architect) |

## 6. Packets

Shared rules for both packets (inline; `packet-coder` skips CLAUDE.md):

- **R-RESULT:** validation, IO and network failures return `Result<T>` with a `[Lab.<Code>]` message; never throw for them. The hunter keeps its `CaseResult.Infrastructure` path for tool failures.
- **R-SEED:** every random choice comes from `LabRandom(runSeed, stream)` (`Random/LabRandom.cs`). No `System.Random`, no `Guid.NewGuid()` for data, and no CsCheck entry point that seeds itself.
- **R-TIME:** time goes through the hunter's `TimeProvider` (`_time`), never `Stopwatch`, `DateTime.UtcNow` or `Task.Delay` without it. Tests use `FakeTimeProvider`.
- **R-USING:** `using` / `await using` on every `IDisposable`.
- **R-CPM:** no package versions in `.csproj` and no new package. CsCheck is already referenced.
- Nullable is enabled and CI builds with warnings as errors. Do not add `!` to silence a warning that has a real null path.
- xUnit tests are named `Method_Case_Result`. Each new test must fail when its step is reverted; say so in the worknote.
- Never log or print a lease id, token ref or token value.
- No `dotnet restore -r` and no `publish -r`. If a `packages.lock.json` changes, revert it and report it.

Checks for both packets, in order:

```powershell
dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ModelHunterTests"
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
```

The trait is `RequiresDocker`. `Category!=Docker` excludes nothing (F123). Neither packet needs Docker.

Merge order: **L27d, then L27e**. Both edit `ModelHunter.cs` and `ModelHunterTests.cs`. L27e branches from the campaign tip after L27d merges. Either can run beside L19n, whose files are disjoint.

### 6.1 L27d: the cut-window I1 excuse covers `EnrollAgain` (F125)

- **Branch / worktree / commit:** branch `ml/l27d-hunt-enrollagain-excuse` from the campaign tip, worktree `.claude/worktrees/ml-l27d`, commit subject `fix(lab): excuse a cut-window I1 on EnrollAgain as on Enroll [L27d]`.
- **Depends on:** nothing.
- **Security review:** yes, one `security-advisor` pass. It is a lab tool that drives faults and enrollments, and the change narrows what the hunter reports as a violation.
- **Size:** small (about 12k tokens).
- **Files:**
  - `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`
  - `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`
  - `docs/analysis/meridian-lab/worknotes/ml-l27d.md` (new)
- **Steps:**
  1. `ModelHunter.cs:539`: change the excuse to

     ```csharp
     // KI-F67: a request sent inside a cut may answer 500 and log an unhandled exception; Enroll and EnrollAgain are the same
     // request (EnrollmentModel.Expect), so both are excused (F125). Nothing else is.
     bool allowed = model.PgDown && (op.Kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain);
     ```

     Keep the brackets for the reader. `or` is a pattern combinator, so it would bind the same way without them. Update the `CheckStateAsync` summary (`:488`) to say "no I1 outside a cut window on Enroll or EnrollAgain".
  2. `FakeLab.Enroll` (`ModelHunterTests.cs:683-688`): when `_pgDown && Pg500`, also do `_queuedI1++` before returning 500. That is the product's behaviour: a cut 500 logs an unhandled exception. Update the `Pg500` property with a doc comment: "Inside a cut the product answers 500 and logs an unhandled exception, so the next `invariants_check` returns one I1 (KI-F67)." A 503 cut answer (`Pg500` false) queues nothing.
  3. Write the worknote: the rule, run 11's case (section 3, row F125), and the test list.
- **Tests (each fails when its step is reverted):**
  - `Replay_EnrollAgainInsideCut_Pg500_IsNotDiverged`: `FakeLab { Pg500 = true }`, new hunter, `ReplayAsync([Mint, PgDown, EnrollAgain(0), PgUp])`. Assert success, `Diverged` false, and `Known` 1. With step 1 reverted it diverges at index 2 with `outside a PgDown window`. This is run 11's divergence.
  - `Replay_EnrollInsideCut_Pg500_IsNotDiverged`: the same with `Enroll(0)`. This is the control: it is green both before and after step 1. With step 2 it now exercises the I1 path.
  - `Replay_EnrollAgainOutsideCut_I1_Diverges`: the excuse must not widen past the cut.
    - Setup: `FakeLab { FlakyI1OnCheck = 3 }` and a new hunter, then `ReplayAsync([Mint, EnrollAgain(0)])`. The status matches (201 for `Created`), so `Compare` passes and only the I1 can diverge the case.
    - Which check returns the I1: for a new hunter the checks are the case-start checkpoint (1), the check after `Mint` (2) and the check after `EnrollAgain` (3). The warm-up calls no `invariants_check`. If the coder finds a different count, set `FlakyI1OnCheck` to the check after the `EnrollAgain` and record the number in the worknote.
    - Assert `Diverged` at index 1 with `outside a PgDown window`.
    - This test is green before and after step 1. It fails against a mutant that allows I1 on any `EnrollAgain`, which is its purpose.
  - `Hunter_PgDownEnroll500_IsKnownNotNew` stays green with step 2 in place (cut 500s now queue I1).
- **Live acceptance:** none separately. The campaign review's live hunt (D-ML74) is the check.
- **Docs owed:** the worknote. Nothing in `docs/meta/doc-update-map.md` covers the hunter (grep it to confirm).

### 6.2 L27e: the hunt is a function of its seed (F124, D-ML143)

- **Branch / worktree / commit:** branch `ml/l27e-hunt-seeded-sample` from the campaign tip **after L27d merges**, worktree `.claude/worktrees/ml-l27e`, commit subject `fix(lab): seeded, count-bounded hunt sample and deterministic shrink [L27e]`.
- **Depends on:** L27d merged.
- **Security review:** yes, one `security-advisor` pass. The scope is a lab tool, its seeds and the repro file.
- **Size:** about 35k tokens.
- **Files:**
  - `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`
  - `src/Tools/Dhadgar.Lab/Tools/Hunt/HuntModelTool.cs` (description string only)
  - `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`
  - `docs/analysis/meridian-lab/worknotes/ml-l27e.md` (new)
- **Steps:**
  1. Replace `Sample` (`ModelHunter.cs:216-276`) with an async `SampleAsync(ulong runSeed, int iterations, int budgetSeconds, CancellationToken ct)` that awaits `RunCaseAsync` directly. Remove the `Task.Run`, the `GetAwaiter().GetResult()` and the `CsCheckException` catch. Shape:

     ```csharp
     private const int MaxShrinkCases = 200;
     private const string SeedStream = "hunt:enrollment";

     private async Task<SampleFindings> SampleAsync(ulong runSeed, int iterations, int budgetSeconds, CancellationToken ct)
     {
         var rng = new LabRandom(runSeed, SeedStream);
         DateTimeOffset deadline = _time.GetUtcNow().AddSeconds(budgetSeconds);
         int throttled = 0, known = 0, carried = 0, casesRun = 0;
         for (int i = 0; i < iterations; i++)
         {
             if (_time.GetUtcNow() >= deadline) return Findings(budgetHit: true, ...);
             var pcg = new PCG(0u, rng.NextUInt64());
             string caseSeed = pcg.ToString();          // before Generate: it names this case
             List<HuntOp> ops = OpGen.List[1, 15].Generate(pcg, null, out _);
             CaseResult result = await RunCaseAsync(ops, ct).ConfigureAwait(false);
             casesRun++;
             if (result.Infrastructure is not null) return Findings(infrastructure: result.Infrastructure, ...);
             throttled += result.Throttled ? 1 : 0; known += result.Known; carried += result.Carried;
             if (result.Diverged)
             {
                 // The original is this case; shrink it (step 2).
                 (List<HuntOp> shrunk, int shrinkCases, bool budgetHit) = await ShrinkAsync(ops, deadline, ...);
                 return Findings(shrunk, original: ops, originalDetail: result.Detail, caseSeed, casesRun, shrinkCases, budgetHit, ...);
             }
         }
         return Findings(budgetHit: false, ...);
     }
     ```

     - `Findings` stands for building `SampleFindings`. Extend the record with `string? CaseSeed`, `int CasesRun`, `int ShrinkCases` and `bool BudgetHit`.
     - `known`, `throttled` and `carried` also add the shrink cases' counts, as the CsCheck version did.
     - `Generate`'s second parameter is CsCheck's `Size? min`, and CsCheck's own `Sample` passes `null`. If `null` does not compile under nullable, stop and return `blocked spec` with the signature. Do not add `!`, and do not construct a `Size`.
  2. Add `ShrinkAsync(List<HuntOp> failing, DateTimeOffset deadline, ...)`. This is greedy one-op removal:

     ```csharp
     List<HuntOp> current = failing; int shrinkCases = 0; bool progress = true;
     while (progress && current.Count > 1)
     {
         progress = false;
         for (int j = 0; j < current.Count; j++)
         {
             if (shrinkCases >= MaxShrinkCases || _time.GetUtcNow() >= deadline) return (current, shrinkCases, true);
             List<HuntOp> candidate = [.. current.Take(j), .. current.Skip(j + 1)];
             CaseResult r = await RunCaseAsync(candidate, ct).ConfigureAwait(false);
             shrinkCases++;
             if (r.Infrastructure is not null) return (current, shrinkCases, false) with infrastructure;  // ends the sample, as today
             if (r.Diverged) { current = candidate; progress = true; break; }   // restart from j = 0
         }
     }
     return (current, shrinkCases, false);
     ```

     - Removing a `Mint` shifts later token indexes. That is accepted: each candidate is judged by running it.
     - An infrastructure error during the shrink fails the job as it does today.
     - The shrink cap of 200 sets `budget_hit` true. Name the cap `shrink_limit_hit`, a separate boolean, if the coder finds that clearer; record the choice in the worknote.
  3. `RunAsync` (`:111-195`):
     - Call `SampleAsync`.
     - Drop `string cscheckSeed = new PCG(0u, runSeed).ToString();`.
     - `["cscheck_seed"]` and the repro's `cscheck_seed` line (`:699`) become `findings.CaseSeed`, the PCG seed string of the original diverging case. The repro key is unchanged, so `ReproParser` needs no edit (check this by grep).
     - Both payloads gain `["cases_run"]`, `["shrink_cases"]` and `["budget_hit"]`. The passed payload keeps `["iterations"] = iterations`, meaning the requested count, and adds `cases_run`.
     - The replay, `shrink_unstable`, `original_ops`, `original_detail` and `carried` logic stays exactly as it is.
     - Update the XML docs at `:100-110`: "draws up to `iterations` cases, each from a seed of `LabRandom(runSeed, "hunt:enrollment")`, for at most `budgetSeconds` on the hunter's clock; a divergence is shrunk by removing one op at a time".
  4. If nothing else in the file uses CsCheck's `Check`, remove `using` lines that now warn as unused. Keep `CsCheck` for `Gen`, `PCG` and `OpGen`.
  5. `HuntModelTool.cs:78`: append to the description "The hunt runs at most iterations cases or budget_s, whichever ends first; the same seed draws the same cases."
  6. Tests:
     - Fix `Hunter_SameSeed_SameFirstOpList`. Rename it `Hunter_SameSeed_SameOpsAndOriginal`. Build two hunters over **two new** `FakeLab { ReusedTokenReturns201 = true }` instances and run `RunAsync(Seed, 200, 30, ...)` once on each. Assert that `ops`, `original_ops`, `cscheck_seed` and `cases_run` are equal.
     - `Hunter_PlantedReuseBug_ShrinksAndReplays` keeps `InRange(ops.Count, 1, 3)`. Greedy removal gives a one-minimal list, so this holds.
     - `I1FromThirdOp`, `FlakyI1OnCheck` and every other `ModelHunterTests` test must stay green unchanged. The `I1FromThirdOp` doc comment (`:524-529`) says "Shrink length is bounded by time". Change it to "by `MaxShrinkCases` and time". If a test needs any other change, it is a spec gap: stop and return `blocked spec` with the failing assertion.
- **Tests to add (each fails when its step is reverted):**
  - `Hunter_SameSeed_SameCallSequence`: two new hunters over two new `FakeLab {}` instances, which never diverge. Call `RunAsync(Seed, 20, 30, ...)` on each and compare the full `lab.Arguments` lists: tool name, plus the `token_ref` of each `nodes_enroll_probe`. They are equal. With step 1 reverted, the CsCheck sample differs after the first case and the test fails.
  - `Hunter_DifferentSeed_DifferentCallSequence`: the same with seeds `Seed` and `Seed + 1`. The lists differ. This guards against a constant generator.
  - `Hunter_Iterations_CasesRunEqualsIterations`: `FakeLab {}`, `RunAsync(Seed, 20, 30, ...)`, clock not advanced. Assert `cases_run == 20` and `budget_hit == false`. With step 1 reverted, CsCheck runs the whole wall-clock budget and the field is absent.
  - `Hunter_BudgetSpent_StopsWithBudgetHit`: a `FakeLab` hook `OnInvariantsCheck` advances `_time` by 2 s on each call; `RunAsync(Seed, 500, 10, ...)`. Assert `0 < cases_run < 500` and `budget_hit == true`. Add `public Action? OnInvariantsCheck { get; set; }` to `FakeLab` and invoke it in the `invariants_check` branch.
  - `Hunter_SeedAboveLongMax_IsDeterministic`: `RunAsync(12499590173714643239UL, 10, 30, ...)` twice, on new hunters and new fakes. The call sequences are equal (memory: the ulong seed readers, F119).
  - `Hunter_AlwaysDiverging_ShrinksToOneOp`: `FakeLab { I1Violations = true }` makes every case diverge, so every one-op list diverges too. Assert:
    - `ops` has exactly 1 op;
    - `shrink_cases` equals `original_ops.Count - 1`, because each pass removes op 0 at once;
    - `budget_hit` is false.

    With step 2 reverted, the CsCheck shrink gives a random length and no `shrink_cases`.
  - `Hunter_BudgetSpentInShrink_KeepsSmallestSoFar`: `FakeLab { I1Violations = true }`, with `OnInvariantsCheck` advancing `_time` by 1 s, and `RunAsync(Seed, 50, 4, ...)`. Assert:
    - `budget_hit` is true;
    - `ops` is non-empty and no longer than `original_ops`;
    - the job still ends `fail` with the shrunk list replayed.

    The `MaxShrinkCases` cap of 200 is a safety net. Greedy removal over at most 15 ops runs at most 119 candidates, so no test can reach the cap. Say so in the worknote.
- **Live acceptance:** none separately. The campaign review's live hunt (D-ML74) uses `hunt_model_run seed 4242` twice on fresh daemons and expects the same `original_ops`, which is now possible.
- **Docs owed:** the worknote, covering the rule, why CsCheck's `Sample` was dropped (the probe facts from section 3, row F124), the payload fields, and the test list. The lab guide's hunt section is L45's job; add nothing there now.

## 7. What unblocks, in order

1. **L19n (seam S-L15), now Ready.** Run it on a fresh daemon on the campaign tip, per [nodes-ef-retry-packet](nodes-ef-retry-packet.md) section 5: first red on the tip (KG-W5-17 becomes live), then green in a scratch worktree with NR-1's Nodes diff applied. If the first probe answers 401 before NR-1, its hard stop applies.
2. **NR-1's PR**, after S-L15 is green in scratch. ops pushes `fix/nodes-ef-retry` and opens the PR to `main`, and **the owner merges it** (D-41). The PR needs no `security-review` label (D-ML132).
3. **Lock-file cleanup.** Nothing to clean. No `packages.lock.json` is modified in the campaign worktree or the main checkout. The only untracked path in the campaign worktree is `tests/Dhadgar.Lab.Tests/TestResults/`. It is not ignored, and it is the likely reason for the `-dirty` build ids. ops deletes that folder by explicit path before L19n's run, and every run brief gets the line "delete `tests/Dhadgar.Lab.Tests/TestResults/` before the run". If the orchestrator meant a different lock file, name it; this ruling found none.
4. **Merge `origin/main` into `campaign/meridian-lab`** once NR-1 and the vertical-slice Nodes PRs are on `main`. Merge, never rebase (D-38). Then rerun the non-Docker suite (`RequiresDocker!=true`), S-L15 (KG-W5-17 closes when green), A6 (S-L6) and A11 (S-L7) on a fresh daemon. Any conflict in `src/Dhadgar.Nodes` goes to the architect.
5. **L27d, then L27e** (section 6). They run in parallel with steps 1 to 4. Each gets one packet-reviewer and one security-advisor. They are needed before the campaign review.
6. **Follow-up specs.** L29b, L54b, L55b, L05b, L25g, L28b and L52b are outlines. The architect specs each before dispatch. Order: L54b and L55b before the wave 10 gates; L25g before L52b; L28b and L29b any time.
7. **W8 deltas note** (`worknotes/W8-sprint2-deltas.md`). It is due before L39 and L40 and can be written now. It is not blocked by anything.
8. **Next waves.**
   - **L30 can start now.** It is the pool model with Linux and host providers, and it depends only on L19 (`work-packets.md:3471`). It needs neither Windows containers nor L00.
   - **L31 to L33 stay blocked.** L31 needs the L00 settings block (`work-packets.md:3519`), and L00 is parked because Windows containers are off. D-ML37 and D-ML38 are not raised. Nothing in run 11 changes this.
   - **Wave 7 can start beside L30.**
     - L34 and L36 depend on L22 and L12, both met.
     - L35 follows L34.
     - L37 needs L30 and L34.
     - L38 needs L35, L36 and L37.
     - L56 needs L35 (L53, L54 and L11 are met).
   - Suggested first dispatch: L30 ‖ L34 ‖ L36. The packets merge one at a time and each rebases on the tip, because they share `ToolPolicy.cs` and `LabServices.cs`. The wave 6 gate (S-L8) still waits on L00.
9. **Close-out doc sweep** (one documentation-writer task, any time): mark `wf-r7.md` and `wf-r8.md` as superseded where their ledgers do, and replace the remaining `Category!=Docker` lines in the run 9 and run 10 rulings with a pointer to F123.

## 8. Owner questions

None are new. These are still open and none blocks the next steps:

- **D-ML138:** carried out on option A. The owner ratifies it by action.
- **D-ML133:** the Nodes outbox.
- **D-ML120:** Nodes start without its database.
- **D-ML107:** fuzz 500s (KG-W5-1).
- **D-ML37 / D-ML38:** wave 6, only if Windows containers are turned on.

One timing note for the owner, not a decision: the merge of `main` into the campaign (section 7, step 4) waits on the owner merging NR-1.
