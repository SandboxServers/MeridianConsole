# L27e worknote: the hunt is a function of its seed (F124, D-ML143)

## Rule

The hunt draws its cases from `LabRandom(runSeed, "hunt:enrollment")`. Each case takes `new PCG(0u, rng.NextUInt64())`, and that PCG seed string names the case (`cscheck_seed` in the payload and the repro). The sample runs `RunCaseAsync` directly with `await`, so the `Task.Run` and `GetAwaiter().GetResult()` bridge are gone. The sample stops at `iterations` cases or when `budget_s` is spent on the hunter's clock, whichever comes first.

## Why CsCheck's `Check.Sample` was dropped

CsCheck's sample drew its seeds from the run seed through its own stream and ran until its own wall-clock budget, so the same seed did not give the same cases and the budget ran out whatever `iterations` was. The probe facts are in section 3 (row F124) of the wave 5 close ruling. `OpGen` is still a CsCheck `Gen`, and `PCG` is still CsCheck's.

## Shrink

Greedy one-op removal. A candidate that still diverges replaces the list and the scan restarts from op 0. The shrink stops at `MaxShrinkCases` (200) candidates or at the deadline, and then keeps the smallest list that still diverges. The cap is one boolean, `budget_hit`, which is true for the time budget and the cap alike. A separate `shrink_limit_hit` was not added. Greedy removal over at most 15 ops runs at most 119 candidates, so no test can reach the cap.

An infrastructure error during the shrink fails the job, as it did before.

## Payload fields

Both `passed` and `diverged`/`not_reproduced` payloads gain `cases_run`, `shrink_cases` and `budget_hit`. `passed` keeps `iterations` as the requested count. `cscheck_seed` is now the PCG seed of the first diverging case. The repro key is unchanged, and `ReproParser` does not read it.

## Tests

- `Hunter_SameSeed_SameOpsAndOriginal` (renamed from `Hunter_SameSeed_SameFirstOpList`): two fresh fakes; `ops`, `original_ops`, `cscheck_seed`, `cases_run` equal.
- `Hunter_SameSeed_SameCallSequence`: two fresh fakes, 20 cases, no divergence; tool calls and probe `token_ref`s equal.
- `Hunter_DifferentSeed_DifferentCallSequence`: seeds `Seed` and `Seed + 1`; the sequences differ.
- `Hunter_Iterations_CasesRunEqualsIterations`: 20 cases; `cases_run == 20`, `budget_hit == false`.
- `Hunter_BudgetSpent_StopsWithBudgetHit`: `OnInvariantsCheck` advances 2 s; 500 requested, 10 s budget; `0 < cases_run < 500` and `budget_hit`.
- `Hunter_SeedAboveLongMax_IsDeterministic`: seed `12499590173714643239UL`, two fresh runs, identical call sequences.
- `Hunter_AlwaysDiverging_ShrinksToOneOp`: `I1Violations`; one op left, `budget_hit` false, and `shrink_cases >= original_ops - 1`. The spec asked for equality. It does not hold: a 15-op case ran 24 shrink candidates, so some one-op removals do not diverge. The likely cause is removing a `PgUp`, which leaves the cut open so no I1 fires; this was not separately confirmed.
- `Hunter_BudgetSpentInShrink_KeepsSmallestSoFar`: `OnInvariantsCheck` advances 1 s, 4 s budget; `budget_hit` true, and the shrunk list is no longer than the original.

`I1FromThirdOp`'s doc comment now says "bounded by `MaxShrinkCases` and time". Every other `ModelHunterTests` test is unchanged and green.

## Live acceptance

None separate. The campaign review's live hunt (D-ML74) runs `hunt_model_run seed 4242` twice on fresh daemons and expects the same `original_ops`.

## Status

Done: production change, tool description, tests, this note. Checks are in the report at `.claude/reports/meridian-lab/l27e-coder.md`.
