# ml-l27c worknote: hunt cases start on a restarted, warmed Nodes pool (F120, D-ML136)

## Status

Done and committed (continuation 2). Nothing is left in the packet.

## Done

- `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`: a case starts with `fault_reset`, then `svc_restart` and `WarmPoolAsync` when `_needsRestart` or a cut is in play, then `stack_reset_data` and the case-start checkpoint. `_needsRestart` starts true and is set after a cut. The warm-up mint is `hunt-warmup`, and a non-201 answer is `[Lab.Unavailable]`.
- `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`: FakeLab models idle pooled connections; the packet's test list is covered.
- The fake's per-reset token cap exempts `hunt-warmup` mints (the warm-up token is discarded by the next reset).
- HealThrows throws once (not on every heal), so the case-start path cannot throw out of `RunCaseAsync`. Judged legitimate; see the report.

## Rule and evidence (F120, D-ML136)

- Rule: a case starts with fault_reset, then svc_restart plus a 201 warm-up enroll via nodes when the hunter is new, the previous case cut, or the case cuts; then stack_reset_data and the case-start checkpoint.
- Why `FakeLab` models idle pooled connections: a cut only fails an enroll that takes a pooled connection the cut killed, so a fake with a stale flag could not tell a restarted, warmed pool from an empty one; `_idle`/`_staleIdle` make the warm-up matter.
- Evidence: Run 10: the in-hunt replay started on a restarted warm pool and reproduced; the separate `replay=` job had a new hunter, no restart and an empty pool and gave 201. See wave5-run10-ruling.md section 3 and 7.2.

## Hang

Not a deadlock. Each `RunAsync` spends about 30 s of real time in the CsCheck budget. The earlier 90 s blame timeout was too short.

## Checks (final)

- `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true`: Build succeeded.
- `dotnet test ... --filter FullyQualifiedName~ModelHunterTests --blame-hang-timeout 20m`: Passed! Failed 0, Passed 25, Skipped 0, Total 25 (9 m).
- `dotnet test ... --filter "Category!=Docker"`: Failed! Failed 2, Passed 1487, Skipped 5, Total 1494 (18 m 39 s). Both failures are environmental and outside the packet:
  - `ToxiproxyTimeoutToxicDockerTests.TimeoutToxic_AddedToAnOpenConnection_HoldsTheNextRequest`: it carries `RequiresDocker`, not `Category=Docker`, so the filter let it run. Error: nodes did not answer 200 within 90 s.
  - `TenantTwinSeamTests.TenantIsolationEnroll_Holds`: labelled lab containers from another stack exist; the test wants `MERIDIAN_LAB_S6_WIPE=1` to wipe them. Not set, because that would remove containers this worktree does not own.

## Next step

None for this packet. Review may follow.
