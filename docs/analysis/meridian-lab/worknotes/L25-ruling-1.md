# L25 ruling 1: results DI, known-issue honesty, ingest scope

Architect ruling, 2026-10-28. Input: `.claude/reports/meridian-lab/L25-coder.md` and `worknotes/L25.md` (both in `ml-l25`), the L25 block and the wave 5 plan in `work-packets.md`, D-ML86 and D-ML89.

Worktree: `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l25`, branch `ml/l25-tools`, tip `bbb59400`. That tip already holds L16g. Paths below are under `src/Tools/Dhadgar.Lab/` unless named. Line numbers are at `bbb59400`.

Ledger rows made with this ruling: F87-F89 and D-ML91-D-ML94. No owner decision is needed, and nothing is BlockedDecision.

## Findings

- **F87. L25 has no home for the results DB, and its bundle says "none known" without checking.**
  - `ResultsDb` and `BundleIngestor` are registered nowhere. `Mcp/Areas/ResultsArea.cs` does not exist, and `AddResultsArea` is only declared (`Mcp/LabMcpServer.cs:36,81`).
  - `uat_run` never ingests (`Tools/Uat/UatRunTool.cs:212-234`). File 5 and S-L6 both expect it to.
  - `uat_attest` cannot re-ingest (`Tools/Uat/UatReportTools.cs:189-197`).
  - The writer (`Uat/Bundle/BundleWriter.cs:387-389,552`), the attestor (`Uat/Bundle/BundleAttestor.cs:263-268`) and the runner (`Uat/UatRunner.cs:211-213`) all pass an empty match table to `RunSummary`. So run.json's `known_failures: []` reads as "none known", but no matcher ran.
  - The row JSON never carries the spec row's `known_issue` claim (`BundleWriter.cs:480-497`). The ingest reads the claim only when the key is present (`Results/BundleIngestor.cs:411`).
- **F88. The shipped KI entries cannot match honestly.**
  - KI-F67's `^inv:I1\|nodes\|` has only a `services` scope, so it excuses any Nodes I1 violation in any row. I1 check ids are only `unhandled` and `unexplained-<status>` (`Invariants/I1UnhandledInvariant.cs:76,96`).
  - A `services` scope never matches `clause` or `hist` signatures, because their `Service` is null (`Results/KnownIssues.cs:269`; `Results/SignatureBuilder.cs:170,178`). So the two claiming catalog rows (`catalog/nodes.toml` `nodes-pg-down-enroll`, `nodes-enroll-malformed`) can never resolve.
  - KI-F69's 4xx pattern is unreachable from row evidence: `BuildAll` emits only 5xx `http` signatures.
  - Under F67 the pg row is BLOCKED by a step error, and its clauses are written `Unverified` (`Uat/RowExecutor.cs:179-180,233`; `BundleWriter.cs:227`). An issue keyed only on those clause texts would excuse any blocking cause, such as a mint error or a fault-inject error.
  - When a step expects an error and gets success instead, no signature records it (`RowExecutor.cs:172-176`). So a 200 instead of a 400 would look like KI-F69.
- **F89. Step 3 does not ingest every kind, and nothing says so.**
  - `exc` and `http`: the ingest feeds `[]` for both (`BundleIngestor.cs:131-132`). Row JSON has no log window or Hosting.Diagnostics lines, and steps carry no method or route.
  - `hist`: history bundles have a different layout (`row.json` at the root, `kind = "history"`, `BundleWriter.cs:279-336`). They ingest as zero rows.
  - Pairs: `pair` is always null until L53 (`BundleWriter.cs:563`).
  - Faults: the row fault JSON (`BundleWriter.cs:454-462`) lacks `target_id` and `kind`, which the `faults` table requires (`Results/ResultsDb.cs:34`).
  - Standalone violations are already covered. The run-start and run-end checkpoints become synthetic rows `invariants/<id>` with their violations (`UatRunner.cs:194-199,250-260`), so they ingest as `inv` signatures. But the `RunSummary.cs:6-7` doc comment describes a `violation` section that nothing writes.

## Rulings

### D-ML91. One `ResultsService` singleton owns the DB; re-ingest stays in L25

