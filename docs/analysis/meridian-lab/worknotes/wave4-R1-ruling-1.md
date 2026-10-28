# Wave 4 R1 ruling, pass 1 (L16f, L19, L20, L21)

Inputs: `.claude/reports/meridian-lab/wave4-review-1.md` (N1-N8, N-D1, verdict `decision`) and `.claude/reports/meridian-lab/wave4-security-1.md` (N1-N4 for this slice; its L22-L24 items are ruled in `wave4-R2-ruling-1.md`). Reviewed range `d6ca843..145c421d`; line numbers below are at `5daa8590` and every item also names the member, so a shifted line is still findable.

Verdicts: **L16f ship**, **L19 fix**, **L20 fix**, **L21 fix** (the decision is ruled here, so it becomes fix work).

## Findings

| ID | Finding | Evidence |
| --- | --- | --- |
| F75 | Why S-L6 fails: the invariant checkpoints run before the things they check exist. The start checkpoint (`UatRunner.cs:159-162`) runs before row `stack-up`. `LabCredentialRefs` is empty then, so I2 and I3 fail with `[Lab.NotFound] the stack has no credentials` and I5 fails on `queues` (`InvariantWiring.cs:99-103`); Toxiproxy is not up either. The `stack-up` row's own checkpoint then runs before Nodes has migrated (migrations run at `nodes-up`), so I2 and I3 get SQLSTATE 42P01. `stack-up` grades UNVERIFIED, every later row is SKIPPED by `depends_on`, and the end checkpoint fails the same way. C11 says a source that is down is an `InvariantCheckError`, and S-L6 requires zero errors, so the two rules contradict each other on the first row. The exact count of 4 was not reproduced. | review N1, N-D1; `I2OutboxInvariant.cs:77-81`, `I3DbChecksInvariant.cs:63-67`, `I5OrphanInvariant.cs:101-121`, `LabSelfTestE2ETests.cs:176-180` |
| F76 | `fault_corrupt_file` cannot be replayed. The tool passes no seed (`FaultKindTools.cs:159-161`), and `CorruptFileFault.WriteGarbage` (:297) seeds its stream with `fault:<fault_id>`, where the fault id is a fresh random id on every call. With a fixed seed the bytes still change from run to run, so the R-SEED line in L20 is itself the defect. | review N2; `FaultBook.BeginAsync` `NewFaultId()` (:188) |
| F77 | `disk_fill` passes the guard on the infra containers (postgres, rabbitmq, redis, toxiproxy), whose `/tmp` is on the overlay root, so `df` reports the free space of the Docker VM disk. That disk also holds the dev stack's volumes, so the fill can push the dev databases into ENOSPC. Only the service containers have a bounded `--tmpfs /tmp` (`LabServices.cs:170`). | security N1; `DiskFillFault.cs:70-89` |

## Decisions

| ID | Decision | Status | Refs |
| --- | --- | --- | --- |
| D-ML80 | N-D1 option (a). C11 gains a third outcome, **not applicable**, recorded as an `InvariantSkip` that is never an error and never changes a grade. I2 and I3 skip a database while the stack has no credentials or while the service has not created its schema (`__EFMigrationsHistory` missing). I5 skips the `queues` and `proxies` sources while the stack has no credentials, and still checks containers and backups. Any other failure stays a check error, including 42P01 on a migrated database. S-L6 keeps "zero errors" and also asserts that skips occur only at `(run-start)` and `stack-up`. Options (b), a per-invariant `needs` field, and (c), relaxing S-L6, are rejected: (b) needs every spec author to know the dependencies, and (c) hides real outages. | Accepted | F75; review N1, N-D1; C11 amended |
| D-ML81 | Inside a UAT run the runner derives the `fault_corrupt_file` seed: `LabRandom(run.Seed, "fault:<section>/<row>/<fault spec id>").NextUInt64() >> 11` (53 bits, so it survives JSON), unless the spec sets `seed`. `WriteGarbage` uses the constant stream `corrupt_file:garbage`, so the same seed always writes the same bytes. Outside a run the tool keeps `LabRandom.NewSeed()`. | Accepted | F76; review N2; R-SEED (L20, L22) amended |
| D-ML82 | `disk_fill` requires a bounded tmpfs: `/tmp` must report fstype `tmpfs` with size ≤ 262 144 KiB, or the fault is refused with `[Lab.BlastRadius] /tmp is not a bounded tmpfs` before any write. The fill is capped at the tmpfs size. The infra containers do not get `--tmpfs`, so `disk_fill` is not available on them by design. | Accepted | F77; security N1 |

## Rulings on each note

| Note | Ruling | Fix items |
| --- | --- | --- |
| review N1 (L21) | Fix, reshaped by D-ML80. The reviewer's "42P01 while the service is not running" test is replaced by a schema probe; registry state does not show whether migrations have finished. | 1-8 |
| review N-D1 (L21) | Ruled: D-ML80, option (a) | 1-8 |
| review N2 (L20) | Fix (D-ML81) | 10-12 |
| review N3 (L20) | Fix as written | 16 |
| review N4 (L20) | Fix: a tool-level test that also checks the file bytes, plus a guard test | 17 |
| review N5 (L19) | Fix: a failed heal gets a 60 s retry deadline. The sweeper is unchanged and picks the entry up again when the deadline passes. | 18 |
| review N6 (L19) | Fix: heal the fault now, then return the failure. Returning Success would hide a journal that can no longer be written. | 19 |
| review N7 (L19) | Fix: `finally` always calls `DownAsync(wipe: true)`. No fake test: the test is Docker-only, so a build is the check. | 20 |
| review N8 (L19) | Fix: the `File` row allows nothing; the enum value stays because C10 names it | 21 |
| security N1 (L20) | Fix (D-ML82) | 13 |
| security N2 (L21, supervisor) | Fix: the supervisor masks the PEM body per process. This slice owns it because I4 and `Ingest` belong to L21/L07; `wave4-R2-ruling-1.md` does not cover it. | 22 |
| security N3 (L21) | Fix as written | 9 |
| security N4 (L19/L20) | Fix: validate the ref and check the label before each heal mutation. No such container still means healed. | 14-15 |
| review "not atomic" note (L16f) | Owner decision OD-1 below; does not block | - |
| review "not reviewed in this pass" | Owner decision OD-2 below; does not block | - |

