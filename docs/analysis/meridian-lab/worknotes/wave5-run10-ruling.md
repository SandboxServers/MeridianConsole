# Wave 5 / 5E: run 10 ruling

Architect ruling, 2026-11-02, on [wave5-docker-run](wave5-docker-run.md) "Continuation 10" and the run record `.claude/reports/meridian-lab/wave5-docker-run-10.md` (gitignored). Campaign tip `4aeb6b71` (L27b merged), docs commit `4b508757`. Inputs: [run 9 ruling](wave5-run9-ruling.md) (D-ML126 to D-ML133), the ledger README, the scratchpad homes `r10-home-1` and `r10-home-2`, and the code at the tip.

## 1. Ruling

- **Run 10 is not the D-ML130 confirmation run.** It repeated run 9's reds-only set (hunt, M5, M4, M1, twin, chaos, fuzz, plus A11 and A3 once) instead of D-ML130's set. It also ran before boundary review pass 4 (D-ML129), which has no report. The boundary is still **open**. Nothing from run 10 closes or reopens a gate it was not meant to test (F118).
- **Three lab defects, all with follow-ups, none a Docker fix wave.**
  - **F119, seed:** seeds of 2^63 or more are dropped wherever a bundle seed is read as `long`. `results_flake_check` refuses those runs, the results DB stores seed 0, and triage drafts print "Seed: 0". This affects about half of all random seeds. Packet R10-1.
  - **F120, hunt start state:** whether a cut case gives a 500 depends on Nodes' idle connection pool when the case starts, and that pool is not part of the op list. L27b restarts Nodes only after a cut. The in-hunt replay therefore started on a freshly restarted, warm pool and reproduced the failure, while the separate replay ran with a new hunter, no restart and an empty pool, and gave 201. Packet L27c.
  - **F121, flake check rows:** `results_flake_check` accepts only bare row ids, but it reports `section/row` keys. Folded into R10-1.
- **One lab defect parked:** mutation detector bundles carry the plain build id, although the services ran the mutant build (F122). This becomes KG-W5-19, with an outline follow-up L54b. It gates nothing at this boundary.
- **Product defects, unchanged:**
  - The hunt's 500 is F114's product half (NR-1 plus S-L15, D-ML128).
  - The fuzz result, 28 of 50 HTTP 500, is KG-W5-1 (D-ML107).
- **Met again and not rerun:** A11 once, A3, gate MD (M1, M4 and M5 `detected`), twin, chaos and teardown.
- **Order to boundary close:**
  1. R10-1 and L27c, each with a review and a security pass.
  2. Boundary review pass 4, over the range extended to the new tip (D-ML137).
  3. Review fixes.
  4. The confirmation run. This is D-ML130's set plus two probes: R9-1 (flake check dependency path, on a plain run with a high seed) and the hunt replay consistency check (section 6).
  5. L19n.
- **Owner:** one BlockedDecision, D-ML138. Should R10-1 and L27c land before the confirmation run, and should KG-W5-13 be gated on L27b plus L27c? The proposed default is yes for both. Work proceeds on the default until the owner rules.

## 2. Gate by gate

