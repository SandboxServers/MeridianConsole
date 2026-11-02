# Next batch plan (after tip `143b5b02`)

Architect plan, 2026-11-02. Read at campaign tip `143b5b02` (tip review 1 closed `ship` on pass 3; fixes `90efff87` and `143b5b02`). It lists what can run now, in what order, what blocks the rest, and specs the follow-ups L29b and L54b and a split-out L35m. New decisions are D-ML153 to D-ML159 (section 6). Paths are under `src/Tools/Dhadgar.Lab/` or `tests/Dhadgar.Lab.Tests/` unless they start with `src/`, `tests/`, `tools/` or `docs/`.

## 1. Packets that can run, or whose dependencies are met

"Docker" means the packet's own checks need Docker running, so the orchestrator needs the user's yes before dispatch. "Net" means package or browser downloads.

| Packet | Dependency check | Size | Docker | Security | Branch / worktree | Spec |
| --- | --- | --- | --- | --- | --- | --- |
| L27d | wave 5 close met (D-ML142) | ~12k | no | yes | `ml/l27d-hunt-enrollagain-excuse` / `ml-l27d` | [close ruling](wave5-close-ruling.md) 6.1; `Hunt/ModelHunter.cs:539` still reads `model.PgDown && op.Kind == HuntOpKind.Enroll` |
| L27e | L27d (not merged yet) | ~35k | no | yes | `ml/l27e-hunt-seeded-sample` / `ml-l27e` | close ruling 6.2; `Check.Sample` is at `ModelHunter.cs:220` now (the spec says 216) |
| L29b | wave 5 close met | ~18k | no | no | `ml/l29b-soak-strength` / `ml-l29b` | section 7.1 here |
| L54b | wave 5 close met | ~35k | no | yes | `ml/l54b-mutant-stamp` / `ml-l54b` | section 7.2 here |
| L39 | W8 note (written today) | ~15k | no | yes (agent project) | `ml/l39-footprint` / `ml-l39` | work-packets L39 as amended by [W8 note](W8-sprint2-deltas.md) section 2 |
| L35m | L34 merged (`a4a69e6a`) | ~22k | yes (`LabDbDockerTests`) | yes | `ml/l35m-dbquery-mask` / `ml-l35m` | section 7.3 here (D-ML154) |
| L35 | L34 merged; L35m first (D-ML154) | ~40k (after the split) | yes, plus net (Playwright package, chromium) | yes | `ml/l35-playwright` / `ml-l35` | work-packets L35 minus the `LabDb` masking in file 5 |
| L37 | L30 `cdc8c73b`, L34 `a4a69e6a` merged; after L54b (D-ML159) | ~35k | no (its checks); a live `cli.toml` run needs Docker | yes | `ml/l37-cli` / `ml-l37` | work-packets L37, amended by [l37 ruling 1](l37-ruling-1.md) (D-ML162 to D-ML164) and addendum 1 (D-ML167): dev client seeded and pinned, member config routes through the Gateway's `/api/v1` paths, per-subcommand allowlist, admin key per call |
| L62 | L52, L29, L27 merged; sequenced after L27e, L29b and L52b (D-ML158) | ~40k | yes (the A/B run) | yes | `ml/l62-guided` / `ml-l62` | work-packets L62; not this batch |
| L05b | wave5-fix-C merged | ~15k (estimate) | no | yes | `ml/l05b-lease-watch` / `ml-l05b` | outline only; architect specs it next |
| L25g | wave5-fix-A merged | ~25k (estimate) | no | yes | `ml/l25g-row-evidence` / `ml-l25g` | outline only; architect specs it next |
| L28b | wave5-fix-B merged | ~20k (estimate) | no | yes | `ml/l28b-fuzz-rows` / `ml-l28b` | outline only; architect specs it next |
| L55b | wave 5 close met | ~30k (estimate) | yes (`SelfCheckRecoveryTests`) | yes | `ml/l55b-recovery-rehydrate` / `ml-l55b` | outline only; architect specs it next |
| L52b | L25g (not merged) | ~30k (estimate) | no | no | `ml/l52b-coverage-kinds` / `ml-l52b` | outline only; after L25g |