## Rules to inline (packet-coder skips CLAUDE.md)

- `Result<T>` for every validation, IO, network or Docker failure; never throw for them. `Result<T>.Success(null)` throws, so never write it; use the non-generic `Result.Success()` where there is no value.
- `PathValidator` for every file path (CorruptFileFault already does; keep it).
- Docker calls are separate `ArgumentList` items through `IDockerCli`; never build a command string.
- `CryptographicOperations.ZeroMemory` for key bytes (CorruptFileFault buffers; keep it).
- `using` on every `IDisposable` and `Process`.
- No package versions in `.csproj` (Central Package Management); this fix adds no package.
- Nullable is on and warnings are errors: build with `-p:TreatWarningsAsErrors=true`.
- xUnit: `[Fact]`/`[Theory]`; `[SkippableFact]` + `Skip.IfNot` for OS-dependent supervisor tests (copy `SupervisorTests.Supervisor_EscapedVaultValue_IsRedactedAfterDecode`).
- Every new `Regex` has `RegexOptions.CultureInvariant` and a 1 s timeout.
- Edit only the files named below. Do not edit `docs/analysis/**`; the architect has already made the contract edits.

## Fix list

Part A covers items 1-12, 9 and 22 (invariants, seed and redaction). Part B covers items 13-21 (faults). One coder applies both parts in order. If the orchestrator splits the work, part A and part B each go to their own coder with their own commit; they share no file except `README.md` of the lab tool.

### 1. Contract types: `src/Tools/Dhadgar.Lab/Invariants/InvariantTypes.cs`

Replace the `InvariantReport` record (:42) and add these types. Keep `IInvariant` and the other records unchanged.

```csharp
/// <summary>A source an invariant did not check because it does not exist yet (C11 "not applicable"). Never an error.</summary>
public sealed record InvariantSkip(string InvariantId, string Source, string Reason, string? Row = null);

public sealed record InvariantReport(
    IReadOnlyList<InvariantViolation> Violations,
    IReadOnlyList<InvariantCheckError> Errors,
    IReadOnlyList<InvariantSkip>? Skipped = null);

/// <summary>The result of a check that can skip sources.</summary>
public sealed record InvariantOutcome(IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantSkip> Skipped);

/// <summary>An invariant that reports skipped sources; the monitor calls this instead of <see cref="IInvariant.CheckAsync"/>.</summary>
public interface ISkippableInvariant : IInvariant
{
    Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct);
}

/// <summary>Whether a lab database can be checked yet.</summary>
public enum DbApplicability
{
    Applicable,
    StackNotUp,
    SchemaNotReady,
}

/// <summary>Answers whether <paramref name="database"/> can be checked. A Failure is a check error.</summary>
public delegate Task<Result<DbApplicability>> InvariantDbGate(string database, CancellationToken ct);
```

Verify: build. Every existing `new InvariantReport(v, e)` still compiles.

### 2. `src/Tools/Dhadgar.Lab/Invariants/I2OutboxInvariant.cs`

- The class implements `ISkippableInvariant`.
- The constructor (:47) gains a final parameter `InvariantDbGate? gate = null`, stored in `_gate`.
- Move the body of `CheckAsync` (:58-106) into `CheckWithSkipsAsync`. `CheckAsync` becomes:

```csharp
public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
{
    Result<InvariantOutcome> outcome = await CheckWithSkipsAsync(window, ct).ConfigureAwait(false);
    return outcome.IsSuccess
        ? Result<IReadOnlyList<InvariantViolation>>.Success(outcome.Value.Violations)
        : Result<IReadOnlyList<InvariantViolation>>.Failure(outcome.Error);
}
```

- In `CheckWithSkipsAsync`, keep `var skipped = new List<InvariantSkip>();` and `var gated = new Dictionary<string, DbApplicability>(StringComparer.Ordinal);`. At the top of the `foreach (OutboxTarget target ...)` body, before the probes:

```csharp
if (_gate is not null)
{
    if (!gated.TryGetValue(target.Database, out DbApplicability state))
    {
        Result<DbApplicability> answer = await _gate(target.Database, ct).ConfigureAwait(false);
        if (answer.IsFailure)
        {
            return Result<InvariantOutcome>.Failure($"{target.Service} applicability: {answer.Error}");
        }

        state = answer.Value;
        gated[target.Database] = state;
        if (state != DbApplicability.Applicable)
        {
            skipped.Add(new InvariantSkip(Id, "db:" + target.Database, SkipReason(state)));
        }
    }

    if (state != DbApplicability.Applicable)
    {
        continue;
    }
}
```

- Add `internal static string SkipReason(DbApplicability state) => state == DbApplicability.StackNotUp ? "stack not up (no lab credentials)" : "schema not created by its service yet";`. I3 reuses it.
- The probe failure return (:78-81) becomes `Result<InvariantOutcome>.Failure(...)` with the same text. The final return is `Result<InvariantOutcome>.Success(new InvariantOutcome(violations, skipped))`.

Verify: item 7 tests.

### 3. `src/Tools/Dhadgar.Lab/Invariants/I3DbChecksInvariant.cs`

Same pattern as item 2:

