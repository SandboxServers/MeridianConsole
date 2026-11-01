# wf-fgh-fix worknote (wave5-fix-FGH, ruling D-ML114, D-ML115, D-ML116)

Branch `ml/wf-fgh-fix`, base `87d63e80`. Commits: FGH-a `0ef5dc0f`, FGH-b `6c1f1692`. Coder report: `.claude/reports/meridian-lab/wave5-fixFGH-coder.md`.

## 1. Forced changes

The ruling's test list does not catch the window-prefilter revert. No named test has an excuse that starts before the next window's `From - 30 s` and ends inside that window, so the revert stayed green with the ruling's tests alone.

Added `I6_LongSvcRestart_StartBeforeTheWindow_StillExcuses` (`tests/Dhadgar.Lab.Tests/Invariants/I6RestartInvariantTests.cs`). It is not in the ruling's list. The excuse starts at T0 and the second window opens at T0+1min, so the old `At >= From - Margin` filter drops it. With the test added, that revert goes red.

## 2. FGH-a revert runs

Each revert was applied, built, tested, then restored before the commit.

1. Plain `stack_up` back to `*` (`ReadBool` removed from the branch). Failed 2 of 31 in the I6 and LabRestartExcuses filter. Failing tests: `I6_StackUpExcuse_DoesNotExcuseNodes`, `ForCall_StackUp_ExcusesTheInfraContainers`.
2. Window prefilter restored in `I6RestartInvariant.CheckAsync`. Failed 1 of 32. Failing test: `I6_LongSvcRestart_StartBeforeTheWindow_StillExcuses`. Before the added test existed, this revert was green (31 of 31).
3. `RecordCall` moved back before the `try`, unconditional. Failed 2 of 54 in the filter (LabRestartExcuses, LabToolDispatcher, ToolPolicy). Failing tests: `Dispatcher_FailedStackUp_RecordsNoExcuse` (expected), and `Dispatcher_AcceptedSvcStart_RecordsTheExcuse` (its `Until` assert, because the clock has not advanced when the excuse is recorded).

## 3. FGH-b revert

`DaemonUri` changed to call `LabHosts.IsLabEndpoint`. Failed 2 of 9 in `SelfCheckRecoveryGuardTests`: `ToolCallUrl_IsTheDaemonMcpEndpoint` and `StatusUrl_IsTheDaemonStatusEndpoint`. Restored to `IsDaemonEndpoint`, rebuilt, 9 of 9 passed.

Reading check: `GetStatusAsync` and `CallToolAsync` both route through `DaemonUri`.

## 4. Suite counts

- Check 4 (filter, Docker class `SelfCheckRecoveryTests` excluded): 89 passed, 0 failed.
- Check 5 (non-Docker suite) after FGH-a: 1431 passed, 5 skipped.
- Check 5 (non-Docker suite) after FGH-b: 1436 passed, 5 skipped (1441 total).
- Baseline at fix-H: 1415 passed, 5 skipped.

## 5. Guides quoting `selfcheck --recovery` output

None found under `docs/` outside `docs/analysis`. The `selfcheck --recovery` output now gains `note:` lines. No guide needs an update for this packet.