- New `Results/ResultsService.cs`, a sealed singleton and `IDisposable`. It is the only owner of the `ResultsDb` and the only place a `BundleIngestor` is built.
  - `ResultsDb` and `BundleIngestor` are **not** registered themselves. Nothing in the Results area is Scoped or Transient.
  - The lab daemon builds with `ValidateScopes = true` and `ValidateOnBuild = true` (`Daemon/LabDaemon.cs:251-255`), so F15's Testing gap does not apply here. The DI test below still pins the lifetime.
- The DB opens lazily, on first use, through `ResultsDb.Open(paths.Results, validator)`. `ResultsDb.Open` already checks `lab-results.db` against `[LabPaths.Results]` with `PathValidator` (`ResultsDb.cs:79`).
  - One `SemaphoreSlim(1, 1)` serializes every DB use, because a `SqliteConnection` is not thread-safe.
  - An open failure is returned as `[Lab.Unavailable]` and retried on the next call. Nothing throws at DI build time.
- Observers (A3) run **after** the gate is released. L26's `TriageService` and L52's `CoverageCollector` may then call back into `ResultsService` without deadlocking. `BundleIngestor` keeps one notify implementation; it is split into `WriteAsync` and `NotifyAsync` (item 4).
- New `Mcp/Areas/ResultsArea.cs` registers `ResultsService` (step 4) and `results_query` (step 7). R-REG holds: no other file registers Results services. L26 adds its lines to the same file.
- The `uat_attest` re-ingest stays in L25. After a successful `Attest`, the tool ingests the bundle again. The payload gains an `ingest` field. An ingest failure never fails or rolls back the attest, because the attest has already committed through `BundleStaging`.
- `uat_run` ingests at run end when a bundle was written, and gains the same `ingest` field. The payload's `overall` stays run.json's value (A4).

### D-ML92. Run-end and attest matching move to L26; L25 writes honest "unchecked" values

- L25 does not match known issues in the writer, the attestor or the runner. In those three places the bundle says so:
  - `"known_issues": {"checked": false, "reason": "the bundle does not match known issues until L26; the results DB ingest does"}`;
  - `known_failures: null`, never `[]`;
  - `new_failures` lists every failure. That is true as written, because C12 calls a failure "new" when no issue is linked to it.
  - `overall` stays conservative: any failure gives `fail`.
- The ingest does match, and records which way it went, in `runs.known_issues_json` (migration 2) and in the `ingest` payload:
  - checked: `{checked: true, issues: <n>, file_sha256}`;
  - unchecked: `{checked: false, reason}`.
  - The ingest is unchecked when `Lab:RepoRoot` is empty, `known-issues.toml` is absent under it, the file fails to load, or it cannot be read. An unchecked ingest matches against `KnownIssues.Empty`, so every row gets `known_match` null and is new.
  - The absent-file case is unchecked at the service level only. `KnownIssues.Load` keeps C12's "absent file is an empty set" for other callers.
- The bundle `overall` and the DB `overall` may differ until L26. Each payload names its own: `overall` is the bundle's, and `ingest.overall` is the DB's.
- L26 owns:
  - matching at run end and on attest;
  - the spec claim in the row JSON;
  - step pseudo-signatures for unmet expected errors;
  - a `requires` key on issues;
  - the exact KI-F67 and KI-F69 clause entries.
  - The work-packets L26 amendments B1-B5 list the shapes.

### D-ML93. L25 ingests `clause` and `inv` only, and every report names what is missing

- L25 ingests `clause` signatures and `inv` signatures, including the synthetic `invariants/<id>` rows.
- It refuses history bundles (`run.json` `kind == "history"`) with `[Lab.InvalidArgument] history bundles are not ingested until L25f`.
- New `Results/EvidenceCoverage.cs` is the single source for coverage:
  - `BuiltKinds = ["clause", "inv"]`;
  - `NotBuiltKinds = ["exc", "http", "hist", "twin", "peer", "env", "ver"]`;
  - `NotIngested = ["faults", "pairs", "history_runs"]`.