- Implement `ISkippableInvariant`, add `InvariantDbGate? gate = null` as the final constructor parameter (:38), and make `CheckAsync` delegate as in item 2.
- In the `foreach (DbCheck check ...)` loop (:60), before `_query`, evaluate the gate once per distinct `check.Database` with the same dictionary.
- Not applicable: add one skip per database (`"db:" + check.Database`, `I2OutboxInvariant.SkipReason(state)`), then `continue`.
- A gate Failure sets `firstError ??= $"{check.Service}/{check.CheckId}: {answer.Error}"` and continues, as a query failure does today.
- `LoadChecks` failure and the `firstError` return keep their meaning, wrapped in `Result<InvariantOutcome>`.

### 4. `src/Tools/Dhadgar.Lab/Invariants/I5OrphanInvariant.cs`

- Implement `ISkippableInvariant`.
- Add a final constructor parameter `Func<bool>? stackCreated = null`, stored as `_stackCreated` (null means "always created").
- Move the body into `CheckWithSkipsAsync`; `CheckAsync` delegates as in item 2.
- Containers (:86-99) run as today.
- Then `bool stack = _stackCreated?.Invoke() ?? true;`.
- When `!stack`:
  - do not call `_queues` or `_proxies`;
  - add `new InvariantSkip(Id, "queues", "stack not up (no lab credentials)")` and the same skip for `"proxies"`;
  - skip both violation loops.
- When `stack`, :101-130 run unchanged. A failure there stays `Failed(...)`.
- Backups (:132-139) always run.
- `Failed` returns `Result<InvariantOutcome>.Failure($"{source}: {error}")`.

### 5. Wiring: `src/Tools/Dhadgar.Lab/Invariants/InvariantWiring.cs` and `src/Tools/Dhadgar.Lab/Mcp/Areas/InvariantsArea.cs`

In `InvariantWiring` (after `SqlQuery`, :165-166), add:

```csharp
/// <summary>The schema probe: true once EF has created its history table in the service's database.</summary>
internal const string MigratedProbeSql = "select to_regclass('public.\"__EFMigrationsHistory\"') is not null as migrated";

/// <summary>
/// The I2/I3 applicability gate (C11, D-ML80): StackNotUp with no lab_reader credential, SchemaNotReady before the service
/// migrates, otherwise Applicable. A probe failure is returned as a failure (a check error), never as a skip.
/// </summary>
public static InvariantDbGate DbGate(LabDb db, LabCredentialRefs refs) =>
    async (database, ct) =>
    {
        if (refs.Get(LabCredentialRefs.LabReader) is null)
        {
            return Result<DbApplicability>.Success(DbApplicability.StackNotUp);
        }

        Result<DbQueryResult> probe = await db.QueryAsync(database, MigratedProbeSql, NoParameters, 1, ct).ConfigureAwait(false);
        if (probe.IsFailure)
        {
            return Result<DbApplicability>.Failure(probe.Error);
        }

        bool migrated = probe.Value.Rows.Count == 1
            && probe.Value.Rows[0].TryGetValue("migrated", out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
        return Result<DbApplicability>.Success(migrated ? DbApplicability.Applicable : DbApplicability.SchemaNotReady);
    };

/// <summary>True once stack_up has created the lab credentials (I5's queues and proxies exist only then).</summary>
public static Func<bool> StackCreated(LabCredentialRefs refs) => () => refs.Get(LabCredentialRefs.LabReader) is not null;
```

Add `using Dhadgar.Lab.Stack;` if it is missing.

In `InvariantsArea` (:44-51), pass `InvariantWiring.DbGate(sp.GetRequiredService<LabDb>(), sp.GetRequiredService<LabCredentialRefs>())` as the new last argument of `I2OutboxInvariant` and of `I3DbChecksInvariant`. In the I5 registration (:64-70), pass `InvariantWiring.StackCreated(sp.GetRequiredService<LabCredentialRefs>())` as the last argument.

### 6. Monitor, runner and outputs

- **Monitor.** `InvariantMonitor.cs`:
  - `CheckOutcome` (:71) becomes `(string InvariantId, IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantSkip> Skipped, string? Error)`; every existing construction passes `Array.Empty<InvariantSkip>()`.
  - In `RunOneAsync` (:48-69), when `invariant is ISkippableInvariant skippable`, call `skippable.CheckWithSkipsAsync`. On success, return its violations and `outcome.Value.Skipped.Select(s => s with { Row = window.Row }).ToArray()`; on failure, return the error as today. The timeout and exception catches stay.
  - `CheckpointAsync` (:26-46) gathers skips from successful outcomes into `skipped` and returns `new InvariantReport(violations, errors, skipped)`.
- **Row result.** `UatRunner.cs:37`: `UatRowResult` gains a final parameter `[property: JsonIgnore] IReadOnlyList<InvariantSkip>? Skipped = null`.
- **Row executor.** `RowExecutor.cs`:
  - the normal return (:255) adds `Skipped: invariants.Skipped`;
  - `Blocked` (:560) gains a final parameter `IReadOnlyList<InvariantSkip>? skipped = null`, passed as `Skipped: skipped`;
  - the blocked path (:219) passes `blockedReport.Skipped`.
- **Run aggregate.** `UatRunner.cs:193-195`: the aggregate report gets a third argument:

```csharp
(startReport.Skipped ?? []).Select(s => s with { Row = "(run-start)" })
    .Concat(rows.SelectMany(r => r.Skipped ?? []))
    .Concat((endReport.Skipped ?? []).Select(s => s with { Row = "(run-end)" }))
    .ToArray()
```

  `SyntheticRows` and `CheckpointAsync` (:241-251) are unchanged; with no monitor they still return `new InvariantReport([], [])`.
- **Bundle.** `Uat/Bundle/BundleWriter.cs:483`: add `skipped = invariants.Skipped ?? []` to the anonymous object, after `errors`.
- **On-demand tool.** `Tools/Invariants/InvariantsCheckTool.cs:59-66`: add `skipped = report.Skipped ?? []` after `errors`.