Not runnable:

| Packet | Waits on |
| --- | --- |
| L31, L32, L33 (wave 6) | L00 (parked by the owner: Windows containers off) and D-ML37/D-ML38 |
| L40 | L32 (wave 6). The W8 note it also needed now exists |
| L41-L44 (wave 8) | L40, so wave 6 |
| L57-L61 (wave 9) | L41, L42 or L43, so wave 6 |
| L38 | L35, L37 (L36 merged) |
| L56 | L35 (L53, L54, L11 merged) |
| L45-L47 (wave 10) | L44 and L55-L62, so wave 6 |

## 2. Batch and order

**Batch 1 (dispatch together, 4 packets, disjoint files, no Docker):**

| Packet | Files it owns |
| --- | --- |
| L27d | `Hunt/ModelHunter.cs`, `Hunt/FakeEnrollmentLab.cs` (or the fake named in 6.1), `Hunt/ModelHunterTests.cs` |
| L29b | `Soak/SoakJob.cs`, `Chaos/ChaosJob.cs`, `Tools/Soak/SoakTools.cs`, `Jobs/JobRunner.cs` (doc comment only), `Soak/SoakJobTests.cs`, `Chaos/ChaosJobTests.cs`, `Chaos/ChaosToolsTests.cs`, `src/Tools/Dhadgar.Lab/README.md` (soak lines) |
| L54b | `Mutants/`, `Build/LabBuilder.cs`, `Build/LastBuildReader.cs`, `Uat/Bundle/BuildStamp.cs`, `Uat/Bundle/BundleWriter.cs` (the `build` line), `Results/FlakeCheckJob.cs`, `Processes/LabProcessStart.cs`, `Docker/DockerCli.cs`, `Tools/Doctor/LabDoctorTool.cs`, the three detector tools' stamp lines, `Mcp/Areas/MutantsArea.cs`, and their tests; `src/Tools/Dhadgar.Lab/README.md` (mutation lines) |
| L39 | `src/Agents/Dhadgar.Agent.Windows/` (three files), `Seams/AgentFootprintSeamTests.cs` |

L29b and L54b both edit `src/Tools/Dhadgar.Lab/README.md`, in different sections. Whichever merges second rebases (one-line conflict at most). L54b changes the `UatRunTool.cs` stamp line and L29b changes only `SoakTools.cs`; neither edits `RowFilterCheck.cs`.

**Checks under D-ML153:** in batch 1 each packet runs its build and its **scoped** test filter (given per packet below), never the full Lab suite. After the four merge, one serial run of the full non-Docker suite (`RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`, about 9.5 min) runs on the campaign tip, dispatched as a check-only packet-coder task with no edits. A red there gets a fix packet, not a revert.

**Merge order for batch 1:** L39 → L27d → L29b → L54b (smallest first; each rebases on the tip).

**Then, serial:**

1. L27e (after L27d; same files).
2. L35m (Docker yes), then L35 (Docker yes, plus net). L35 adds `Microsoft.Playwright` to `Directory.Packages.props` (not there today; R-CPM pins 1.64.1) and restores with `--force-evaluate` as its checks say.
3. L37 after L54b (both edit `Build/LabBuilder.cs`). It can run beside L35m or L35, because their files are disjoint.
4. Next architect pass: specs for L05b, L25g, L28b, L55b. Then L52b after L25g, and L62 after L27e, L29b and L52b.
5. L38, then L56 (after L35 and L37).
6. The wave 7 boundary: wave review, then the Docker tests of waves 5-7 (D-ML74).

Wave 6 onward stays blocked until the owner answers D-ML37/D-ML38 (section 3).

## 3. Owner decisions

