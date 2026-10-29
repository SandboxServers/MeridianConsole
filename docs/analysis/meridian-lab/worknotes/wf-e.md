# wf-e worknote: wave 5 review fix pass 2 (wave5-review-2 and wave5-security-2)

Branch `ml/wf-e-tools`, base b9e693f0. Reports: `.claude/reports/meridian-lab/wave5-review-2.md` and `wave5-security-2.md` (meridian-lab worktree).

## Done

- Security N1 (patch scope bypass): `MutantBuilder.PatchTouchesOnly` is public. A `diff --git` line must name one listed path on both sides with no quoted name, else `[Lab.InvalidArgument] change.patch has a header the scope check cannot read` (or `touches a file that files does not list`). A `---`/`+++` line with a quote is refused the same way. Test: `MutantBuilderTests.Build_PatchWithQuotedHeaderAndNoMinusPlusLines_IsRefusedBeforeGitApply` and `PatchTouchesOnly_UnreadableOrMismatchedHeader_IsRefused`.
- Security N2 (lab-mutant path has no scope check): `SelfCheckRunner.PatchScopeRow` runs the same check before `git apply`. `Dhadgar.Lab.SelfCheck` now has `InternalsVisibleTo("Dhadgar.Lab.Tests")`. Tests in `SelfCheck/SelfCheckRunnerGuardTests.cs`.
- Security N3 (token in child env): `RunProcessAsync` removes `MERIDIAN_LAB_TOKEN`. Test `RunProcessAsync_ChildDoesNotInheritTheDaemonToken`.
- Security N4 (cleanup filter): `name=^meridian-lab-` added to the sweeps in `TwinLatencyDockerTests` and `TenantTwinSeamTests`; the S6 wipe pre-check copied into `TenantTwinSeamTests`.
- Security N5 (payload shape): `TwinRunner.ErrorDetail` (internal) guards `ValueKind` before `TryGetProperty`. Tests `ErrorDetail_*`.
- Security N6 (build files as mutant targets): `MutantSpecLoader.IsBuildFile` deny list. Test `MutantSpecTests.ProductFiles_BuildConfiguration_IsRefused`.
- Review N1 (twin ingest test): `BundleIngestorTests.Ingest_TwinPair_StoresATwinSignatureWithThePointer`. Passes. The revert-verify step (delete `RowVerdicts.cs:159-165` and watch it fail) was not run in this pass.
- Review N2 (heal catch test): the catch is unreachable through the dispatcher, so the test is renamed `Twin_HealToolFails_ResultStillReturned` and its comment says it does not exercise the catch (fallback in the note).
- Review N3 (worknotes): `wf-a.md`..`wf-d.md` carry a header line and a pass-2 section with the OD-W5-1 default, R1 item 7, the known-issue removals, the forced changes, the L28b line, the D-ML103 rule and the D-ML102 messages.

## Not done, and why

- Security N2 (preferred part): the `git apply --numstat` cross-check is not added. The fail-closed header reader closes the reported gap; the git-side list is an optional second check.
- Review N2 verify: `TwinRunner.cs` catch cannot be reached without a source change (the dispatcher swallows the throw).
- Security N5 verify: a `stack_reset_data` string payload cannot reach `ResetAsync` through the dispatcher (every refusal is an object with `error`); the guard is tested via `ErrorDetail` directly.

## Checks

- Release build, CI=true, TreatWarningsAsErrors: Build succeeded (0 errors).
- Non-Docker filter on MutantBuilderTests, MutantSpecTests, TwinRunnerTests, SelfCheckRunnerGuardTests, BundleIngestorTests: Passed 67, Failed 0.

## Next step

None for this pass. Commit as `fix(lab): wave 5 review fix pass 2 [wave5]`.