### 7. Tests for items 1-6 (add only; the files exist)

- **`tests/Dhadgar.Lab.Tests/Invariants/I2OutboxInvariantTests.cs`:**
  - `I2_StackNotUp_SkipsWithoutQuerying`: the gate returns `StackNotUp`; a query delegate that counts calls. Expect 0 calls, Success, no violations, one skip with `Source == "db:nodes"`.
  - `I2_SchemaNotReady_Skips`: the gate returns `SchemaNotReady`; expect one skip with `Reason` starting "schema not created".
  - `I2_Applicable_42P01_IsStillAFailure`: the gate returns `Applicable` and the query fails with `[Lab.InvalidArgument] Postgres rejected the statement (SQLSTATE 42P01)`. Expect Failure containing `42P01`. This test fails if the code swallows every 42P01.
  - `I2_GateFails_IsFailure`: the gate returns Failure("[Lab.Unavailable] down"); expect Failure.
- **`I3DbChecksInvariantTests.cs`:** `I3_StackNotUp_SkipsEachDatabaseOnce`. Two checks on `nodes`, the gate returns `StackNotUp`; expect exactly one skip and 0 query calls.
- **`I5OrphanInvariantTests.cs`:** `I5_StackNotCreated_SkipsQueuesAndProxies_StillReportsOrphanContainer`. `stackCreated: () => false`; the queue and proxy delegates throw if called; one labelled container is not in the registry. Expect 1 `orphan-container` violation and the skips `queues` and `proxies`.
- **New `InvariantWiringGateTests.cs`:** `DbGate_NoCredentials_IsStackNotUp`. Use `new LabDb(new LabConnectionStrings(new SecretVault(), new LabCredentialRefs()))` and a fresh `LabCredentialRefs` passed to `DbGate`; expect `StackNotUp` with no network call (no stack is needed).
- **`InvariantMonitorTests.cs`:** `Monitor_SkippableInvariant_SkipsAreStampedWithTheRow_AndAreNotErrors`. A local `ISkippableInvariant` returns one skip; window Row `"r1"`. Expect `Errors` empty and `Skipped` single with `Row == "r1"`.
- **`tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs`:**
  - Add `private sealed class SkippingInvariant(string id) : ISkippableInvariant`. Its `CheckWithSkipsAsync` returns Success with no violations and one skip `(id, "db:nodes", "stack not up (no lab credentials)")`; its `CheckAsync` returns `Clean()`.
  - Test `Skips_AreLabelledByCheckpoint_AndNeverGrade`: run `InvariantSpec` row `strict` with `new InvariantMonitor([new SkippingInvariant("I2")])`.
  - Expect the row grade is not `Unverified`; `run.Value.Invariants!.Errors` is empty; `Skipped!.Select(s => s.Row)` equals `["(run-start)", "strict", "(run-end)"]`; no row id starts with `invariants/`.

Verify: `dotnet test ... --filter "FullyQualifiedName~Invariants|FullyQualifiedName~UatRunnerTests"`.

### 8. S-L6 assertion: `tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs` `AssertRunPasses` (:176-180)

Keep the three existing asserts. After them add:

```csharp
// D-ML80: sources that do not exist yet are skipped, only before Nodes has migrated. A skip at run end means a gate that always skips.
JsonElement skipped = invariantsJson.RootElement.GetProperty("skipped");
foreach (JsonElement skip in skipped.EnumerateArray())
{
    string row = skip.GetProperty("row").GetString() ?? string.Empty;
    Assert.True(row is "(run-start)" or "stack-up", "unexpected invariant skip: " + skip.GetRawText());
}

Assert.Contains(skipped.EnumerateArray(), s => s.GetProperty("row").GetString() == "(run-start)");
```

Verify: build only. The orchestrator runs S-L6 (Docker) at the wave boundary (D-ML74).

### 9. Security N3: `src/Tools/Dhadgar.Lab/Invariants/I1UnhandledInvariant.cs:75`

- Change the detail to `Truncate(service + ": " + SensitiveText.Redact(line.Message, out _))`.
- Add `using Dhadgar.Lab.Secrets;`.
- Test in `I1UnhandledInvariantTests.cs`: `UnhandledException_Detail_IsSensitiveTextRedacted`. Copy `UnhandledException_Line_Counts` (:73) with the message `Unhandled exception while validating cookie=abc123xyz`. Expect the detail to contain `cookie=[redacted]` and not `abc123xyz`. If `SensitiveText` writes a different marker for `cookie=`, assert the marker it writes (read `SensitiveText.Redact`), and still assert `DoesNotContain("abc123xyz")`.

### 10. Seed pass-through: `src/Tools/Dhadgar.Lab/Tools/Faults/FaultKindTools.cs`

- `FaultCorruptFileArgs` (:26) becomes `(string File, string Mode, string? Member = null, int? HealAfterS = null, ulong? Seed = null)`.
- The call at :161 becomes `new CorruptFileArgs(args.File, args.Mode, args.Member, args.Seed)`.
- `SampleArgs` (:155-156) is unchanged.

### 11. Run-derived seed: `src/Tools/Dhadgar.Lab/Uat/Eval/UatVars.cs` and `src/Tools/Dhadgar.Lab/Uat/RowExecutor.cs`

- `UatVars`: after `RunId` (:25), add `public string RowId => _values["row_id"];` and `public string SectionId => _values["section_id"];`.
- `RowExecutor`: add the field `private static readonly HashSet<string> SeededFaultTools = new(StringComparer.Ordinal) { "fault_corrupt_file" };` and this helper:

```csharp
/// <summary>R-SEED (D-ML81): a seeded fault tool gets a seed derived from the run seed unless the spec sets one.</summary>
private static JsonElement EffectiveArgs(UatFault fault, UatVars vars, RunContext run)
{
    if (!SeededFaultTools.Contains(fault.Tool) || fault.Args.ValueKind != JsonValueKind.Object || fault.Args.TryGetProperty("seed", out _))
    {
        return fault.Args;
    }

    JsonObject args = JsonNode.Parse(fault.Args.GetRawText())!.AsObject();
    ulong seed = new Dhadgar.Lab.Random.LabRandom(run.Seed, "fault:" + vars.SectionId + "/" + vars.RowId + "/" + fault.Id).NextUInt64() >> 11;
    args["seed"] = seed;
    return JsonSerializer.SerializeToElement(args);
}
```

- In `InjectAsync` (:299-317): add `JsonElement args = EffectiveArgs(fault, vars, run);` as the first line. Use `args` in `Synthetic(fault.Tool, args)` (:301) and in `new UatRowFault(fault.Id, fault.Tool, args, ...)` (:315), so the row JSON and repro show the seed.
- `Dhadgar.Lab.Random` stays fully qualified to avoid a clash with `System.Random`.

### 12. Stable garbage stream and its tests

- `src/Tools/Dhadgar.Lab/Faults/Kinds/CorruptFileFault.cs`:
  - add `private const string GarbageStream = "corrupt_file:garbage";`;
  - :297 becomes `new LabRandom(heal.Seed, GarbageStream).NextBytes(buffer);`;
  - `WriteGarbage` no longer reads `faultId`. Remove the parameter and update its caller.
- `tests/Dhadgar.Lab.Tests/Faults/CorruptFileFaultTests.cs` (add):
  - `Garbage_SameSeedDifferentFaultIds_WriteTheSameBytes`: prepare `f1` with `seed: 7`, inject, read the bytes, heal; then prepare `f2` with `seed: 7` and inject. Expect equal bytes. This fails with the old `fault:<id>` stream.
  - `Garbage_DifferentSeeds_WriteDifferentBytes`: seeds 7 and 8, same procedure. Expect the bytes differ.
- `tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs`:
  - `ScriptedArgs` (:624) becomes `(string? Target = null, string? FaultId = null, string? File = null, string? Mode = null, ulong? Seed = null)`.
  - Add the spec constant `SeededFaultSpec`: section `runner-seeded`, one row `garbled` with `depends_on = []`, a step `tool = "faults_active"`, and a fault with `id = "garble"`, `inject = { tool = "fault_corrupt_file", args = { file = "nodes-ca-key", mode = "garbage" } }`, `start = "before_steps"` and `heal = "after_steps"`.
  - Test `SeededFault_SeedIsDerivedFromTheRunSeed`: for run seeds `1UL`, `1UL` and `2UL`, use a new `Harness` each time with `fault_corrupt_file` (returns `new { fault_id = "f-garble-1" }`), `fault_heal` and `ProbeTool()`. Record `Assert.Single(corrupt.Calls).Seed`. Expect all three non-null, the two `1UL` seeds equal and the `2UL` seed different.
  - Test `SeededFault_SpecSeed_IsKept`: the same spec with `args = { file = "nodes-ca-key", mode = "garbage", seed = 5 }`; expect `Seed == 5UL`.

Verify: `--filter "FullyQualifiedName~CorruptFileFaultTests|FullyQualifiedName~UatRunnerTests"`.

### 13. Security N1 / D-ML82: `src/Tools/Dhadgar.Lab/Faults/Kinds/DiskFillFault.cs` `PrepareAsync` (:70-89)

Add `internal const int MaxTmpfsKib = 262144;`. Replace the df call and the parse with:

```csharp
Result<DockerOutput> df = await ContainerSupport.RunAsync(
    _docker, ct, "exec", containerId.Value, "df", "-k", "--output=fstype,size,avail", DeclaredMount).ConfigureAwait(false);
if (df.IsFailure || df.Value.ExitCode != 0)
{
    return Result<JsonElement>.Failure("[Lab.Docker] free space of /tmp could not be read");
}

// A header line, then "<fstype> <size KiB> <avail KiB>".
string last = df.Value.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
string[] columns = last.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
if (columns.Length != 3
    || !long.TryParse(columns[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)
    || !int.TryParse(columns[2], NumberStyles.None, CultureInfo.InvariantCulture, out int avail))
{
    return Result<JsonElement>.Failure("[Lab.Docker] free space output is unreadable");
}

// D-ML82: only a bounded tmpfs can be filled; an overlay /tmp is the Docker VM disk that the dev stack shares.
if (!string.Equals(columns[0], "tmpfs", StringComparison.Ordinal) || size > MaxTmpfsKib)
{
    return Result<JsonElement>.Failure("[Lab.BlastRadius] /tmp is not a bounded tmpfs");
}

int available = (int)Math.Min(avail, size);
```

The `available <= leave` check (:84-87) and the heal data (:89) stay as they are.

In `tests/Dhadgar.Lab.Tests/Faults/DiskFillFaultTests.cs`:

- Change the fakes as follows:

| Line | From | To |
| --- | --- | --- |
| :20 | `"Avail\n5000\n"` | `"Type 1K-blocks Avail\ntmpfs 65536 5000\n"` |
| :39 | `"Avail\n5000\n"` | `"Type 1K-blocks Avail\ntmpfs 65536 5000\n"` |
| :52 | `"Avail\n5000\n"` | `"Type 1K-blocks Avail\ntmpfs 65536 5000\n"` |
| :65 | `"Avail\n50\n"` | `"Type 1K-blocks Avail\ntmpfs 65536 50\n"` |
| :77 | `"Avail\nlots\n"` | `"Type 1K-blocks Avail\ntmpfs 65536 lots\n"` |