| ID | Blocks | Recommendation | Question for the user |
| --- | --- | --- | --- |
| D-ML37 / D-ML38 | Wave 6 (L31-L33), so L40-L47 and wave 9: the campaign PR (D-ML131) | Yes to both, when you are ready: turn on the Windows Containers and Hyper-V features and let the lab run a separate Windows `dockerd`. Then L00 (the spike) runs first. | "The rest of the lab needs Windows containers on this PC (Hyper-V plus Containers, and a second Docker engine just for the lab). May I turn those on, and when? Until then, everything after wave 7 waits, including the one campaign PR." |
| D-ML133 | nothing in the lab | Option A: a separate one-line Nodes PR to `main` that adds `UseBusOutbox()`, as Servers and Notifications already have. | "Nodes can publish an event even when its database save fails. May I open a small separate PR that adds the outbox Nodes is missing?" |
| D-ML107 | nothing (KG-W5-1 stays open until it is ruled) | A separate product PR to `main` that returns 400 instead of 500 for malformed request bodies; no known issue. | "The fuzzer finds 500 errors for malformed request bodies. May I fix that in a separate product PR?" |
| D-ML120 | nothing | No change: Nodes keeps failing fast without its database. | "Should Nodes start up and report not-ready when its database is down, instead of failing at startup? I recommend no change." |
| D-ML138 | nothing (already carried out on option A) | Ratify. | "R10-1 and L27c landed before the confirmation run, as recommended. OK to record that as approved?" |

**Owner answers (2026-11-02):** D-ML37/D-ML38 yes; D-ML133 yes (separate Nodes PR, outbox); D-ML107 yes (separate fuzz 500s PR); D-ML120 "start and report not-ready", against the recommendation above (a product change in the same Nodes PR as the outbox). D-ML138 is still ratify-by-action. The ledger is the README.

## 4. W8 note

Written now: [W8-sprint2-deltas.md](W8-sprint2-deltas.md). It was cheap because the names were already gathered, and it unblocks L39 today. Its rulings are D-ML157.

## 5. Stale ledger entries (fixed in the README in this pass)

| Where | Was | Now |
| --- | --- | --- |
| README packet row "Tip review 1" | In review, tip `fdebb5e1` | Done: pass 3 `ship` at `143b5b02` |
| README boundary text | tip `fdebb5e1`, "Tip review 1 is running" | tip `143b5b02`, review closed |
| L35 row | BlockedDependency | Ready after L35m (D-ML154) |
| L37 row | BlockedDependency | Ready, after L54b (D-ML159) |
| L39 row | BlockedDependency | Ready (W8 note) |
| L29b, L54b rows | BlockedDependency (outline) | Ready (specced here) |
| L62 row | BlockedDependency | dependencies met; sequenced after L27e, L29b and L52b (D-ML158) |
| L00 row | "Parked" (not a ledger status word) | BlockedDecision (D-ML37/D-ML38) |
| RESUME | tip `fdebb5e1`, tip review in review | rewritten |

## 6. Decisions

