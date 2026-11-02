# ml-l27b worknote (L27b, F114/F116, KG-W5-13)

## Status
Code complete and committed (continuation 2 of 3). Commit f562565d353cebd77eada4028a2a5d8905be1d3d,
subject `fix(lab): isolate hunt cases and make every repro replayable [ml-l27b]`.

## What changed
- Case-start `invariants_check` runs after the single start-of-case `fault_reset`.
- `_cutSinceRestart` is restarted at each case boundary so cut state does not leak between cases.
- `SampleFindings` now distinguishes `Original` and `Carried` findings.
- `shrink_unstable`, `carried`, and `original_*` fields are emitted in hunt results.
- `ReproParser` accepts `PgDown`, `PgUp`, and `RestartNodes` actions.
- `Mint(60)` round-trips through the repro format (covered by `ReproRoundTripTests`).

## Deviations
- The brief said "second fault_reset". It was read as the one start-of-case reset; no second reset was added.
- The docs edit was skipped because no docs file was named in the packet.

## Checks
- `dotnet test tests\Dhadgar.Lab.Tests ... --filter "FullyQualifiedName~Hunt&RequiresDocker!=true"`:
  `Passed!  - Failed: 0, Passed: 27, Skipped: 0, Total: 27` (Duration 8 m).
- Full non-Docker/non-Windows-container/non-network Lab run: see report.

## Next step
None for the code. Remaining: confirm the full Lab run result recorded in the report, then close the packet.

> Superseded in part by L27c (`0c1900b3`): `_cutSinceRestart` is now `_needsRestart`, which starts true for a new hunter; see [ml-l27c](ml-l27c.md).
