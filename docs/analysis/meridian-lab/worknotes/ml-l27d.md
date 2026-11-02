# L27d: the cut-window I1 excuse covers EnrollAgain (F125)

## Rule

`ModelHunter.CheckStateAsync` (`src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`) excused an I1 violation inside a PgDown cut only for
`HuntOpKind.Enroll`. `EnrollmentModel.Expect` treats `Enroll` and `EnrollAgain` as the same request: both are `NotSuccess` inside a
cut, and both count as KI-F67 `known` on a 500. The excuse now covers `Enroll` and `EnrollAgain` (`op.Kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain`).
Nothing else is excused: an I1 outside a cut window still diverges, and an I3 still fails.

KI-F67: a request sent inside a cut may answer 500 and log an unhandled exception (I1, `Invariants/I1UnhandledInvariant.cs`).

## Run 11's case (wave5-close-ruling.md, section 3, row F125)

`[Mint(60), PgDown, EnrollAgain(7), PgDown, Mint(60), PgDown, EnrollAgain(0), ...]`. Op 2 names an unminted token, so its
expectation is `None` and it is skipped (`ModelHunter.cs`, the `expected` check). Op 6 is the case's first enroll request: it is inside
the cut, it got the cut 500 (job `known` 2, `carried` 0), and its own I1 diverged the case. The excuse did not cover `EnrollAgain`.

## Fake change

`FakeLab.Enroll` (`tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`): when `_pgDown && Pg500`, it queues one I1 before it returns 500.
That is the product's behaviour. A 503 cut answer queues nothing. The `Pg500` property has a doc comment saying so.

## Tests

- `Replay_EnrollAgainInsideCut_Pg500_IsNotDiverged`: `[Mint, PgDown, EnrollAgain(0), PgUp]`, Pg500. Not diverged, `Known` 1.
  Reverted rule: diverges at index 2 with "outside a PgDown window".
- `Replay_EnrollInsideCut_Pg500_IsNotDiverged`: the control, same with `Enroll(0)`. Green before and after the rule change.
- `Replay_EnrollAgainOutsideCut_I1_Diverges`: `FlakyI1OnCheck = 3` on `[Mint, EnrollAgain(0)]`. The check numbering for a new hunter is
  start checkpoint 1, after Mint 2, after EnrollAgain 3. Diverges at index 1 with "outside a PgDown window".
  Green before and after the rule change; it guards against widening the excuse.
- `Hunter_PgDownEnroll500_IsKnownNotNew`: unchanged, green with the fake change.

## Checks

- `dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: 0 errors (53 pre-existing
  IL2026 warnings in `Dhadgar.Agent.Windows`, outside this packet).
- `dotnet test ... --filter "FullyQualifiedName~ModelHunterTests"`: see the report.

## Next step

None. The packet is complete. The campaign review's live hunt (D-ML74) is the acceptance check.