| ID | Decision |
| --- | --- |
| D-ML153 | A dispatch runs at most 4 packet-coders. Inside a parallel batch, packet checks and reviewer re-runs use the packet's scoped filter only. The full non-Docker Lab suite runs once on the campaign tip after the batch merges, as one serial task. Reason: parallel testhosts gave 32 spurious loopback failures. |
| D-ML154 | KG-W7-1 moves out of L35 into **L35m**. `db_query` masks by the base column (Postgres table OID and attribute number), not by result name, so an alias cannot bypass it. In `identity` and `betterauth`, a string-typed result column with no base column (an expression) is masked too. L35 drops the masking part of its file 5. |
| D-ML155 | L54b: the daemon keeps a `RunningMutantBuild` mark from a mutant's successful build until a successful restore of the plain build. Bundles stamp the mark (`build.id = mutant-<id>-<commit8>`, `build.mutant = <id>`, dirty). The flake check refuses a mutant source and refuses while a mark is set. The token strip covers every lab `Process` builder (`LabBuilder`, `DockerCli`, `LabDoctorTool`). A `---`/`+++` header without its `a/`/`b/` prefix is refused. |
| D-ML156 | L29b: a soak ends `weak` (with a `reason`) when it has 1-2 loops, fewer than `I7ResourceTrendInvariant.MinSamples` samples, or I7 `skipped`; the existing fail conditions stay. A chaos run with `workload_loops == 0` fails with a `reason`. `soak_run` loads the spec and runs `RowFilterCheck` before it starts the job. |
| D-ML157 | The W8 note's rulings: L39 aliases the Windows literals only (no Agent.Core change); L40 adds hub events 4101 and 4102; L41's allowlist key is `AgentIngress:ClientCertificateMode`, and only the lab TLS CA goes to a member's Root store; L42 registers `ICertificateStore` and `IProcessManager` itself and replaces fakes after `AddAgentCore()`. |
| D-ML158 | L62 waits for L27e (it changes the hunter's draw), L29b (chaos outcome) and L52b (until then the coverage kinds guided mode weights are `not_collected`, so the A/B would measure nothing). |
| D-ML159 | L37 runs after L54b; both edit `Build/LabBuilder.cs`. |

## 7. Packet specs

Rules every Lab packet carries (inline them into the packet brief):

- `Result<T>` (from `Dhadgar.Shared.Results`) for validation, IO and process failures; errors read `[Lab.<Code>] <text>`. Never `Result<T>.Success(null)`.
- Every file path through `IPathValidator.ValidatePath(path, [root])`.
- Child processes only through `ArgumentList`; `using` on every `Process` and `IDisposable`.
- Never print, log or write the lab token or a lease id.
- No package versions in `.csproj`; versions live in `Directory.Packages.props`.
- Nullable is on; build with `-p:TreatWarningsAsErrors=true`.
- xUnit with the existing test project's style (`[Fact]`, `Assert.*`, fakes over mocks; the trait names `RequiresDocker` and `Category` exist, `Category!=Docker` excludes nothing).
- Never `git worktree prune`; never `publish -r` or `restore -r`.

### 7.1 L29b: soak and chaos strength

- **Branch / worktree / commit:** `ml/l29b-soak-strength` / `ml-l29b` / `fix(lab): weak soak and failing zero-loop chaos, soak rows checked up front`
- **Security review:** no. **Docker:** no.
- **Decisions:** D-ML112, D-ML141 (KG-W5-2, KG-W5-20), D-ML156.

**Files**

1. `Soak/SoakJob.cs`.
   - Add `internal const int MinLoops = 3;`.
   - Replace line 129 with this grade:

     ```csharp
     (string overall, string? reason) = Grade(loops, samples, sampleErrors, i7, i7Failed);
     ```

     ```csharp
     /// <summary>D-ML112: fail on a sampler error, a failed I7 or no loop; weak on too few loops or samples; else pass.</summary>
     internal static (string Overall, string? Reason) Grade(int loops, int samples, int sampleErrors, string i7, bool i7Failed)
     {
         if (sampleErrors > 0 || i7Failed || loops == 0)
         {
             return ("fail", loops == 0 ? "no workload loop succeeded" : null);
         }

         if (loops < MinLoops)
         {
             return ("weak", "fewer than 3 workload loops");
         }

         if (samples < I7ResourceTrendInvariant.MinSamples || i7 == "skipped")
         {
             return ("weak", "too few resource samples for I7");
         }

         return ("pass", null);
     }
     ```

   - Add `reason` to the anonymous result object; it is null on pass.
2. `Chaos/ChaosJob.cs` `Outcome()`.
   - Change the grade to `_workloadLoops > 0 && _violations == 0 && _checkErrors == 0 && !_timelineFailed ? "pass" : "fail"`.
   - Add `["reason"] = _workloadLoops == 0 ? "no workload loop succeeded" : null`.
   - `_workloadLoops` counts successful loops only (line 313), so "every loop errored" is the same case.
   - Update the `<summary>` to say so.
3. `Tools/Soak/SoakTools.cs` `SoakRunTool.ExecuteAsync`.
   - After the duration check and before `_store.IsSoaking`: refuse a file over `UatSpecPaths.MaxSpecBytes` with `[Lab.InvalidArgument] the spec is too large`.
   - Read the text and catch `IOException` and `UnauthorizedAccessException` as `[Lab.Unavailable] the spec could not be read`.
   - `SpecLoadOutcome loaded = SpecLoader.LoadText(relative, text);`. A null `loaded.Spec` gives `[Lab.InvalidArgument] the spec does not load: <n> problem(s)`.
   - When `args.Rows is { Count: > 0 }`, call `RowFilterCheck.Check(loaded.Spec, args.Rows)` and return its error unchanged.
   - Mirror `Tools/Uat/UatRunTool.cs:145-189`. The method becomes `async`; keep the `Task.FromResult` returns as plain `return`.
4. `Jobs/JobRunner.cs:18`: the doc comment lists `weak` as a grade a soak can report.
5. `src/Tools/Dhadgar.Lab/README.md`, soak section: `weak` and its two reasons; chaos fails with no workload loop; soak `rows` checked at start.

**Tests**

| Test | File | Why it fails when reverted |
| --- | --- | --- |
| `Grade_TwoLoops_IsWeak`; `Grade_ThreeLoopsTwoSamples_IsWeak`; `Grade_I7Skipped_IsWeak`; `Grade_ZeroLoops_IsFail`; `Grade_Enough_IsPass` | `Soak/SoakJobTests.cs` | the old line passes 1-2 loops and too few samples |
| `RunAsync_OneLoop_EndsWeakWithReason`: the existing fake invoker and a `FakeTimeProvider` run long enough for one loop; assert `overall == "weak"` and a non-null `reason` | `Soak/SoakJobTests.cs` | the old job returns `pass` |
| `Outcome_ZeroWorkloadLoops_Fails`; `Outcome_AllLoopsErrored_Fails` | `Chaos/ChaosJobTests.cs` | the old `Outcome()` passes with no violation |
| `SoakRun_RowNotInSpec_RefusedBeforeStart` (`[Lab.NotFound]`) and `SoakRun_RowWithoutDependency_Refused` (`[Lab.InvalidArgument] ... without its dependency`), using a temp repo root with a two-row spec where row 2 `depends_on` row 1; assert no job started | `Chaos/ChaosToolsTests.cs` (it already builds `SoakRunTool`) | the old tool never loads the spec |

**Checks** (`<W>` = the worktree)

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~SoakJobTests|FullyQualifiedName~ChaosJobTests|FullyQualifiedName~ChaosToolsTests|FullyQualifiedName~JobRunnerTests"
```

**Docs owed:** file 5. The ledger closes KG-W5-2 and KG-W5-20 on merge (the orchestrator).

### 7.2 L54b: mutant build stamp, flake refusal, token strip, header prefix

- **Branch / worktree / commit:** `ml/l54b-mutant-stamp` / `ml-l54b` / `fix(lab): stamp detector bundles with the mutant build and strip the lab token from every child`
- **Security review:** yes (child process environment, patch scope). **Docker:** no.
- **Decisions:** D-ML141 (F122, H3, H4), D-ML155.
- **Contract change (C9 `run.json` `build`):** add an optional `mutant` (string). It is present only on a bundle produced while a mutant build served the services. Its `id` is then the mutant build id and `dirty` is true.

**Files**

1. New `Mutants/RunningMutantBuild.cs`:

   ```csharp
   /// <summary>The mutant build serving the lab's services, set by mutation_run from build to restore (D-ML155).</summary>
   public sealed record MutantMark(string BuildId, string MutantId, string Commit);

   public sealed class RunningMutantBuild
   {
       private readonly Lock _gate = new();
       private MutantMark? _mark;

       public MutantMark? Current { get { lock (_gate) { return _mark; } } }

       public void Set(MutantMark mark) { ArgumentNullException.ThrowIfNull(mark); lock (_gate) { _mark = mark; } }

       public void Clear() { lock (_gate) { _mark = null; } }
   }
   ```

   Use `object` for the lock if the project's target lacks `System.Threading.Lock` (check other files first).
2. `Mcp/Areas/MutantsArea.cs`: `services.AddSingleton<RunningMutantBuild>()`, the same way `InvariantsArea.cs:99` registers its singletons.
3. `Mutants/MutationRunJob.cs`.
   - The constructor gains `RunningMutantBuild running` (after `redactor`; null throws).
   - In `RunMutantAsync`, after the stale check and before the `svc_start` loop: `_running.Set(new MutantMark(built.Value.BuildId, spec.Id, built.Value.Commit));`.
   - In `RunAsync`, after `RestorePlainAsync` returns success: `_running.Clear();`.
   - A failed restore keeps the mark: the services may still be mutant code, so later bundles say so.
4. `Tools/Mutants/MutationRunTool.cs:90`: pass `_services.GetRequiredService<RunningMutantBuild>()`. `PlainBuildId()` (line 151) keeps the plain reader.
5. `Uat/Bundle/BuildStamp.cs`.
   - The record becomes `BuildStamp(string Id, string Commit, bool Dirty, string Checkout, string? Mutant = null)`.
   - `FromRunJson` reads `mutant` with `Text(build, "mutant")`.
   - `LedgerLine` appends ` mutant <id>` when `Mutant` is non-null.
6. `Build/LastBuildReader.cs`: add an overload. Existing callers keep the two-argument form.

   ```csharp
   public static BuildStamp Read(LabPaths paths, IPathValidator validator, RunningMutantBuild? running) =>
       running?.Current is { } mark
           ? new BuildStamp(mark.BuildId, mark.Commit, Dirty: true, Checkout: string.Empty, Mutant: mark.MutantId)
           : Read(paths, validator);
   ```

7. Detector call sites use the overload with `_services.GetService<RunningMutantBuild>()`:
   - `Tools/Uat/UatRunTool.cs:211`
   - `Tools/History/HistoryRunTool.cs:404`
   - `Tools/Twins/TwinRunTool.cs:157`

   `HuntModelTool.cs:101` and `MutationRunTool.cs:153` stay as they are.
8. `Uat/Bundle/BundleWriter.cs:607`: write `mutant` only when non-null. A plain bundle's `run.json` is byte-for-byte unchanged:

   ```csharp
   var build = new JsonObject { ["id"] = _stamp.Id, ["commit"] = _stamp.Commit, ["dirty"] = _stamp.Dirty, ["checkout"] = _stamp.Checkout };
   if (_stamp.Mutant is { } mutant) { build["mutant"] = mutant; }
   ```

   Then use `build` in the anonymous object.
9. `Results/FlakeCheckJob.cs` `PlanAsync`.
   - Add an optional last parameter `MutantMark? running = null`.
   - After `ReadRunAsync` (line 82), when `BuildStamp.FromRunJson(run.Value!)` has a non-null `Mutant`, or its `Id` starts with `mutant-`, return `[Lab.InvalidArgument] a mutant detector run cannot be rerun on the current build`.
   - When `running` is non-null, return `[Lab.Conflict] a mutant build is serving the lab; rerun after mutation_run restores it`.
   - The flake-check tool passes `_services.GetService<RunningMutantBuild>()?.Current`. Find the tool with `Grep "FlakeCheckJob.PlanAsync"`.
10. H3, `Processes/LabProcessStart.cs`.
    - Extract the strip loop (lines 40-43) into `internal static void StripToken(ProcessStartInfo info)`.
    - Call it in `Create`, in `Build/LabBuilder.cs` `RunAsync` (after the `ArgumentList` loop), in `Docker/DockerCli.cs` (after line 78), and in `Tools/Doctor/LabDoctorTool.cs` `VersionCheckAsync` (after its `ArgumentList` loop).
    - The daemon already removes the token from its own environment at start (`LabCli.TakeTokenFromEnvironment`). This is the second guard, for a token set later or a test host.
11. H4, `Mutants/MutantBuilder.cs`.
    - Replace `HeaderPaths` (line 385) with `TryReadHeaderPath(string line, out string? path)`.
    - A non-header line returns true with `path = null`; so does `/dev/null`.
    - A `--- ` line must start `--- a/` and a `+++ ` line must start `+++ b/`, else it returns false.
    - In `PatchTouchesOnly`, false gives `[Lab.InvalidArgument] change.patch has a header the scope check cannot read`.
    - A removed hunk line that begins `-- ` also reads as a `--- ` header and is refused. That fails closed, as before.
12. `src/Tools/Dhadgar.Lab/README.md`: mutation section (bundle `build.mutant`; no flake check on detector runs or during a mutation run); process section ("every child process", naming the three builders).

**Tests**

| Test | File | Why it fails when reverted |
| --- | --- | --- |
| `RunAsync_DetectorCall_SeesMarkSet_ThenCleared`: the fake invoker records `running.Current` when the detector tool is called (expects the mutant id and `mutant-<id>-<commit8>`) and after `RunAsync` (expects null) | `Mutants/MutationRunJobTests.cs` (fake at line 440) | without file 3 the mark is never set |
| `RunAsync_RestoreFails_MarkKept` | same | a clear on failure would hide mutant code |
| `Read_WithMark_ReturnsMutantStamp`; `Read_NoMark_ReturnsLatest` | `Build/LastBuildReaderTests.cs` | the overload does not exist |
| `RunJson_MutantStamp_WritesMutant`; `RunJson_PlainStamp_HasNoMutantKey` | `Uat/Bundle/` (the `BundleWriter` test class that asserts `build`; Grep `"checkout"`) | the writer drops the field |
| `PlanAsync_MutantSource_Refused`; `PlanAsync_WhileMarkSet_Refused` | `Jobs/FlakeCheckJobTests.cs` | the plan accepts a detector run |
| `DetectFlakes_MutantBuildRun_NotComparedWithPlainRun`: two ingests of one spec and seed, one plain and one `mutant-…`, different grades; assert no flake | `Results/BundleIngestorTests.cs` | pins that a mutant id isolates ingest flakes (KG-W5-19) |
| `RunAsync_ChildDoesNotInheritTheDaemonToken`: copy `SelfCheckRunnerGuardTests.RunProcessAsync_ChildDoesNotInheritTheDaemonToken` (set the variable, run `cmd /c echo %MERIDIAN_LAB_TOKEN%` through `LabBuilder.RunAsync`, assert the output lacks the secret, restore in `finally`); `[Trait("Category","Windows")]` only if that test has it | `Build/LabBuilderTests.cs` | `LabBuilder` inherits the variable |
| `StripToken_RemovesExplicitTokenKey` (set `info.Environment["MERIDIAN_LAB_TOKEN"]`, any case) | `Processes/` (the `LabProcessStart` test class) | pins the helper |
| `PatchTouchesOnly_MinusHeaderWithoutPrefix_Refused` (`--- x.cs` with `x.cs` listed); `PatchTouchesOnly_PlusHeaderWithoutPrefix_Refused`; `PatchTouchesOnly_DiffGitWithoutPrefixes_Refused` (pin; `diff --git x.cs x.cs`) | `Mutants/MutantBuilderTests.cs` | the old reader accepts an unprefixed header |

Update every `new MutationRunJob(` in tests (`MutationRunJobTests.cs:383`) for the new parameter.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~MutationRunJobTests|FullyQualifiedName~MutantBuilderTests|FullyQualifiedName~LabBuilderTests|FullyQualifiedName~LastBuildReaderTests|FullyQualifiedName~FlakeCheckJobTests|FullyQualifiedName~BundleIngestorTests|FullyQualifiedName~FlakeOnIngestTests|FullyQualifiedName~UatRunToolIngestTests|FullyQualifiedName~BundleWriter|FullyQualifiedName~LabProcessStart|FullyQualifiedName~ToolRegistration"
git -C <W> status --porcelain   # only src/Tools/, tests/Dhadgar.Lab.Tests/ paths
```

**Docs owed:** file 12. The ledger closes KG-W5-19 for new bundles on merge. Gate MD is re-judged at the wave 10 gate run.

### 7.3 L35m: `db_query` masks token columns by base column

Amended by [l35m ruling 1](l35m-ruling-1.md) (D-ML160, D-ML161): pass-through-type expression rule, name rule, Nodes entries, live-name step in `AuthStackDockerTests`.

- **Branch / worktree / commit:** `ml/l35m-dbquery-mask` / `ml-l35m` / `fix(lab): mask token columns in db_query by base column`
- **Security review:** yes. **Docker:** yes (the Docker test).
- **Decisions:** KG-W7-1, D-ML154.

**Files**

1. `Data/ColumnMask.cs` (new, internal static).

   ```csharp
   internal static class ColumnMask
   {
       public const string Masked = "[masked]";

       // (database, table, column), Postgres names, case-sensitive.
       internal static readonly FrozenSet<(string Db, string Table, string Column)> Sensitive = new[]
       {
           ("betterauth", "session", "token"), ("betterauth", "account", "accessToken"), ("betterauth", "account", "refreshToken"),
           ("betterauth", "account", "idToken"), ("betterauth", "account", "password"), ("betterauth", "verification", "value"),
           ("identity", "refresh_tokens", "TokenHash"), ("identity", "OpenIddictTokens", "Payload"),
           ("identity", "OpenIddictTokens", "ReferenceId"), ("identity", "OpenIddictApplications", "ClientSecret"),
       }.ToFrozenSet();

       internal static readonly FrozenSet<string> ExpressionMaskedDbs = new[] { "betterauth", "identity" }.ToFrozenSet();

       // origin: (table, column) of the base column, or null for an expression.
       public static bool ShouldMask(string database, (string Table, string Column)? origin, string dataTypeName) => ...;
   }
   ```

   - A base column in `Sensitive` is masked whatever its type.
   - An expression (null origin) in `ExpressionMaskedDbs` is masked when `dataTypeName` is `text`, `character varying`, `character`, `bytea`, `json` or `jsonb`.
   - Everything else passes through.
   - The coder checks the BetterAuth table and column names against a live `information_schema.columns` read in the Docker test (better-auth default schema; `src/Dhadgar.Identity/Data/Migrations` for Identity) and corrects the set if one differs.
2. `Data/LabDb.cs`.
   - In `ReadRowsAsync`, before reading rows, take `reader.GetColumnSchema()`. Keep `TableOID` and `ColumnAttributeNumber` per ordinal, plus `DataTypeName`.
   - Buffer the raw values as today.
   - After the reader is disposed, and still inside the READ ONLY transaction on the same connection, resolve the non-zero OIDs with one bound query:

     ```sql
     SELECT c.oid, a.attnum, c.relname, a.attname
     FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
     WHERE c.oid = ANY(@oids)
     ```

   - Replace each masked value with `ColumnMask.Masked`. A masked null stays null.
   - Column names are kept.
   - Npgsql allows one open reader per connection, so the catalog query must run after the reader is disposed.
3. Tests:
   - `Data/ColumnMaskTests.cs` (no Docker): `Sensitive_BaseColumn_Masked`, `Alias_SameBaseColumn_Masked` (origin is what decides, not the name), `Expression_InIdentity_TextMasked`, `Expression_InNodes_NotMasked`, `OtherColumn_NotMasked`.
   - `Data/LabDbDockerTests.cs` `DbQuery_TokenColumns_Masked` (`RequiresDocker`): create `betterauth`-named and `identity`-named test tables with the sensitive columns in the test database. Insert a known secret. Then run `SELECT token FROM session`, `SELECT token AS t FROM session`, `SELECT s.token FROM session s JOIN ...` and `SELECT token || '' AS x FROM session`, and assert none returns the secret. Follow the existing Docker test's database setup; if it cannot name a database `betterauth`, add a test seam on `ShouldMask`'s database argument and say so in the report.
4. `src/Tools/Dhadgar.Lab/README.md`, `db_query` section: the masked columns and the expression rule.

**Tests and why they fail when reverted:** `Alias_SameBaseColumn_Masked` and the Docker alias and concat queries fail under name-based or no masking.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ColumnMaskTests|FullyQualifiedName~LabDb"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabDbDockerTests"   # Docker running
```

**Docs owed:** file 4. KG-W7-1 closes on merge with the Docker test green.

### 7.4 L39 amendments

- Use the [W8 note](W8-sprint2-deltas.md) section 2 file list: `Program.cs:26,31`, `Windows/WindowsCertificateStore.cs:43`, `Installation/AgentRegistry.cs:10,13`. No Agent.Core edit.
- Under D-ML153, its Lab check is `--filter "FullyQualifiedName~AgentFootprintSeamTests"`, not the full suite.
- `tests/Dhadgar.Agent.Windows.Tests` runs in full (a separate testhost, not loopback-heavy).