- Change the :30 assertion to `new[] { "exec", ContainerId, "df", "-k", "--output=fstype,size,avail", "/tmp" }`.
- Add `PrepareAsync_OverlayTmp_IsBlastRadius`: `"Type 1K-blocks Avail\noverlay 61202244 58000000\n"`. Expect a Failure equal to `[Lab.BlastRadius] /tmp is not a bounded tmpfs` and exactly one docker call (the df).
- Add `PrepareAsync_TmpfsTooLarge_IsBlastRadius`: `tmpfs 1048576 1000000`. Expect the same refusal.
- Add `PrepareAsync_BoundedTmpfs_Succeeds`: `tmpfs 65536 65000`. Expect `available_kib == 65000`.

### 14. Security N4, part 1: `src/Tools/Dhadgar.Lab/Docker/IDockerInspector.cs` and `src/Tools/Dhadgar.Lab/Faults/Kinds/ContainerSupport.cs`

`DockerInspector` (:55-56): the regex becomes `new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))`. `$` accepts a trailing newline; `\z` does not. Add the helper below, and have the check at :65 call it.

```csharp
internal static bool IsValidContainerRef(string value)
{
    try
    {
        return ContainerRef.IsMatch(value);
    }
    catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
    {
        return false;
    }
}
```

In `ContainerSupport`, add:

```csharp
/// <summary>The label inspect format (the lab label must read "1").</summary>
internal const string LabelFormat = "{{index .Config.Labels \"meridian-lab\"}}";

/// <summary>
/// Security N4: before a heal mutates a container named by the journal, the reference must be valid and the container must
/// carry the lab label. A container that no longer exists is healed (nothing to undo).
/// </summary>
public static async Task<Result> CheckHealRefAsync(IDockerCli docker, string containerId, CancellationToken ct)
{
    if (!DockerInspector.IsValidContainerRef(containerId))
    {
        return Result.Failure("[Lab.BlastRadius] journal container reference is not valid");
    }

    Result<DockerOutput> run = await RunAsync(docker, ct, "inspect", "--format", LabelFormat, containerId).ConfigureAwait(false);
    if (run.IsFailure)
    {
        return Result.Failure(run.Error);
    }

    if (run.Value.ExitCode != 0)
    {
        return SaysAlreadyDone(run.Value, "No such")
            ? Result.Success()
            : Result.Failure("[Lab.Docker] container could not be inspected");
    }

    return string.Equals(run.Value.StdOut.Trim(), "1", StringComparison.Ordinal)
        ? Result.Success()
        : Result.Failure("[Lab.BlastRadius] journal container is not labelled as lab-owned");
}
```

### 15. Security N4, part 2: the three heal bodies and their tests

- In `KillFault.HealAsync` (:109), `PauseFault.HealAsync` (:64) and `DiskFillFault.HealAsync` (:136), insert before the mutating call:

```csharp
Result owned = await ContainerSupport.CheckHealRefAsync(_docker, heal.Value.ContainerId, ct).ConfigureAwait(false);
if (owned.IsFailure)
{
    return owned;
}
```

  Do not add it to `StartAsync`, `UnpauseAsync` or `RemoveFillAsync`; the reconcile paths reach those after the guard.
- In `tests/Dhadgar.Lab.Tests/Faults/KillFaultTests.cs`, add next to `Ok` (:213):

```csharp
internal static FakeDocker HealDocker(Func<IReadOnlyList<string>, DockerOutput> respond) =>
    new(args => args[0] == "inspect" && args.Count == 4 && args[2].Contains(".Config.Labels", StringComparison.Ordinal) ? Ok("1") : respond(args));
```

- In the nine heal tests, change `new FakeDocker(` to `HealDocker(`:
  - `KillFaultTests` :88, :100, :111;
  - `PauseFaultTests` :54, :66, :77;
  - `DiskFillFaultTests` :124, :136, :147.
  Where a test asserts `docker.Calls.Single()`, change it to `docker.Calls.Last()` and add `Assert.Equal(2, docker.Calls.Count);`.
- New tests in `KillFaultTests`:
  - `HealAsync_InvalidJournalRef_MakesNoDockerCall`: an entry whose heal data `ContainerId` is `"--help"`. Expect a Failure starting `[Lab.BlastRadius]` and `docker.Calls` empty.
  - `HealAsync_UnlabelledContainer_IsRefusedBeforeStart`: a plain `FakeDocker` whose label inspect returns `Ok("<no value>")`. Expect a Failure containing `not labelled` and no call with `args[0] == "start"`.
  - `HealAsync_ContainerGone_IsSuccess`: the inspect returns `new DockerOutput(1, "", "Error: No such object: x")` and the start returns `new DockerOutput(1, "", "Error: No such container: x")`. Expect Success.
- New test in `PauseFaultTests`: `HealAsync_UnlabelledContainer_IsRefusedBeforeUnpause`.
- New test in `DiskFillFaultTests`: `HealAsync_UnlabelledContainer_IsRefusedBeforeRm`.

### 16. Review N3: `src/Tools/Dhadgar.Lab/Faults/Kinds/CorruptFileFault.cs` `ReconcileAsync` (:183-237)

- Declare `string? firstError = null;` before the folder loop, and `bool allRestored = true;` inside it, before the file loop.
- `live.IsFailure` (:215-218): set `allRestored = false; firstError ??= live.Error;` then `continue`.
- After `RestoreFromBackup`, a failure sets `allRestored = false; firstError ??= restore.Error;`.
- Wrap the folder delete (:229-233) in `if (allRestored && Directory.Exists(...))`.
- After the loops: `return firstError is null ? Result<int>.Success(restored) : Result<int>.Failure(firstError);`.
- Update the comment at :229: "only when every file was restored; otherwise the backup stays for the next reconcile".
- Test in `CorruptFileFaultTests`, `ReconcileAsync_RestoreFails_KeepsTheBackup_ThenRetrySucceeds`:
  - prepare and inject `f9` with `truncate`, then `WriteText(BackupOf("f9"), "tampered")`;
  - reconcile with an empty journal: expect a Failure starting `[Lab.Integrity]` and the folder still present;
  - `File.WriteAllBytes(BackupOf("f9"), Original)`, then reconcile again: expect Success, `ReadBytes(CaKey) == Original`, and the folder gone.