- The ingest stores that object in `runs.evidence_json` (migration 2). The `ingest` payload of `uat_run` and `uat_attest` echoes it, and `results_query` `runs` returns it.
- "Known" in the DB means every **built** signature of the row matched one open issue. `evidence.note` says so.
- Later packets:
  - **L25e** (new): `exc` and `http` evidence (row log windows, Hosting.Diagnostics lines, step method and route).
  - **L25f** (new): faults (the writer adds `target_id` and `kind`), pairs (against L53's real writer) and history bundles with `hist` signatures.
  - Each moves its kinds from `NotBuiltKinds` or `NotIngested` into `BuiltKinds` in `EvidenceCoverage`, in the same commit.

### D-ML94. KI-F67 is scoped to its row; scopes that cannot match are refused (amends D-ML87 and the C12 loader)

- KI-F67 in L25:
  - patterns `'^http:5\d\d\|[^|]*\|POST\|/api/v1/agents/enroll\|'` and `'^inv:I1\|(nodes|gateway)\|(unhandled|unexplained-500)$'`;
  - `[issue.scope] sections = ["catalog-nodes"]`, `rows = ["nodes-pg-down-enroll"]`;
  - `services` is dropped.
- KI-F69 is unchanged in L25. It keeps its `http` 4xx pattern with `services = ["nodes"]`, which is valid.
- L26 (B5) splits it: KI-F69 becomes row evidence only (clause signatures, `sections` and `rows` scope, `patterns = []`), and the `http` pattern moves unchanged to a new `KI-F69-HTTP` with `services = ["nodes"]`. One issue cannot hold both, because a clause signature carries no service. L28's oracle matches `KI-F69-HTTP` (L28 amendment).
- New loader rule (`KnownIssues.ParseIssue`):
  - An issue whose scope sets `services` is invalid if any signature or pattern has a kind that carries no service. Only `exc`, `http` and `inv` carry a service (C12).
  - An issue whose scope sets `invariants` is invalid if any kind is not `inv`.
- In L25, both claiming rows still resolve to `claimed_mismatch`, because their evidence is clause signatures. That is the honest verdict; L26 makes them resolvable.
- A later packet that needs KI-F67 outside the catalog row widens the scope with its own named sections and rows. L27's hunter row is the first (L27 amendment A5).

## Finishing list for the coder

Do the items in order. Commit after each step with the subject `feat(lab): results database, signatures and overall grading (step N/7)`. Steps 1-3 are done (`8f3a2bf1`, `aa5c3df7`, `7b708435`, `1d382ed6`), and step 4's first part is done (`bbb59400`).

Rules for every item:

- R-RESULT: no `Result<T>.Success(null)`; use `Result.Success()` for no value. IO, path and SQLite errors return a `Result`, never throw.
- R-PATH: every path goes through `IPathValidator.ValidatePath(path, [root])`.
- R-LOG: never log or return a token, a lease id or a vault value. Ingest error texts pass through `Redactor.Redact` before they go into a payload.
- R-USING: `using` on every `IDisposable`, including `ResultsService` and `ResultsDb` in tests.
- Regex: every new `Regex` gets `RegexOptions.CultureInvariant` and `TimeSpan.FromSeconds(1)`.
- Tests that start a daemon join `[Collection("LabHome")]`.
- No package versions in `.csproj`, and no new package.
- Nullable is on, and warnings are errors.

### Step 4 (rest): results service, wiring, re-ingest

1. **New `Results/KnownIssueCheck.cs`.**
   - Code:

     ```csharp
     public sealed record KnownIssueCheck(KnownIssues Issues, bool Checked, string? Reason, string? FileSha256)
     {
         public static KnownIssueCheck Of(KnownIssues issues, string fileSha256) => new(issues, true, null, fileSha256);
         public static KnownIssueCheck Unchecked(string reason) => new(KnownIssues.Empty, false, reason, null);
         // {"checked":true,"issues":<Issues.Issues.Count>,"file_sha256":"<hex>"} or {"checked":false,"reason":"<reason>"}
         public JsonObject ToJson();
     }
     ```

   - Verify: build.
2. **New `Results/ResultsService.cs`.**
   - Code:

     ```csharp
     public sealed class ResultsService : IDisposable
     {
         public ResultsService(LabPaths paths, IPathValidator validator, Redactor redactor, IServiceProvider services,
             ILoggerFactory loggers, TimeProvider time);

         // Reloads on every call. Path: Path.Combine(paths.RepoRoot, "docs", "guides", "uat-specs", "known-issues.toml"),
         // validated against [paths.RepoRoot]. Reads the bytes once; FileSha256 = lowercase hex of SHA256.HashData(bytes);
         // parses with KnownIssues.LoadText("known-issues.toml", text, logger).
         // Unchecked reasons (fixed text, never file content):
         //   "Lab:RepoRoot is not set" | "known-issues.toml is absent under Lab:RepoRoot" |
         //   "known-issues.toml could not be read" | "known-issues.toml is invalid; run the loader tests for the problems"
         public KnownIssueCheck LoadKnownIssues();

         // Gate -> open lazily -> new BundleIngestor(db, validator, LoadKnownIssues(), redactor.Redact,
         //   services.GetServices<IIngestObserver>(), loggers.CreateLogger<BundleIngestor>(), time)
         // -> WriteAsync(bundleFolder, [paths.UatRuns, paths.Jobs], ct) -> release gate -> NotifyAsync on success.
         public Task<Result<IngestResult>> IngestAsync(string bundleFolder, CancellationToken ct);

         // Step 7's queries go through this, under the same gate.
         internal Task<Result<T>> ReadAsync<T>(Func<ResultsDb, Result<T>> read, CancellationToken ct);

         // {"ok":true,"run_id","overall","rows","signatures","known_issues":<check.ToJson()>}
         // or {"ok":false,"error":"<redactor.Redact(error)>"}. Step 5 adds "evidence".
         public JsonObject ToPayload(Result<IngestResult> ingested);

         public void Dispose(); // disposes the ResultsDb when open, then the gate
     }
     ```

   - An open failure returns `[Lab.Unavailable] the results database is not available: <error>` and leaves the DB closed, so the next call retries.
   - Observers are resolved at call time. This avoids a construction cycle with L26 and L52 observers that take `ResultsService`.
   - Verify: build. Step 5's tests cover the behaviour.
3. **New `Mcp/Areas/ResultsArea.cs`**, in the same shape as `Mcp/Areas/HistoryArea.cs`:

   ```csharp
   public static partial class LabMcpServer
   {
       static partial void AddResultsArea(IServiceCollection services)
       {
           services.AddSingleton<ResultsService>();
       }
   }
   ```

   - Verify: build.
4. **`Results/BundleIngestor.cs`.**
   - Change the third constructor parameter (`:46`) from `KnownIssues known` to `KnownIssueCheck known`. Match with `known.Issues` (`:149`).
   - Add `KnownIssueCheck KnownIssues` as the last member of `IngestResult` (`:15`), and fill it at `:190`.
   - Split `IngestAsync` (`:66-99`):
     - `internal async Task<Result<IngestResult>> WriteAsync(string bundleFolder, IReadOnlyList<string> allowedRoots, CancellationToken ct)` holds `:68-92`, without the notify.
     - `internal Task NotifyAsync(string runId, CancellationToken ct)` wraps the existing `NotifyObserversAsync`.
     - `IngestAsync` stays public, as `WriteAsync` and then, on success, `NotifyAsync`. Existing tests keep using it.
   - Update the five test call sites to pass `KnownIssueCheck.Of(KnownIssues.Empty, "test")`: `BundleIngestorTests.cs:112,123` and `FlakeOnIngestTests.cs:43,58,72`.
   - Verify: `FS` with `~Dhadgar.Lab.Tests.Results` is green.
5. **`Tools/Uat/UatRunTool.cs`: run-end ingest.**
   - Add `internal static async Task<JsonObject?> IngestAfterRunAsync(ResultsService results, string? folder, CancellationToken ct)`. It returns null when `folder` is null (plan only); otherwise it returns `results.ToPayload(await results.IngestAsync(folder, ct))`.
   - Call it after the `WriteError` check (`:218-221`) with `_services.GetRequiredService<ResultsService>()` and `sink.Folder`, and set `payload["ingest"]` beside `payload["bundle"]` (`:226`).
   - `payload["overall"]` stays `sink.FinalOverall` (`:229-232`).
   - Update `Description` (`:59`) to name `ingest`.
   - Verify: build.
6. **`Tools/Uat/UatReportTools.cs`: `uat_attest` re-ingest.**
   - `UatAttestTool` gains a constructor parameter `ResultsService results` (`:157`), stored as `_results`.
   - `ExecuteAsync` (`:174`) becomes `async` (drop the `Task.FromResult` wrappers). After `grade` succeeds (`:192-195`), it calls `Result<IngestResult> ingested = await _results.IngestAsync(folder.Value!, ct)`. It returns the existing anonymous object plus `ingest = _results.ToPayload(ingested)`.
   - A failed ingest still returns success with `ingest.ok = false`.
   - Update `Description` (`:171`).
   - `UatAttestTests.cs:419,445` construct the tool with `using var results = new ResultsService(LabPaths.FromRoot(lab), _validator, redactor, new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, time)`.
   - Verify: build.
7. **DI test.** Extend `tests/Dhadgar.Lab.Tests/Daemon/LabDaemonServiceLifetimeTests.cs` `Services_BuildWithScopeAndBuildValidation_ResolveEveryLabService` (`:30`) with:
   - `Assert.False(File.Exists(Path.Combine(services.GetRequiredService<LabPaths>().Results, "lab-results.db")))` before any resolve of `ResultsService` (lazy open);
   - `Assert.Same(services.GetRequiredService<ResultsService>(), scope.ServiceProvider.GetRequiredService<ResultsService>())`;
   - `Assert.NotNull(services.GetRequiredService<UatAttestTool>())`.
   - Verify: reverting item 3 makes the test fail with "No service for type ResultsService". Then run build and `FS` with `~Dhadgar.Lab.Tests.Daemon`, `~Dhadgar.Lab.Tests.Uat` and `~Dhadgar.Lab.Tests.Results`, and commit `(step 4/7)`.

### Step 5: honest fields, coverage, migration 2, history refusal

8. **New `Results/EvidenceCoverage.cs`.**
   - Code:

     ```csharp
     public static class EvidenceCoverage
     {
         public static readonly IReadOnlyList<string> BuiltKinds = ["clause", "inv"];
         public static readonly IReadOnlyList<string> NotBuiltKinds = ["exc", "http", "hist", "twin", "peer", "env", "ver"];
         public static readonly IReadOnlyList<string> NotIngested = ["faults", "pairs", "history_runs"];
         public const string Note = "partial: known means every built signature matched one open issue; not_built_kinds and not_ingested are not in the index yet";
         // {"built_kinds":[...],"not_built_kinds":[...],"not_ingested":[...],"note":Note}
         public static JsonObject ToJson();
     }
     ```

   - `ResultsService.ToPayload` adds `"evidence": EvidenceCoverage.ToJson()` on success.
   - Verify: build.
9. **`Results/ResultsDb.cs`: migration 2.**
   - Append a second entry to `Migrations` (`:20-47`): `alter table runs add column known_issues_json text; alter table runs add column evidence_json text;`.
   - `RunRecord` (`:463`) gains trailing `string? KnownIssuesJson = null, string? EvidenceJson = null`.
   - `UpsertRun` (`:131-149`) inserts and updates both columns.
   - Add a typed read `Result<RunRecord> RunOf(string runId)` for tests and step 7. Return a failure `[Lab.NotFound]` for a missing run, never `Success(null)`.
   - In `BundleIngestor.WriteRun` (`:196-209`), set `KnownIssuesJson = _known.ToJson().ToJsonString()` and `EvidenceJson = EvidenceCoverage.ToJson().ToJsonString()`.
   - Verify: `ResultsDbTests` "migrate twice" still passes, and `LatestSchemaVersion` is 2.
10. **`Results/BundleIngestor.cs`: history refusal.** After `run.json` is read (`:86-90`), if `Text(run.Value, "kind") == "history"`, return `[Lab.InvalidArgument] history bundles are not ingested until L25f`. Nothing is written and no observer runs.
11. **Bundle honesty: `Uat/Bundle/BundleWriter.cs`, `Uat/Bundle/BundleAttestor.cs`, `Uat/UatRunner.cs`, `Results/RunSummary.cs`.**
    - Writer run object (`BundleWriter.cs:533-569`):
      - `known_failures = (IReadOnlyList<string>?)null`;
      - a new `known_issues = new { @checked = false, reason = UncheckedReason }`, after `known_failures`;
      - `new_failures` unchanged. The writer's `JsonOptions` (`:30-34`) does not drop nulls, so the key stays present with `null`.
    - Put `UncheckedReason` (the D-ML92 text) once, as `public const string BundleUncheckedReason` on `KnownIssueCheck`. The writer and the attestor both use it.
    - Fix the comment at `:386`: the overall is unmatched until L26.
    - Attestor `RecomputeRun` (`BundleAttestor.cs:262-270`): `runObj["known_failures"] = null`, and `runObj["known_issues"] = new JsonObject { ["checked"] = false, ["reason"] = KnownIssueCheck.BundleUncheckedReason }`. Fix the comment at `:262`.
    - Runner `UatRunner.cs:211`: the comment becomes "Unmatched overall (D-ML92): used only when no bundle exists (plan_only); the bundle and the ingest carry their own."
    - `RunSummary.cs:6-7`: replace the doc with "One graded row of a run. Run-start and run-end violations arrive as the synthetic rows `invariants/<id>` (`UatRunner.SyntheticRows`)."
12. **Tests for step 5**, new or added in `tests/Dhadgar.Lab.Tests/Results/` and `tests/Dhadgar.Lab.Tests/Uat/Bundle/`:
    - `EvidenceCoverageTests.EvidenceCoverage_KindsPartitionC12`: the union of `BuiltKinds` and `NotBuiltKinds` equals `SignatureBuilder.Kinds`, and the two are disjoint. It fails if a packet adds a kind to one list and not the other.
    - `BundleIngestorTests.Ingest_SignatureKinds_AreOnlyBuiltKinds`: ingest a fixture with a NotHeld clause and an unallowed violation; every stored signature kind is in `BuiltKinds`.
    - `BundleIngestorTests.Ingest_RunEndViolationRow_IsInvSignature`: a fixture row `invariants/I1` (FAIL, one unallowed I1 violation) stores `inv:I1|nodes|unhandled` and the row is in `new_failures`.
    - `BundleIngestorTests.Ingest_HistoryBundle_IsRefused`: `run.json` `kind = "history"` gives the item 10 error, and `RunOf` gives `[Lab.NotFound]`.
    - `ResultsServiceTests.Ingest_UncheckedKnownIssues_RecordsUnchecked`: `LabPaths.FromRoot(tmp, repoRoot: "")` ingests a FAIL row. `ingest.known_issues.checked` is false with reason "Lab:RepoRoot is not set", `RunOf(...).KnownIssuesJson` says the same, and the row `known_match` is null.
    - `ResultsServiceTests.Ingest_CheckedKnownIssues_RecordsShaAndCount`: a temp repo root with a one-issue `docs/guides/uat-specs/known-issues.toml` gives `checked: true`, `issues: 1`, and a `file_sha256` equal to the file's hash.
    - `ResultsServiceTests.Ingest_ObserverCallsBack_NoDeadlock`: a registered `IIngestObserver` that calls `ReadAsync` finishes within 5 s. It fails if observers run inside the gate.
    - `UatAttestTests.Writer_KnownIssuesUnchecked_KnownFailuresNull`: run.json has `known_failures` null and `known_issues.checked` false.
    - `UatAttestTests.Attest_KnownIssuesUnchecked_KnownFailuresNull`: the same after `BundleAttestor.Attest`.
    - `UatAttestTests.Attest_ReingestsBundle_DbRowGradeUpdated`: ingest, attest `held` through `UatAttestTool`, then `ResultsDb.RowGradeOf` shows the new grade and `ingest.ok` is true.
    - `UatAttestTests.Attest_IngestFails_AttestStillCommitted`: a `ResultsService` whose results root is a file, not a folder, so the open fails. The attest returns success, the row JSON holds the attestation, and `ingest.ok` is false with `[Lab.Unavailable]`.
    - `UatRunToolIngestTests.IngestAfterRun_NullFolder_IsNull` and `IngestAfterRun_WrittenBundle_IsIngested`: the second writes a bundle with `BundleWriter` as `UatRun_ReturnedOverallEqualsRunJson` does, then calls item 5's helper. `ok` is true and `RunOf` finds the run.
    - Verify: build, then `FS` with `~Dhadgar.Lab.Tests.Results` and `~Dhadgar.Lab.Tests.Uat`. Commit `(step 5/7)`.

### Step 6: KI-F67 scope and the loader rule

13. **`docs/guides/uat-specs/known-issues.toml`: KI-F67** (`:18-30`).
    - Second pattern: `'^inv:I1\|(nodes|gateway)\|(unhandled|unexplained-500)$'`.
    - Its `[issue.scope]` becomes `sections = ["catalog-nodes"]` and `rows = ["nodes-pg-down-enroll"]`, with `services` removed.
    - KI-F69 and KI-ENROLL-RETRY are unchanged.
14. **`Results/KnownIssues.cs`: the loader rule.**
    - `KnownIssuePattern` (`:24`) gains `string Kind`, set in `CompilePattern` from the `kind` it already finds (`:337`).
    - In `ParseIssue`, after `ParseScope` (`:326`) and before the problem check (`:327`):
      - when `scope.Services.Count > 0`, every signature kind and pattern kind not in `{exc, http, inv}` adds `"<label>: scope services cannot match a <kind> signature (it carries no service)"`;
      - when `scope.Invariants.Count > 0`, every kind other than `inv` adds `"<label>: scope invariants cannot match a <kind> signature"`.
    - Signature kinds come from the prefix that is already checked at `:305`.
15. **Tests for step 6.**
    - `KnownIssuesTests`:
      - `Load_ServicesScopeWithClauseSignature_IsInvalid`;
      - `Load_InvariantsScopeWithHistPattern_IsInvalid`;
      - `Load_ServicesScopeWithHttpPattern_IsValid`, which guards against refusing too much.
    - `KnownIssueClaimsResolveTests`, on the shipped file:
      - `ShippedFile_LoadsWithNoProblems`;
      - `Match_KiF67_NodesI1OutsideClaimingRow_IsNew`: `RowFailure("catalog-other", "any-row", null, [inv:I1|nodes|unhandled])` is `new`, and the same signature in `("catalog-nodes", "nodes-pg-down-enroll", null, ...)` is `known` as `KI-F67`.
    - Verify: with item 13 reverted, `Match_KiF67_NodesI1OutsideClaimingRow_IsNew` fails, because the old scope excuses it. Then build, `FS` with `~Dhadgar.Lab.Tests.Results`, and commit `(step 6/7)`.

### Step 7: the old step 5

16. **`Observe/ReplayJudge.cs`:** the C18 signature rule, as in file 5 of the packet. Use `SignatureBuilder.Primary` over the ingested rows. A replay of a run whose primary signatures are unbuilt kinds compares only built kinds, and says so in its reason.
17. **New `Tools/Results/ResultsQueryTool.cs` (`results_query`).**
    - Views as in file 6. The `runs` view returns `known_issues` and `evidence` parsed from the two migration 2 columns. Every read goes through `ResultsService.ReadAsync`.
    - Register it in `ResultsArea.cs` with `AddLabTool<ResultsQueryTool>(services)`. Remove `"results_query"` from the `// L25` PlannedTools group (`Policy/ToolPolicy.cs:112-113`); the gate at `:29` stays.
    - The DI test's tool-set assertion (`LabDaemonServiceLifetimeTests.cs:63`) then expects it with no edit.
18. **A6 and S-L6.**
    - Add `ShippedSpecsRegistryValidateTests` per A6.
    - In `tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs` `AssertRunPasses` (`:156`), assert that the result's `ingest.ok` is true, `ingest.overall` is `"pass"`, `ingest.known_issues.checked` is true (S-L6 runs with a real repo root), and `LabPaths.FromRoot(home, repoRoot).Results\lab-results.db` exists. This file runs only at the wave boundary.
    - Verify: run the full checks below and commit `(step 7/7)`.

## Checks (last step; earlier steps use the scoped `FS` filters named above)

```powershell
dotnet restore C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l25\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj --force-evaluate
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l25\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true --no-restore
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l25\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-restore --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests"
git -C C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l25 status --porcelain   # no lock file left modified
```

The restore must leave both lab lock files unchanged; L25 adds no package in steps 4-7. If either lock file shows as modified, stop and report it.

## Out of L25 (owned elsewhere)

| Item | Owner | Where specified |
| --- | --- | --- |
| Matching at run end and on attest, row `known_issue` claim, step pseudo-signatures, `requires`, exact KI-F67/KI-F69 clause entries | L26 | work-packets L26 amendments B1-B5 |
| `exc` and `http` evidence | L25e | work-packets `### L25e` |
| Faults, pairs, history bundles and `hist` | L25f | work-packets `### L25f` |
| KI-F67 scope for the hunter row | L27 | work-packets L27 amendment A5 |
| KI-F69 split into KI-F69 (row evidence) and `KI-F69-HTTP` | L26, used by L28 | work-packets L26 B5, L28 amendment |
| `twin`, `peer`, `env`, `ver` moved to `BuiltKinds` | L53, L58, L57, L60 | work-packets wave 5 plan, dependency amendments |

Security review: yes, for the whole packet (evidence store, with ingest error text going into payloads).
