# R10-1 worknote: bundle seeds as ulong; flake check rows by id or key (F119, F121, D-ML135)

## Done
- `Results/BundleSeed.cs` (new, internal static): `ReadSeed` (ulong, null when absent or not a non-negative integer), `ToStored` and `FromStored` (bit pattern, unchecked cast).
- `Results/BundleIngestor.cs`: `ParsedSeed` removed; the three seed readers use `BundleSeed.ToStored(BundleSeed.ReadSeed(run) ?? 0)`. Storage rule: SQLite has no unsigned 64-bit type, so the results DB keeps the seed's bit pattern in its `long` columns.
- `Results/FlakeCheckJob.cs`: seed read as ulong and refused with the same text when absent; result `seed` written as ulong. Rows named by id or `section/row` key; a bare id in more than one section returns `[Lab.InvalidArgument] ... is ambiguous; name it as section/row`; an unknown id keeps `[Lab.NotFound]`.
- `Triage/DraftWriter.cs`: `DraftSource.Seed` is ulong; `BundleSeed.FromStored` converts the stored long; printed with `CultureInfo.InvariantCulture`.
- `Tools/Jobs/JobTools.cs`: description string names the key form.
- Tests: FlakeCheckJobTests (4 new), BundleIngestorTests (2 new), DraftWriterTests (1 new).

## Known deviation
- `DraftSource.Seed` is `ulong` (the packet said ulong at `:153` as a local). Conversion from the stored long happens in `DraftWriter` via `BundleSeed.FromStored`. Output text is the same as packet step 5.

## Left
- Test run: Dhadgar.Lab.Tests 1496 passed, 2 failed, 5 skipped (1503 total). All 7 new tests pass. The 2 failures are outside this packet: Hunt.ModelHunterTests.Hunter_SameSeed_SameFirstOpList (timed out at 1 m) and Seams.TenantTwinSeamTests.TenantIsolationEnroll_Holds ("twin_run failed: no such spec"). Neither file is touched by this packet. Not re-run (one run allowed).
- Commit with explicit paths only.

## Next step
- Done: committed.

## Last check
- `dotnet test tests\Dhadgar.Lab.Tests -c Release --filter "Category!=Docker"`: Failed: 2, Passed: 1496, Skipped: 5, Total: 1503.