### 17. Review N4: blast-radius tests

- `tests/Dhadgar.Lab.Tests/Faults/BlastRadiusTests.cs`, add `CorruptFile_NodesNotRegistered_IsRefusedAndNoFileIsTouched`:
  - start the daemon as the other tests do;
  - `LabPaths paths = daemon.Services.GetRequiredService<LabPaths>();`;
  - write the bytes `"lab-test-ca-key"` to `Path.Combine(paths.State, "nodes-ca", "ca.key")`, creating the directory;
  - invoke `fault_corrupt_file` with `{ file = "nodes-ca-key", mode = "truncate" }`;
  - expect the error to equal `[Lab.BlastRadius] target is not registered`;
  - expect the file bytes unchanged and `Directory.Exists(paths.FaultBackups)` to be false, or the folder to be empty.
- `tests/Dhadgar.Lab.Tests/Faults/FaultTargetGuardTests.cs`, add `ResolveAsync_NodesRegisteredButUnlabelled_CorruptFileIsRefused`:
  - register `c:nodes` (Container, `nodes`, `ContainerId`);
  - use the inspection `LabInspection() with { Name = "/meridian-lab-nodes", Labels = new Dictionary<string, string>() }`;
  - `ResolveAsync("c:nodes", "corrupt_file", ...)` must give a Failure containing `not labelled`.

### 18. Review N5: `src/Tools/Dhadgar.Lab/Faults/FaultBook.cs`

- After `SweepInterval` (:99), add:

```csharp
/// <summary>A failed heal is retried by the sweeper after this long, not every sweep (review N5).</summary>
public static readonly TimeSpan HealFailedRetryInterval = TimeSpan.FromSeconds(60);
```

- In `HealLockedAsync`, the HealFailed replace (:516) becomes `Replace(current with { State = FaultState.HealFailed, HealError = outcome.Error, Deadline = now + HealFailedRetryInterval });`.
- `SweepOnceAsync` is unchanged.
- Test in `FaultBookTests`, `FaultBook_HealFailed_RetriedOnlyAfterTheRetryInterval`:
  - `Book()`, `StartAsync`, begin with `heal_after_s` 120, set `_kind.HealError`;
  - `_time.Advance(121 s)`, then `WaitUntilAsync(() => _kind.HealCalls == 2)`;
  - five times: `_time.Advance(1 s)` and `await Task.Delay(50)`; expect `HealCalls` still 2;
  - `_time.Advance(60 s)`, then `WaitUntilAsync(() => _kind.HealCalls >= 4)`;
  - `StopAsync`.

### 19. Review N6: `FaultBook.BeginAsync` (:217-224)

The activated-failure branch becomes:

```csharp
if (activated.IsFailure)
{
    // The target is faulted but the journal cannot say so: heal now rather than leave a live fault behind a failed result.
    await HealLockedAsync(faultId, "journal_failed", ct).ConfigureAwait(false);
    return Result<ActiveFault>.Failure(activated.Error);
}
```

In `FaultBookTests`:

- Rename and rewrite `FaultBook_CrashAfterInjectBeforeActive_RecoveryHeals` (:149-166) as `FaultBook_ActiveWriteFails_HealsNowAndReturnsFailure`. With `Book(new ThrowOnActiveJournal(_journal))`, expect `begun.IsFailure`, `_kind.HealCalls == 1`, `_kind.Injected` empty, `book.ListActive()` empty and the journal empty.
- Add `FaultBook_PreparedEntryWithLiveFault_RecoveryHeals`. This keeps the crash-recovery coverage the old test had. Write `Entry("f-live", FaultState.Prepared)` to `_journal`, then `_kind.Injected.Add("f-live")`, `Book()` and `StartAsync`. Expect `_kind.Injected` empty, `Healed` single `f-live` and the journal empty.

### 20. Review N7: `tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs`

Delete `bool stackUp = false;` (:93) and `stackUp = true;` (:99). In `finally` (:233-236), replace `if (stackUp) { ... }` with an unconditional `_ = await stack.DownAsync(wipe: true, CancellationToken.None);`. Verify: build. The test is Docker-only and the orchestrator runs it.

### 21. Review N8: `src/Tools/Dhadgar.Lab/Faults/FaultTargetGuard.cs:39`

- The row becomes `[LabTargetKind.File] = new HashSet<string>(StringComparer.Ordinal), // no fault kind targets a file id; corrupt_file goes through c:nodes (review N8)`.
- Test in `FaultTargetGuardTests`, `ResolveAsync_FileTarget_NoKindAllowed`: register `f:nodes-ca-key` with `LabTargetKind.File`, `nodes-ca-key`, `null`. `ResolveAsync("f:nodes-ca-key", "corrupt_file", ...)` must give a Failure equal to `[Lab.BlastRadius] fault kind is not allowed on this target`.

### 22. Security N2: `src/Tools/Dhadgar.Lab/Processes/LabProcessSupervisor.cs` `Ingest` (:561-591)

`ManagedProcess` (:595-629) gains `public int PemLines { get; set; }` and `public Lock PemGate { get; } = new();`. Add a static field:

```csharp
private static readonly Regex PemBegin = new("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
private const int MaxPemLines = 200;
```

In `Ingest`, after `_observer.Observe(...)` (the observer still gets the raw line, so I4 is unchanged), replace `string redacted = _redactor.Redact(rawLine);` with:

```csharp
string stored = rawLine;
lock (entry.PemGate)
{
    if (entry.PemLines > 0)
    {
        // Inside a multi-line private key: the body never reaches the ring, the file or a bundle (security N2).
        stored = "***pem";
        bool end = rawLine.Contains("-----END", StringComparison.Ordinal);
        entry.PemLines = end || entry.PemLines >= MaxPemLines ? 0 : entry.PemLines + 1;
    }
    else
    {
        try
        {
            if (PemBegin.IsMatch(rawLine) && !rawLine.Contains("-----END", StringComparison.Ordinal))
            {
                entry.PemLines = 1;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            stored = "***pem";
        }
    }
}

string redacted = _redactor.Redact(stored);
```

Add `using System.Text.RegularExpressions;` if it is missing. `Redactor` still masks the BEGIN line itself.

In `tests/Dhadgar.Lab.TestChild/Program.cs`:

- Add `bool pem = false;` and the switch case `case "--pem": pem = true; break;`.
- Before `Console.Out.Flush()` (:66), when `pem`, print `-----BEGIN PRIVATE KEY-----`, `MIIEvQIBADANBgkqhkiG9w0BAQEFAASC`, `-----END PRIVATE KEY-----` and `after-pem`, each with `Console.Out.WriteLine`.
- Update the options comment at :5.

In `tests/Dhadgar.Lab.Tests/Processes/SupervisorTests.cs`, add `[SkippableFact] Supervisor_MultiLinePem_BodyIsNotStored`:

- copy the shape of `Supervisor_EscapedVaultValue_IsRedactedAfterDecode` (:136-163), with the args `"--pem", "--sleep-ms", "60000"`;
- wait for at least 4 ring lines, then stop;
- expect neither the ring messages nor any `Message` in the log file to contain `MIIEvQ`, and the ring to contain `after-pem`.

### 23. Lab README: `src/Tools/Dhadgar.Lab/README.md`

- In the fault table row `disk_fill` (:121), change the Target cell to `Linux service container with a bounded tmpfs /tmp (≤ 256 MiB; others refused, D-ML82)`.
- In the `corrupt_file` row (:122), change "`garbage` (seeded)" to "`garbage` (seeded; inside a run the seed comes from the run seed, D-ML81)".
- Add a bullet after :110 ("Lifetimes"): `A heal that fails stays HealFailed and is retried by the sweeper every 60 s; before a kill, pause or disk_fill heal touches a container named by the journal, the lab checks the reference and the meridian-lab label.`
- In "## Invariants", after the paragraph at :232, add:

  `A source that does not exist yet is skipped, not an error: I2 and I3 skip a database while the stack has no credentials or before the service has migrated, and I5 skips queues and proxies until stack_up. Skips are listed under skipped in invariants.json and invariants_check, labelled with the row or (run-start) / (run-end), and never change a grade (D-ML80).`

## Checks (in order; non-Docker only, per D-ML74)

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.TestChild\Dhadgar.Lab.TestChild.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~Invariants|FullyQualifiedName~Faults|FullyQualifiedName~UatRunnerTests|FullyQualifiedName~SupervisorTests"
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests"
```

The orchestrator runs these at the wave boundary, not the coder: S-L6 `LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten` and S-L7 `ToxiproxySeamTests`, both Docker. If S-L6 still reports errors, send `invariants.json` from that run to an architect ruling. Do not widen the skip rules in a fix pass.

## Packet facts

| Field | Value |
| --- | --- |
| Packet | wave4-R1 fix, pass 1 (L19, L20, L21) |
| Worktree / branch | `.claude/worktrees/meridian-lab` / `campaign/meridian-lab` |
| Commit subject | `fix(lab): apply wave 4 R1 ruling, pass 1 [L19-L21]` (if split: `... part A [L20-L21]` and `... part B [L19-L20]`) |
| Security review | Yes: fault journal, blast radius, file corruption, supervisor redaction, DB credentials in the gate |
| Size | About 45k tokens. Over the 40k budget; split at part A / part B if the coder's context runs short. |
| Order | After the wave4-R2 fix pass has landed (both touch `RowExecutor.cs`, `UatRunnerTests.cs` and `ToxiproxySeamTests.cs`). Re-read each file before editing. |
| Docs owed | Lab README (item 23). The architect has already amended C11, the L20 and L22 R-SEED lines and the L21 R-RESULT line. |

## Owner decisions (non-blocking)

| ID | Question | Default until ruled |
| --- | --- | --- |
| OD-1 | `BundleAttestor` writes row JSON, `ledger.md` and `run.json` as separate files, one `WriteRedacted` call each (`BundleAttestor.cs:163`, `WriteRedacted` :293-301). An IO failure partway leaves them disagreeing, and a retried attest is refused. Make the write atomic: write a temp folder, then swap it in. | Accept for now; the architect adds a small follow-up packet to wave 5. It is not a security issue: the scan runs before the first write. |
| OD-2 | Review 1 did not read DbFault, KillFault, PauseFault, DiskFillFault, the proxy faults, `KindSupport`/`ContainerSupport`, the ToolPolicy/R-REG rows, the `MERIDIAN_LAB_TOKEN` env and lease-id log checks, or `SensitiveText`/`SecretForms`/`SafeIds`. The security review did cover most of them and found N1 and N4. | A second R1 reviewer pass over that list runs alongside the review of this fix. |

## Notes for the orchestrator

- Packet-table statuses are yours to set: L16f ship; L19, L20 and L21 fix.
- This ruling owns security N2 (multi-line PEM): it changes the supervisor that L21's I4 sits beside, and it is not in `wave4-R2-ruling-1.md`.
- Numbering used: F75-F77 and D-ML80-D-ML82; D-ML77-D-ML79 and F72-F74 are left for the R2 ruling.