| Gate or item | Run 10 result | Ruling |
| --- | --- | --- |
| Build (Lab, Lab.Tests, warnings as errors) | Green | Met |
| A11 S-L7 | Passed once | Met once more; D-ML130 still asks for two runs plus A10 |
| A3 M1 Docker | Passed | Met (not in D-ML130's set; carried) |
| A1, A4, A5, A6, A10, non-Docker Lab.Tests | Not run | **Owed** (D-ML130; F118) |
| Gate MD (M1, M4, M5 alone) | All `detected`, same signatures as run 9 | Met (carried from run 9; no related code changed) |
| Twin `enroll-latency-invariance` | Pass, held, diffs `[]` | Met (carried) |
| Chaos `nodes-enroll` 600 s | 0 violations, 147 loops, 294 enrolls | Met (carried) |
| Fuzz enroll 50 | 28 of 50 HTTP 500 | KG-W5-1 (owner, D-ML107) |
| Hunt seed 4242 | `diverged` at op 4, `Enroll(0): expected 201, got 500` | Product: F114's 500 after a database blip (NR-1). Lab: see the next row |
| Hunt repro replay | `not_reproduced` | Lab defect F120. KG-W5-13 stays live and is gated on L27c too (D-ML138) |
| R9-1 flake check, dependency path | Refused `[Lab.InvalidArgument] the run has no spec or seed to rerun` | Not evidenced. Lab defect F119 (the seed is above 2^63), and the source was a detector run (F118, F122). Re-probed in the confirmation run after R10-1 |
| Boundary review pass 4 (D-ML129) | Not run | **Owed** (F118) |
| Teardown | Clean, label-only, no prune | Met |

## 3. Classification

| Result | Class | Evidence | Disposition |
| --- | --- | --- | --- |
| Run 10 ran run 9's set, not D-ML130's | Procedure error | Run record title "same procedure as run 9"; no A1, A4, A5, A6, A10 or non-Docker suite; no review pass 4 report under `.claude/reports/meridian-lab/` | F118. The confirmation run is redefined in section 6 |
| The record says the replay ran "on a fresh daemon" | Procedure error (record) | Hunt and replay both ran on daemon pid 43876, `r10-home-1` | F118. Corrected here; the analysis in section 4 uses the real setup |
| Flake check sent a detector run and `section/row` key | Procedure error, and two lab defects | Source run 844014 is M5's detector bundle (`lease_owner` `job/8e9109ffc493`). `rows` was the key `enroll-probe/enroll-gateway`. The refusal came from the seed check first | F118 (wrong source), F119 (seed), F121 (key form), F122 (detector stamp) |
| Flake check refused "no spec or seed" | Lab defect | `run.json` seed `12499590173714643239` > `long.MaxValue`; `FlakeCheckJob.cs:80` reads `TryGetValue(out long)` | F119 → R10-1 |
| Hunt diverged at op 4 with a 500 | Product defect (known) | `nodes.jsonl` 01:17:24.22, "Exception while reading from stream", `EndOfStreamException` | F114 product half → NR-1, S-L15 |
| Replay `not_reproduced` | Lab defect | Section 4 | F120 → L27c; KG-W5-13 |
| Fuzz 28 of 50 HTTP 500 | Product defect (known) | Same count as runs 8 and 9 | KG-W5-1 |
| A11, A3, M1, M4, M5, twin, chaos | Met | Run record | Carried |

## 4. Hunt and replay: why they disagree (F120)

The two runs started from different pools, and the op lists do not say so.

| Step | In-hunt replay of the shrunk case (job 98ae360f4421) | Separate replay (job d563dfccff55) |
| --- | --- | --- |
| Hunter | Same instance; the case before it cut Postgres, so `_cutSinceRestart` is true | New instance; `_cutSinceRestart` starts false (`ModelHunter.cs:70`) |
| Case start | `svc_restart` nodes 01:17:15 → 01:17:20.30; Nodes' startup background services open pooled connections (DbCommands at 01:17:20.03, twice) | No restart; Nodes had been idle since 01:17:24, and its last pooled connection had broken on the 500 |
| `Mint(60)` | 01:17:20.76 through the lab's own `NodesDbContext` (`NodesLabAdapter.cs:38-68`), so Nodes' pool is untouched | 01:17:53.72, same path |
| `PgDown()` | `fault_proxy_disable` 01:17:21.26 kills the idle pooled connections | `fault_proxy_disable` 01:17:54.13; no idle connection to kill |
| `PgUp()` | `fault_heal` 01:17:23.73 | `fault_heal` 01:17:56.66 |
| `Enroll(0)` | 01:17:24.25 takes a stale connection: EndOfStream, then a 500 | 01:17:57.13 opens a new connection: 201 in 408 ms |
| Result | `diverged`, the case reported | `not_reproduced` |

Sources: `r10-home-1/audit` lines 1283-1351 and 1355-1414; `logs/nodes.jsonl` lines 1603-1606 and 1608-1618.

- **Cause.** L27b's step 2 restarts Nodes only after a cut case. Nothing fixes the pool state at the start of a case. The outcome of `[…, PgDown, PgUp, Enroll]` therefore depends on whether Nodes has an idle connection when the cut lands, and that comes from history: a restart warms the pool, and an earlier enroll in the same case or an earlier case leaves a connection idle. The op lists of the two runs are the same; only their history differs.
- **Why the tests passed.** `ModelHunterTests.FakeLab` models staleness as one bool that every heal sets (`ModelHunterTests.cs:557`). Every cut therefore leaves a stale pool, whether or not a connection was idle. `Hunter_StalePoolAfterCut_ReportsAReproducibleCase` also replays on the same hunter (`:272`), so it shares the history of the run it checks.
- **Why it still matters after NR-1.** NR-1 turns this 500 into a retry, so this particular divergence will go away. The defect class does not. Any product defect that depends on pooled connection state can again shrink or replay inconsistently. The hunt gate (D-ML127) needs a replay that is a function of the op list.
- **Rule (D-ML136).**
  - A case begins with the Nodes restart and the warm-up when either holds: the hunter has not yet restarted Nodes (a new hunter, including every `replay=`), or the previous case cut Postgres, or the case's own ops contain a `PgDown`.
  - The warm-up mints a token and enrolls it directly against Nodes (`via` `nodes`, no Gateway pacing), and must get 201. That leaves at least one idle pooled connection.
  - Then the data reset and the case-start checkpoint run, as L27b has them. `fault_reset` moves to before the restart, so Nodes is never restarted into a cut that another job left open (D-ML119).

## 5. The seed defect (F119) and the rows key (F121)

- **Seed.** Contract C17 makes seeds `ulong`, and `LabRandom.NewSeed()` draws all 64 bits, so about half of all random seeds are 2^63 or more. Three readers parse the bundle's `run.json` seed as `long`:
  - `Results/FlakeCheckJob.cs:80` refuses the run (run 10's refusal); `:232` writes `(long)plan.Seed`, so the job result shows a negative seed.
  - `Results/BundleIngestor.cs:488-489` `ParsedSeed` returns 0. The rows and the run are stored with seed 0 (`:170`, `:238`), and `DetectFlakes(build, 0, rows)` (`:205`) then compares every high-seed run of one build as if they shared a seed. The result is false flakes in `flakes`.
  - `Triage/DraftWriter.cs:112,153` prints `- Seed: 0` into the issue draft, so a triage repro gives the wrong seed.
- **Consequence for KG-W5-16.** R9-1's closure logic is unit-tested but has never run live, and for high seeds the tool refuses before reaching it. KG-W5-16 stays live until R10-1 lands and the confirmation run's R9-1 probe passes.
- **Storage rule (D-ML135).** SQLite has no unsigned 64-bit type. `ResultsDb` keeps `long Seed` columns and stores the seed's bit pattern, `unchecked((long)seed)`. Equality queries (`ResultsDb.cs:367,417`) therefore stay correct. Every reader that shows a seed converts back with `unchecked((ulong)stored)`. Bundles and tool payloads always carry the `ulong`.
- **Rows key (F121).** `PlanAsync` matches `rows` against `entry.RowId` only (`FlakeCheckJob.cs:95-103`), but the job result lists keys (`section/row`, `:234`). Under D-ML135, `rows` accepts either form:
  - A value that contains `/` matches `entry.Key`.
  - Otherwise it matches `entry.RowId`.
  - A bare row id that occurs in more than one section of the run is refused as `[Lab.InvalidArgument] row <id> is ambiguous; name it as section/row`.

## 6. The confirmation run (amends D-ML130; D-ML137)

The confirmation run is run on the tip after R10-1, L27c, boundary review pass 4 and its fixes. It has a fresh daemon per Part B segment, as in run 9. Teardown is label-only, there is no prune, and there is no bare `publish -r`. A red is recorded and parked; it is not fixed (D-ML126).

1. Builds, then the non-Docker suite:

    ```powershell
    dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
    dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
    dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Category!=Docker"
    ```

2. Part A, serial, once each unless noted:
   - A1 `FullyQualifiedName~HunterDockerTests`
   - A4 `FullyQualifiedName~SelfCheckRecoveryTests`
   - A5 `FullyQualifiedName~SelfCheckE2ETests`
   - A6 `FullyQualifiedName~LabSelfTestE2ETests`
   - A10 `FullyQualifiedName~ToxiproxyTimeoutToxicDockerTests`
   - A11 `Seam=S-L7`, **twice**

   Each runs as `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "<filter>"`.
3. Part B segment 1, the R9-1 probe (fresh daemon):
   - Probe steps:
     1. Run `uat_run` with spec `lab-selftest.toml` and seed `12499590173714643239` (above 2^63 on purpose, so the probe also checks R10-1). Record `run_id`, `overall` and the bundle's `rows.jsonl` keys.
     2. Release the lease. Call `results_flake_check` with that `run_id`, rows `["enroll-gateway"]`, reruns `1` and no lease.
     3. Follow the job with `job_status` until it ends.
     4. Run `results_flake_check` again on the same `run_id`, with rows set to the key that step 3's result lists in `rows` (the `section/row` form) and reruns `1`.
   - Pass:
     - Both calls return a `job_id`; neither returns `[Lab.InvalidArgument]` or `[Lab.NotFound]`.
     - Both jobs end `succeeded`, with result `seed` = `12499590173714643239` (not negative), `rows` holding only the `enroll-gateway` key, and `state` `stable`.
     - Each rerun bundle's `rows.jsonl` holds `enroll-gateway` and its `depends_on` closure (`gateway-up` and what it depends on), and `enroll-gateway` is `PASS`.
     - Also record `results_query` for the source run's `seed` column: it must not be 0.
   - Red: KG-W5-16 stays live, parked.
4. Part B segment 2, the hunt consistency probe (fresh daemon):
   - Steps:
     1. Run `hunt_model_run`, model `enrollment`, seed `4242`, `budget_s` 300.
     2. If the hunt ended `diverged`, run `hunt_model_run replay=<job>/repro/repro.toml` on the same daemon.
     3. Stop the daemon. Run setup for a new daemon with a new home, copy the repro folder to the same relative path under the new home's `jobs/`, and replay again.
     4. Record each job's `state`, `first_diverging_op` and `detail`, and the audit lines from `svc_restart` to the first enroll for each replay.
   - Pass (early evidence for KG-W5-13; the gate itself stays with the campaign review's live hunt, D-ML127):
     - The hunt ends `passed`, or
     - it ends `diverged` and both replays end `reproduced` at the same `first_diverging_op`, and each replay's audit shows `svc_restart`, then a warm-up enroll answered 201, before `stack_reset_data`.
   - Red: KG-W5-13 stays live, parked, and the record gets the audit excerpt.
5. Teardown: `stack_down wipe=true` under a lease. Then `docker ps -a --filter label=meridian-lab=1` must show 0 containers, the lab network must be absent, and port 8789 must be free.

Gate MD, twin, chaos, fuzz, soak and coverage are not rerun: neither R10-1 nor L27c touches their code. The detector stamp (F122) is parked.

## 7. Packets

Shared rules for both packets (inline; `packet-coder` skips CLAUDE.md):

- **R-RESULT:** validation, IO and network failures return `Result<T>` with a `[Lab.<Code>]` message; never throw for them.
- **R-PATH:** every file path goes through `IPathValidator` / `BundleFolders` (already in place; do not add raw `Path.Combine` on user input).
- **R-USING:** `using` / `await using` on every `IDisposable`.
- **R-CPM:** no package versions in `.csproj`; no new package.
- Nullable is enabled and CI builds with warnings as errors. Do not add `!` to silence a warning that has a real null path.
- xUnit tests are named `Method_Case_Result`. Time goes through `FakeTimeProvider`, never `Task.Delay` on the wall clock.
- Never log a lease id, token ref or token value.
- Do not run `dotnet restore` with `-r`, or `publish -r`. If `packages.lock.json` changes, revert it and report it.

Checks for both packets, in order:

```powershell
dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Category!=Docker"
```

### 7.1 R10-1: seeds above 2^63, and flake check rows by key (F119, F121, D-ML135)

- **Branch / worktree / commit:** `ml/wf-r10-seed-u64` from the campaign tip, worktree `.claude/worktrees/ml-wf-r10-seed-u64`, commit subject `fix(lab): read bundle seeds as ulong; flake check rows by id or key [R10-1]`.
- **Depends on:** nothing (it can run beside L27c; the two touch disjoint files).
- **Security review:** yes, one `security-advisor` pass. It is a lab tool, and the change touches the results DB and the triage draft text.
- **Size:** small (about 20k tokens).
- **Files:**
  - `src/Tools/Dhadgar.Lab/Results/FlakeCheckJob.cs`
  - `src/Tools/Dhadgar.Lab/Results/BundleIngestor.cs`
  - `src/Tools/Dhadgar.Lab/Triage/DraftWriter.cs`
  - `src/Tools/Dhadgar.Lab/Tools/Jobs/JobTools.cs` (description string only)
  - `tests/Dhadgar.Lab.Tests/Jobs/FlakeCheckJobTests.cs`
  - `tests/Dhadgar.Lab.Tests/Results/BundleIngestorTests.cs`
  - `tests/Dhadgar.Lab.Tests/Triage/DraftWriterTests.cs`
  - `docs/analysis/meridian-lab/worknotes/ml-wf-r10-seed-u64.md` (new)
- **Steps:**
  1. Add one helper in `Results/BundleIngestor.cs` (or a new `internal static class BundleSeed` in `Results/BundleSeed.cs`, if the coder prefers), and use it in both readers:

     ```csharp
     /// <summary>Reads run.json's seed (C17: ulong). Null when absent or not a non-negative integer.</summary>
     internal static ulong? ReadSeed(JsonObject run) =>
         run["seed"] is JsonValue value && value.TryGetValue(out ulong seed) ? seed : null;

     /// <summary>The results DB column form: the seed's bit pattern (SQLite has no unsigned 64-bit type).</summary>
     internal static long ToStored(ulong seed) => unchecked((long)seed);

     /// <summary>The seed a stored column holds.</summary>
     internal static ulong FromStored(long stored) => unchecked((ulong)stored);
     ```

     If `JsonValue.TryGetValue<ulong>` is false for a JSON number that `JsonNode.Parse` holds as a `JsonElement`, fall back to `value.TryGetValue(out JsonElement element) && element.TryGetUInt64(out seed)`. The test in step 6 decides which form is needed.
  2. `FlakeCheckJob.PlanAsync` line 80: replace the `long` read with `BundleSeed.ReadSeed(run.Value!) is not { } seed`. Keep the same refusal text, and pass `seed` (ulong) into the plan at line 118 without a cast. At line 232, write `["seed"] = plan.Seed` (a `ulong` `JsonValue`).
  3. `FlakeCheckJob.PlanAsync` lines 93-104, rows by id or key. For each `rowId`:
     - If it contains `/`, match entries whose `Key` equals it.
     - Otherwise, match entries whose `RowId` equals it. If those entries come from more than one `Section`, return `[Lab.InvalidArgument] row <rowId> is ambiguous; name it as section/row`.
     - If nothing matches, return the existing `[Lab.NotFound] row <rowId> is not in the run`.

     `selected` is the union of the matches. Sections, Rows and Keys are derived from `selected` as today, so `WithSpec` still closes on row ids.
  4. `BundleIngestor`: delete `ParsedSeed`. At `:170`, `:205` and `:238`, use `BundleSeed.ToStored(BundleSeed.ReadSeed(run) ?? 0)`. The `0` default is unchanged for a bundle with no seed.
  5. `DraftWriter.cs:112,153`: print `BundleSeed.FromStored(source.Seed)` with `CultureInfo.InvariantCulture`, and carry `ulong` in the local at `:153`.
  6. `JobTools.cs:135`: append to the description: `rows takes row ids or section/row keys.`
  7. Write the worknote: what changed, the storage rule (bit pattern), and the test list.
- **Tests (each fails when its step is reverted):**
  - `FlakeCheckJobTests.PlanAsync_SeedAboveLongMax_PlansWithSameSeed`: a bundle whose `run.json` seed is `12499590173714643239` plans successfully, with `plan.Seed == 12499590173714643239UL`. It fails on step 2 reverted (the `[Lab.InvalidArgument] ... no spec or seed` refusal).
  - `FlakeCheckJobTests.RunAsync_SeedAboveLongMax_ResultSeedIsUnsigned`: with the fake invoke, the result's `seed` node reads back as `12499590173714643239UL`, and the `uat_run` arguments carry the same value. It fails on the `(long)` cast.
  - `FlakeCheckJobTests.PlanAsync_RowsAsKey_SelectsThatRow`: rows `["<section>/<row>"]` selects one key. It fails on step 3 reverted (`[Lab.NotFound]`).
  - `FlakeCheckJobTests.PlanAsync_BareRowIdInTwoSections_IsAmbiguous`: a run with row `x` in sections `a` and `b`, rows `["x"]`, gives `[Lab.InvalidArgument] ... ambiguous`.
  - `BundleIngestorTests.Ingest_SeedAboveLongMax_StoresBitPattern`: after ingest, the run and its rows have `Seed == unchecked((long)12499590173714643239UL)`, not 0. It fails on step 4 reverted.
  - `BundleIngestorTests.Ingest_TwoHighSeedRunsDifferentSeeds_NoFlake`: two bundles of one build, seeds `ulong.MaxValue - 1` and `ulong.MaxValue - 2`, one row PASS and one FAIL, record no flake. Reverted, both store 0 and a flake is recorded.
  - `DraftWriterTests.Draft_SeedAboveLongMax_PrintsUnsignedSeed`: the draft contains `- Seed: 12499590173714643239`.
- **Docs owed:** the worknote. `docs/meta/doc-update-map.md` has no row for the results DB (check it with grep; if a row exists, follow it).

### 7.2 L27c: a hunt case's start state is a function of its ops (F120, D-ML136)

- **Branch / worktree / commit:** `ml/l27c-hunt-start-state` from the campaign tip, worktree `.claude/worktrees/ml-l27c`, commit subject `fix(lab): hunt cases start on a restarted, warmed Nodes pool [L27c]`.
- **Depends on:** nothing (disjoint from R10-1).
- **Security review:** yes, one `security-advisor` pass. It is a lab tool that drives `svc_restart`, faults and token mints.
- **Size:** small to medium (about 35k tokens). Most of the work is in the fake and the tests.
- **Files:**
  - `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`
  - `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`
  - `docs/analysis/meridian-lab/worknotes/ml-l27c.md` (new)
- **Steps:**
  1. `ModelHunter.cs:69-70`: rename the field to `_needsRestart` and initialise it to `true`. Doc comment: "True until this hunter has restarted Nodes, and again after a case that cut Postgres. A new hunter, and so every replay, starts by restarting Nodes, because a pool left by an earlier job is unknown (F120)."
  2. `RunCaseAsync` lines 299-332: replace the case start with this order. All of it goes inside the existing `try`, so the existing `finally` heal still runs:

     ```csharp
     bool cuts = ops.Any(op => op.Kind == HuntOpKind.PgDown);
     try
     {
         // No restart into a cut another job left open (D-ML119): heal first.
         Result<JsonElement> reset = await CallAsync("fault_reset", new JsonObject(), ct).ConfigureAwait(false);
         if (reset.IsFailure) return Infrastructure(reset.Error);

         // F120: a case that cuts Postgres, and the first case of a hunter, start on a restarted and warmed pool.
         if (_needsRestart || cuts)
         {
             Result<JsonElement> restarted = await CallAsync("svc_restart", new JsonObject { ["service"] = Service }, ct).ConfigureAwait(false);
             if (restarted.IsFailure) return Infrastructure(restarted.Error);
             _needsRestart = false;

             Result warmed = await WarmPoolAsync(ct).ConfigureAwait(false);
             if (warmed.IsFailure) return Infrastructure(warmed.Error);
         }

         reset = await CallAsync("stack_reset_data", new JsonObject { ["databases"] = new JsonArray(Database) }, ct).ConfigureAwait(false);
         if (reset.IsFailure) return Infrastructure(reset.Error);

         // Step 1 (L27b): the case-start checkpoint, unchanged.
         ...
     ```

     The `finally` keeps `if (ranPgDown) _needsRestart = true;`.
  3. Add `WarmPoolAsync`:
     - Call `nodes_token_mint` with `{ label = "hunt-warmup", validity_minutes = HuntOp.ValidityMinutes }`. If the result has no `token_ref`, return `[Lab.Unavailable] nodes_token_mint returned no token_ref`; use the same text the op path uses, so `Hunter_MintWithoutTokenRef_IsInfrastructure` stays green.
     - Call `nodes_enroll_probe` with `{ token_ref, platform = Platform, via = "nodes", hostname = "hunt-warmup" }` through `CallAsync`. Do not use `ProbeEnrollAsync`: direct Nodes takes no Gateway pacing, and a 429 here is an error.
     - A status other than 201 returns `[Lab.Unavailable] nodes warm-up enroll answered <status>`.
     - Log nothing but the fact of the failure. Never log the token ref.
  4. The warm-up's token and node are removed by the `stack_reset_data` that follows. Do not add them to `model` or `tokenRefs`.
  5. `RunAsync` payload (`:166`): write `["run_seed"] = runSeed` (`ulong`), not `(long)runSeed`. This is F119's class: a hunt seed above 2^63 would otherwise print as negative.
  6. `FakeLab` (`ModelHunterTests.cs:378+`): replace the `_stale` bool with an idle-connection model.
     - Add fields `int _idle` and `int _staleIdle`.
     - `svc_restart`: `_idle = RestartWarm ? 1 : 0; _staleIdle = 0;`. Add `public bool RestartWarm { get; init; }` with the doc "Nodes' startup background services leave one idle pooled connection".
     - `fault_proxy_disable` on the first cut: when `StalePoolAfterCut`, `_staleIdle += _idle; _idle = 0;`.
     - `Heal()`: only `_pgDown = false`.
     - `Enroll` outside a cut: if `_staleIdle > 0`, decrement it, queue one I1 and return 500. Otherwise run today's path, and when it returns, set `_idle = Math.Max(_idle, 1)` (the connection goes back to the pool).
     - `RestartFails` becomes `RestartFailsWhileCut` (an error only while `_pgDown`, F104's product behaviour).
     - `Throttles` does not apply to a probe whose `hostname` is `hunt-warmup`.
     - Update the `StalePoolAfterCut` doc to "a cut kills the idle pooled connections; the next enroll after heal that takes one answers 500".
  7. Update the existing tests that count calls; each changes because a new hunter now restarts first:
     - `Replay_RestartInsideCut_IsNotSent`: uses `RestartFailsWhileCut`, and asserts that no `svc_restart` lies between the `fault_proxy_disable` and the `fault_heal`, and that exactly one `svc_restart` precedes the first `stack_reset_data`.
     - `Replay_RestartOutsideCut_IsSent`: asserts 2 `svc_restart` calls (case start and op), and that one of them comes after `stack_reset_data`.
     - `Hunter_CaseAfterCut_RestartsNodesFirst` becomes `Hunter_CaseStart_RestartsWhenNewCutOrAfterCut`. One hunter replays four cases and checks whether the call at each case's start is a restart:

       | Case | Ops | Starts with a restart |
       | --- | --- | --- |
       | 1 | `[Mint]` | yes (new hunter) |
       | 2 | `[Mint]` | no |
       | 3 | `[PgDown, PgUp]` | yes (cuts) |
       | 4 | `[Mint]` | yes (after a cut) |

       "Start" means the calls before that case's `stack_reset_data`.
     - `Hunter_StalePoolAfterCut_ReportsAReproducibleCase`: `FakeLab { StalePoolAfterCut = true }`. Replay the reported ops on a **new hunter over a new FakeLab** with the same flags, not on the same hunter, and assert `Diverged`.
- **Tests (each fails when its step is reverted):**
  - `Replay_NewHunter_SameResultAsWarmHunter`:
    - Lab A: `FakeLab { StalePoolAfterCut = true }`. Hunter A replays `[Mint, Enroll(0)]` and then `[Mint, PgDown, PgUp, Enroll(0)]`.
    - Lab B: a new `FakeLab` with the same flags. Hunter B replays `[Mint, PgDown, PgUp, Enroll(0)]` only.
    - Assert both cut cases diverge at index 3. Reverted (steps 1 to 3), hunter B gets a 201 and does not diverge. This is run 10's mismatch.
  - `Hunter_NewHunter_FirstCaseRestartsAndWarms`: on a new hunter, the calls before the first `stack_reset_data` are `fault_reset`, `svc_restart`, `nodes_token_mint` and `nodes_enroll_probe`, and the probe carries `via` `nodes`. It fails on step 1 or step 3 reverted.
  - `Hunter_WarmUpNot201_IsInfrastructure`: a FakeLab flag `WarmUpStatus = 503` makes the hunt fail with `nodes warm-up enroll answered 503`.
  - `Hunter_RunSeedAboveLongMax_PayloadUnsigned`: `RunAsync(12499590173714643239UL, …)` gives a `run_seed` node equal to that `ulong`. It fails on step 5 reverted.
  - The suite as a whole: every other `ModelHunterTests` test stays green with only the edits in step 7.
- **Live acceptance:** the confirmation run's A1 and the segment 2 probe (section 6). There is no separate Docker run for this packet.
- **Docs owed:** the worknote, covering the rule, the order, why the fake models idle connections, and the run 10 evidence pointer. The L27 hunt section of the lab guide is not written yet (L45); add nothing there.

### 7.3 L54b (outline only): detector bundles carry the mutant build (F122)

- **What it fixes.** `MutationRunJob` (`Mutants/MutationRunJob.cs:188-206`) starts the services on the mutant build, but the detector's `uat_run` stamps the bundle from the last `lab_build` (`Build/LastBuildReader.cs`, `Uat/Bundle/BuildStamp.cs`). That is the plain build. Two effects follow:
  - `BundleIngestor.DetectFlakes` can compare mutant and plain grades as one build.
  - `results_flake_check` on a detector run reruns on the plain build and reports the mutant's FAIL as a flake.
- **Outline.**
  - The detector bundle records `build.id` = the mutant build id, plus a `mutant` field.
  - `results_flake_check` refuses a source run that carries `mutant`, with `[Lab.InvalidArgument] a mutant detector run cannot be rerun on the current build`.
  - Ingest keys flakes by the stamped build.
  - The architect specs the steps before dispatch. It is needed before the wave 10 gate MD and LS, not before the boundary (KG-W5-19).

## 8. Decisions

| ID | Decision | Status |
| --- | --- | --- |
| D-ML134 | Run 10 classification (section 3). Run 10 is not the D-ML130 confirmation run, and its results close nothing new. Met and carried: build, A3, A11 once, gate MD, twin, chaos, teardown. The boundary stays open. | Accepted (architect) |
| D-ML135 | Seeds are `ulong` end to end (C17). The results DB stores the bit pattern in its `long` columns, and readers convert back. `results_flake_check` `rows` takes ids or `section/row` keys, and refuses an ambiguous bare id. Packet R10-1. | Accepted (architect) |
| D-ML136 | A hunt case starts with `fault_reset`, then a Nodes restart and a 201 warm-up enroll when the hunter is new, the previous case cut, or the case cuts. Then the data reset and the case-start checkpoint. Replays restart too. Packet L27c. | Accepted (architect) |
| D-ML137 | Amends D-ML129 and D-ML130. Boundary review pass 4 covers `08571257` to the tip after R10-1 and L27c. R9-1 and R10-1 join slice R1, and L27b and L27c join slice R2. Each slice still gets a `packet-reviewer` and a `security-advisor`; this also covers the missing security passes on R8, R9-1 and L27b. The confirmation run is section 6: D-ML130's set plus the R9-1 probe and the hunt consistency probe. | Accepted (architect) |
| D-ML138 | Owner. Option A, proposed: R10-1 and L27c land before the confirmation run, as follow-ups like R9-1 and L27b, not as a Docker fix wave; they add no run beyond the owed confirmation run. KG-W5-13 is then gated on L27b plus L27c, and the confirmation run's segment 2 is early evidence; the gate check stays with the campaign review (D-ML127). Option B: run the confirmation run now. R10-1 and L27c then wait for the campaign review, and the R9-1 probe uses a seed below 2^63. KG-W5-16 then stays live on high seeds. | BlockedDecision (owner); work proceeds on option A until ruled |

## 9. Known gaps (ledger changes)

| ID | Change |
| --- | --- |
| KG-W5-13 | Reworded. Live (run 10): L27b holds carried violations and keeps the original failure, but a replay can start on a different Nodes pool from the hunt (F120). Until L27c, checked by the campaign review's live hunt (D-ML127, D-ML138) |
| KG-W5-16 | Reworded. Live: R9-1 merged (`e40e0ae5`) but has not run live, and a source run with a seed of 2^63 or more is refused (F119). Until R10-1 and the confirmation run's R9-1 probe |
| KG-W5-18 | New. Live until R10-1: results DB rows and runs with a seed of 2^63 or more are stored as 0, so ingest can record false flakes and triage drafts print "Seed: 0" (F119) |
| KG-W5-19 | New. Mutation detector bundles carry the plain build id (F122). Flake checks and ingest flakes on detector runs are not meaningful. Until L54b, before the wave 10 gates |

## 10. Remaining steps to the campaign PR

| # | Step | Who | Docker |
| --- | --- | --- | --- |
| 1 | Raise D-ML138 (and the open D-ML120, D-ML133) | orchestrator | no |
| 2 | R10-1 and L27c in parallel: coder, then reviewer and security-advisor per packet; merge R10-1 then L27c | packet-coder, packet-reviewer, security-advisor, ops | no |
| 3 | Boundary review pass 4 (D-ML137), three slices with a reviewer and a security-advisor each | packet-reviewer, security-advisor | no |
| 4 | Review fix notes, if any (not a fix wave) | packet-coder | no |
| 5 | Confirmation run (section 6) | run coder | yes |
| 6 | Architect boundary-close ruling on the confirmation run | architect | no |
| 7 | L19n (S-L15 red on the tip, green in a scratch worktree with NR-1's Nodes diff); NR-1 is reviewed `ship` with security `ship` and waits only on that green | packet-coder, run coder | yes |
| 8 | NR-1 PR to `main` on the owner's yes; then `main` merged into the campaign | ops, owner | no |
| 9 | Follow-ups: L05b, L25g, then L52b, L28b, L29b, L55b, L54b (the architect specs each outline first) | architect, packet-coder | as specced |
| 10 | Waves 6 to 10, campaign review (D-ML74), L47, one PR (D-ML131) | all | yes |
