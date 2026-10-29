# wf-b worknote: wave5-fix-B (jobs, hunter, fuzz, soak)

This is the wave5-fix-B worknote (ruling name `wave5-fix-B.md`).

## Pass 2 additions (ruling line 406)

- Failing fuzz cases live only in the job result until L28b. Writing fuzz rows through `BundleWriter` and ingesting them needs a row shape and coverage keys, which is too large for this pass (R2-N11).

Packet: `wave5-fix-B` in `worknotes/wave5-ruling-1.md` (section B, D-ML100, D-ML101, R2-N3..N10, F92).

## Done (uncommitted at time of writing, session 2)

- B1 JobRunner catch: `JobRunner.cs` catch no longer filters OCE. Test `Job_UnrequestedCancellation_EndsFailed_AndTheNextStartIsAccepted`.
- B2 FlakeCheck keys: `FlakeCheckJob.cs` keys are `plan.Keys`. Test `FlakeCheck_UnselectedFailingRow_IsNotAFlake`.
- B3 ModelHunter: `invariant_id` read; mint without token_ref is Infrastructure; op.Token bounds; 429 retry; `HealAsync` catches and logs (ILogger optional last ctor param).
  - Forced test change: `ModelHunterTests.FakeLab` invariants_check answers the real `InvariantViolation` shape (F92).
  - New tests: `Hunter_I1OutsidePgDown_FailsTheCase`, `Hunter_429ThenOk_RetriesOnce`, `Hunter_429Twice_IsThrottled`, `Hunter_MintWithoutTokenRef_IsInfrastructure`, `Hunter_HealThrows_CaseStillEnds`.
- B4 Fuzz leak oracle: `FuzzOracle.Judge` takes `leakCheck`; `FuzzJob` takes required `invoke` and optional `leakCheck`; `FuzzRunTool` resolves services and passes `LeakCheck(SecretVault, EdgeState)`.
  - Tests: `FuzzOracleTests.Judge_LeakCheckTrue_IsALeak_AndTheFindingOmitsTheBody`; new `tests/Dhadgar.Lab.Tests/Fuzz/FuzzRunToolLeakCheckTests.cs` (3 tests).
  - `FuzzJobTests`: the six `new FuzzJob(...)` calls pass `CleanInvoke`. Forced change, no assertion changed.
  - Open risk closed: `invariants_check` is `LabGate.Open` in `Policy/ToolPolicy.cs`, so the null lease id from FuzzJob is accepted.
- B5 end-of-job invariants:
  - `FuzzJob.RunAsync`: `invariants_check` with `since` = job start, caller `job:fuzz`; payload `invariants {checked, violations, errors}`; violation, call error or errors fails the job. Test `FuzzJob_InvariantViolationAfterCases_FailsTheJob`.
  - `SoakJob.RunAsync`: `invariants_check` inside `try` after the loop, before `store.End()`, `since` = soak start, caller `job:soak`. I7 in violations, errors or a call error sets overall fail. I7 in skipped gives `i7 = "skipped"`. Otherwise `i7 = "checked"`. A call error gives `i7 = "error"` (the spec names no value for it). A cancelled soak never reaches the call.
  - `SoakJobTests`: `Invoke` answers `invariants_check` from `_invariantsAnswer` (clean by default) and counts calls in `_invariantCalls`. Cancelled test asserts no call. New tests `SoakJob_RisingTrend_FailsTheOutcome`, `SoakJob_I7Skipped_IsReportedNotFailed`.

## Left

1. Read the check result of the area filter run (see Checks). If green, run the full non-Docker suite of `Dhadgar.Lab.Tests` (the packet's last check), then write the report and commit.
2. Report: append to `.claude/reports/meridian-lab/wf-b-coder.md`; commit only the packet's files.

## Checks

- Release build `src/Tools/Dhadgar.Lab/Dhadgar.Lab.csproj`, CI=true, TreatWarningsAsErrors: Build succeeded (exit 0).
- Release build `tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj`, same flags: Build succeeded (exit 0).
- Area filter test run (JobRunner, FlakeCheckJob, ModelHunter, Fuzz, SoakJob, I7): Passed! - Failed: 0, Passed: 69, Skipped: 0, Total: 69.
- Full non-Docker suite (`RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests`): Failed! - Failed: 19, Passed: 1303, Skipped: 5, Total: 1327. 5 SingleInstanceGuardTests failed on a per-user lock held by concurrent runs; they pass on rerun. 14 BarrierProxyTests fail with "address already in use" on 127.0.0.1:25000, held by Docker Desktop (`com.docker.backend`, pid 30644). Outside this packet and not caused by its code.
