# Lab mutants

A mutant is a deliberately broken copy of the product (kind `product`) or of a lab tool (kind `lab`), used to prove that
the lab can see the failure it claims to see (D-ML51). Each mutant folder holds:

- `mutant.toml`: the spec (schema 1). `base` is the commit the patch applies to, `files` lists every file the patch
  touches, `detector` names the run that must catch it and `expect_pattern` the signature that proves it.
- `change.patch`: a `git diff` against `base`.

## What ships

Nothing here ships. No product project, props file or workflow references this folder. A product mutant is applied only
in a `git archive` copy under `LabPaths.Builds` (`lab_build {mutant}`, then `mutation_run`), never in a checkout and never
committed in patched form. The product tree on `main` stays unchanged.

## Current mutants

| Id | Kind | Change | Detector |
| --- | --- | --- | --- |
| M1-token-no-concurrency | product | drop `IsConcurrencyToken()` on `EnrollmentToken.UsedAt` | `history_run enroll-race`, E1 or E6 |
| M4-no-concurrency-catch | product | remove the `DbUpdateConcurrencyException` catch in enrollment | `history_run enroll-race`, I1 500 |
| M5-token-logged | product | log the plaintext enrollment token | `uat_run lab-selftest.toml`, I4 |

## Refreshing a stale patch

`MutantPatchesApplyTests` runs `git apply --check` for every patch. A failure names the mutant. The refresh steps:

1. Read the failing product file at HEAD and find the moved anchor.
2. Make the change again by editing the product file in the worktree, then run
   `git diff -- <file> > tools/lab/mutants/<id>/change.patch` (use `--output=` so the bytes stay exact).
3. Restore the product file byte for byte from a backup taken before the edit. Do not commit the product file.
4. Set `base` to the new HEAD sha in `mutant.toml`.
5. Confirm `git status` shows only `tools/lab/` and `src/Tools/` paths.

## Rules for a new mutant

- Only the files in `files` may appear in the patch: product mutants touch `src/` outside `src/Tools/`; lab mutants touch
  `src/Tools/` only (`MutantPatchesTouchOnlyDeclaredFiles`).
- A verdict of `survived` is a finding. Never weaken a mutant or its detector to make it pass.
