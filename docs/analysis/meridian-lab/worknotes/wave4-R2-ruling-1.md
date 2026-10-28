# Ruling: wave 4 review R2 (L22, L23, L24), pass 1

Brief: rule on wave4-review-2 (verdict `decision`) and confirm or amend its fix notes, so that one fresh packet-coder can apply them without making choices. Review report: `.claude/reports/meridian-lab/wave4-review-2.md`. Branch `campaign/meridian-lab`, read at tip f4e0824d. That tip is 145c421d plus the R3 fix, which touches none of the files below. No source or test file was edited for this ruling.

- **Decisions:** D-ML74 (recorded), D-ML75 (renumbering), D-ML76 (D1). All Accepted. **No BlockedDecision.**
- **Findings:** F69 (Nodes has no problem+json for binding 400s), F70 (a missing pointer grades UNVERIFIED), F71 (the coverage manifest names parked projects).
- **Fix packet:** one packet, `wave4-R2-fix-1`, with the 9 items below. Security review: yes (item 2 changes how the runner calls fault tools).

## Numbering (D-ML74, D-ML75)

The brief said "findings from F68". F68 already exists (README, L19-ruling-2), so the new findings start at F69.

Two rulings carried the number D-ML73. The README's D-ML73 is the BlockedDecision on Nodes readiness under a broker outage. It was written first, in L19-ruling-2, and F68, L23's spec at `work-packets.md:2462` and `ToxiproxySeamTests.cs:188` cite it. The orchestrator's later ruling in the L22 session (the `FaultRow_IsBlockedUntilL22` assertion is superseded) reused the number in RESUME line 8, `worknotes/L22.md:7` and `UatRunnerTests.cs:230`. The README D-ML73 keeps its number. The orchestrator's ruling becomes **D-ML75**. RESUME also records **D-ML74** (live-stack E2E tests run at the wave boundary), which had no ledger row. It now has one, attributed to the orchestrator.

Orchestrator actions:

- RESUME line 8: change "D-ML73 (FaultRow_IsBlockedUntilL22 …)" to "D-ML75".
- RESUME line 12: change "No BlockedDecision" to say that D-ML71 and D-ML73 are open, non-blocking owner decisions.
- `worknotes/L22.md:7`: append "(renumbered D-ML75, wave4-R2-ruling-1)". Worknotes are history, so the old number stays and the note goes beside it.
- Item 8 removes the stale test comment.

## D1 (decision): the 1 MiB step, ruled D-ML76

**Ruling: drop the step.** `nodes-enroll-malformed` keeps two steps, an empty body and a wrong-typed field. `http_request` gets no generated-body argument. The architect has amended L23's spec in `work-packets.md` (file 2, row `nodes-enroll-malformed`).

Reasons:

1. Oversize enroll bodies are already covered by the plan.
   - L28, the contract fuzzer, sends boundary strings of empty, 64 KiB and 1 MiB on the enroll target. Its oracle fails a 400 without problem+json.
   - L58's `oversize` peer attack sends a 2 MiB enroll body.
   - A third copy in the catalog would add no coverage.
2. A generated-body argument would be a new `http_request` surface (C7 args, redaction, size caps and a security pass) built only to repeat L28.
3. An 800 KiB TOML literal makes the catalog unreadable, and the repo's diff tooling chokes on it.

This is an architect call. It changes neither product behaviour nor a security posture, so it is not an owner decision.

## Fix notes ruled

| Note | Ruling | Change from the review |
| --- | --- | --- |
| N1 | Amended | The clause counts both I2 probes, not only `OutboxState`. Delivered `OutboxMessage` rows are deleted, so any row left is pending. |
| N2 | Amended | The try block also covers the two row-end `HealDueAsync` calls. Variable declarations move above the `try`. The abandoned heal uses a `TimeProvider` token and swallows only its own timeout. |
| N3 | Confirmed | none |
| N4 | Amended | The `gateway.toml:229-250` reference is dropped (that row has no wait). Nodes edits as written. |
| N5 | Amended (D-ML76, F69) | Pointer wire case `Content-Type`. Clause on both steps. Row gets `known_issue = "KI-F69"`. |
| N6 | Amended (final under D-ML76) | Header count 2 and the row `expected` text. |
| N7 | Confirmed, extended | The row `expected` text drops "(Degraded)" too. |
| N8 | Confirmed, extended | The `FaultSpec` `expected` string drops "until L22" too. |
| N9 | Amended (F71) | The stale check needs a project file, not just a folder. The six parked entries leave the manifest. Adapter check by catalog file name. |

N5 detail. Nodes has no `IProblemDetailsService` and no status-code pages (F69). The wrong-typed body fails minimal-API binding, so its 400 most likely carries no body and no `Content-Type`. The pointer then does not resolve, and the clause grades UNVERIFIED, not NOT_HELD (F70). Per D-ML23 the expectation stays: an enroll error must be problem+json. The run settles it. If F69 is confirmed, the fix is product-side: `AddDhadgarErrorHandling` plus `UseDhadgarErrorHandling` in Nodes, outside this campaign, like VS-07R. The empty-body step returns `ProblemDetailsHelper.BadRequest("invalid_platform")`, so its clause should hold today.

## Not-fixed items

- **D2 (security label):** confirmed and campaign-level. D-ML16 and D-ML39 ship one campaign PR with the `security-review` label. The orchestrator checks that wave4-security-1/2 covered L22-L24, and gives the fix packet a `security-advisor` pass (item 2).
- **D3 (catalog tool names unchecked):** noted for wave 5. `ShippedSpecsValidateTests` loads with `SpecLoader.LoadText` only, so tool names and argument shapes in `catalog/` are never checked against the registry. L25 or L26, whichever first builds `SpecValidator` with the tool registry in tests, must run it over `docs/guides/uat-specs/**`. The orchestrator copies this into the L25 brief.
- **D4 (status honesty):** confirmed.
  - `lab-coverage.json` `real` keeps its D-ML33 meaning: an adapter plus a catalog fault row exist. That is structural only.
  - Any README or `docs/PROJECT-STATE.md` line about lab catalog coverage says "code exists" until a recorded `uat_run` of that catalog backs it. PROJECT-STATE has no lab line today.
  - The README packet table still marks L22-L24 (and other merged wave 4 rows) BlockedDependency. The orchestrator sets them to Integrated.

## Fix list (packet `wave4-R2-fix-1`)

All paths are relative to the worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab`. Edit only the files named here.

Rules that apply:

- `Result<T>` for failures, never exceptions.
- `using` on every `IDisposable`.
- Nullable is on, and warnings are errors.
- No package versions in `.csproj`.
- Test conventions are those of `tests/Dhadgar.Lab.Tests`: xUnit `[Fact]`, `Harness`/`ScriptedTool`, and `[Fact(Timeout = 10000)]` for async waits.
- Do not change grading, `HealDueAsync` or `ResetAsync`.

1. **`docs/guides/uat-specs/catalog/nodes.toml:250`** (N1).
   - Change: replace the line with
     `sql = 'select (select count(*) from "OutboxMessage") + (select count(*) from "OutboxState" where "Delivered" is null) as pending'`
     Keep `pointer = "/rows/0/pending"`, `value = 0`, `since`, `timeout_ms = 60000` and `poll_ms = 5000`. Leave the clause text as it is.
   - Verify: `Grep '"Delivered"' docs/guides/uat-specs/catalog/nodes.toml` finds only `"OutboxState" where "Delivered"`.

2. **`src/Tools/Dhadgar.Lab/Uat/RowExecutor.cs:109-205`** (N2, L22 file 1: "any injected faults still healed"). Change `ExecuteRowAsync` to this shape:

   ```csharp
   string? faultError = null;
   string? stepError = null;
   bool expectFailed = false;
   string? expectFailure = null;
   var stepResults = new List<UatStepResult>();
   try
   {
       // Faults that start before the first step. ... (lines 109-118, without the faultError declaration)
       string? previousStep = null;
       // the step loop, lines 125-194, unchanged
       // the last-step AfterStepAsync, lines 196-200, unchanged
       // the two HealDueAsync calls and `faultError ??= healError`, lines 202-205, unchanged
   }
   catch (OperationCanceledException)
   {
       // A cancelled run (MCP cancel, daemon shutdown) still heals what it injected. FaultBook's deadline and lease-end heal are the backstop.
       await HealAbandonedAsync(vars, run, faults).ConfigureAwait(false);
       throw;
   }
   ```

   Lines 206 onward (Blocked, settle, clauses, invariants) stay after the `try` and are unchanged. Add beside `HealDueAsync`:

   ```csharp
   private static readonly TimeSpan AbandonedHealBudget = TimeSpan.FromSeconds(30);

   /// <summary>
   /// Heals every fault still in place after the row was cancelled, under its own budget, because the run's token is already cancelled.
   /// A failed heal falls to fault_reset inside HealDueAsync. When the budget runs out, the FaultBook backstops heal what is left.
   /// </summary>
   private async Task HealAbandonedAsync(UatVars vars, RunContext run, FaultState faults)
   {
       if (faults.Active.Count == 0)
       {
           return;
       }

       using var budget = new CancellationTokenSource(AbandonedHealBudget, _time);
       try
       {
           _ = await HealDueAsync(_ => true, vars, run, faults, budget.Token).ConfigureAwait(false);
       }
       catch (OperationCanceledException) when (budget.IsCancellationRequested)
       {
           // Budget spent; FaultBook's deadline and lease-end heal remain.
       }
   }
   ```

   Known limit: if the cancel lands while an inject call is in flight, that fault was never recorded in `faults.Active`, and only the FaultBook backstops heal it. Do not try to close this in the packet.

   Test: add `FaultRow_Cancelled_FaultStillHealed` to `tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs`, next to `FaultRow_StepBlocks_FaultStillHealed` (line 235).
   - Add a spec constant `CancelledStepSpec`. Copy `BlockingStepSpec` (line 58), but make the step `id = "cancel"` and `tool = "faults_active"`.
   - Test body:

   ```csharp
   [Fact(Timeout = 10000)]
   public async Task FaultRow_Cancelled_FaultStillHealed()
   {
       using var cts = new CancellationTokenSource();
       var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
       var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
       var cancel = new ScriptedTool("faults_active", () =>
       {
           cts.Cancel();
           throw new OperationCanceledException(cts.Token);
       });
       using var harness = new Harness(new FakeProbe(true), null, kill, heal, cancel);

       await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Runner.RunAsync(
           new UatRunRequest(Load(CancelledStepSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), cts.Token));

       Assert.Single(kill.Calls);
       Assert.Equal("f-kill-1", Assert.Single(heal.Calls).FaultId);
   }
   ```

   - Why it fails when reverted: `LabToolDispatcher` rethrows the step's OCE because the token is cancelled (`LabToolDispatcher.cs:154`). On today's code the OCE leaves `ExecuteRowAsync` before any heal, so `heal.Calls` is empty.
   - Verify:
     - With the RowExecutor change stashed, the test fails on `Assert.Single(heal.Calls)`. With the change, it passes.
     - `FaultRow_StepBlocks_FaultStillHealed`, `FaultRow_InjectFails_IsBlockedAndOthersHealed` and `HealFails_LabNotClean_RemainingRowsBlocked` still pass.

3. **`docs/guides/uat-specs/catalog/gateway.toml:50-52`** (N3).
   - Change: between step `settle` (`wait_ms = 60000`) and step `readyz-down`, insert:

     ```toml
       [[row.step]]
       id = "settle-more"
       wait_ms = 15000
     ```

   - Keep the row `expected` (line 19) and the clause text (line 69) as "within 75 s". The fault is then in place for about 75 s, under the FaultBook 120 s default deadline.
   - Verify: in `gw-nodes-down`, `Grep -n "wait_ms"` shows 60000 and then 15000, both before `id = "readyz-down"`.

4. **`docs/guides/uat-specs/catalog/nodes.toml:57` and `:296-299`** (N4).
   - Change:
     - Row `nodes-pg-down-enroll`, step `settle`: `wait_ms = 10000` becomes `wait_ms = 60000`.
     - Row `nodes-killed-enroll`, step `await`: `wait_ms = 30000` becomes `wait_ms = 60000`. Then insert the step below between `await` and `enroll-again`, so the single sample sits at the spec's 90 s bound:

       ```toml
         [[row.step]]
         id = "await-more"
         wait_ms = 30000
       ```

   - Do not change `nodes-pg-readonly` or `nodes-token-reuse-after-failure`. Their specs give no time bound.
   - Verify: `Grep -n "wait_ms" nodes.toml` shows 60000 at `nodes-pg-down-enroll` `settle`, and 60000 then 30000 in `nodes-killed-enroll`. The enroll-call counts in the header (lines 5-6) do not change for these rows.

5. **`docs/guides/uat-specs/catalog/nodes.toml:710-736`** (N5, D-ML76, F69).
   - Change:
     - Line 733: `pointer = "/result/headers/Content-Type"`. `LabHttp` keeps the wire case, and `JsonPointer` is case-sensitive.
     - Line 730: clause text becomes "the wrong-typed enroll reply is application/problem+json".
     - After line 714, add `known_issue = "KI-F69"`.
     - Append a second clause after the first:

       ```toml
         [[row.expect]]
         id = "problem-json-empty"
         text = "the empty-body enroll reply is application/problem+json"
         source = "step"
         step = "empty"
         pointer = "/result/headers/Content-Type"
         op = "contains"
         value = "application/problem+json"
         required = true
       ```

     - Keep both clauses `required = true` (D-ML23).
   - Verify: `Grep "headers/content-type" docs/guides/uat-specs` finds nothing. `Grep -c "headers/Content-Type"` finds 2 in nodes.toml.

6. **`docs/guides/uat-specs/catalog/nodes.toml:6` and `:713`** (N6, D-ML76).
   - Change:
     - Line 6: `nodes-enroll-malformed 3` becomes `nodes-enroll-malformed 2`.
     - Line 713: `expected = "An empty body and a wrong-typed field each return 400 application/problem+json"`.
     - The title stays.
   - Verify: the row has exactly 2 `path = "/api/v1/agents/enroll"` steps, and the header says 2.

7. **`docs/guides/uat-specs/catalog/nodes.toml:237` and `:186`** (N7).
   - Change:
     - Line 237: `text = "Nodes /healthz stays 200 while the broker is cut (F68: a broker outage is not a 503)"`.
     - Line 186: `expected = "The enroll returns 201 and /healthz returns 200 while RabbitMQ is cut; undelivered outbox rows reach zero within 60 s of the heal"`.
     - The pointer, op and value stay.
   - Verify: `Grep "Degraded" docs/guides/uat-specs/catalog/nodes.toml` finds no clause `text` or row `expected` that claims the Degraded state.

8. **`tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs:220,230,48`** (N8, D-ML75).
   - Change:
     - Rename `FaultRow_IsBlockedUntilL22` to `FaultRow_UnregisteredFaultTool_IsBlocked`.
     - Delete the comment line 230.
     - In `FaultSpec`, line 48, set `expected = "blocked: the fault tool is unregistered"`.
     - The assertions stay.
   - Verify: `Grep "UntilL22|until L22" tests/` finds nothing, and the renamed test passes.

9. **`tests/Dhadgar.Lab.Tests/Coverage/LabCoverageTests.cs` and `src/Tools/Dhadgar.Lab/lab-coverage.json`** (N9, F71).
   - In `ManifestIsComplete`, after line 50, add:

     ```csharp
     // An entry must still name a project: a folder left behind with only bin/obj (a moved or parked project) does not count (F71).
     var stale = entries
         .Where(e => !discovered.Contains(e.Project) && !HasProjectFile(ResolveUnderRoot(root, e.Project)))
         .Select(e => e.Project)
         .OrderBy(p => p, StringComparer.Ordinal)
         .ToList();
     Assert.True(stale.Count == 0, "Manifest entries with no project: " + string.Join(", ", stale));
     ```

     Add the helper:

     ```csharp
     private static bool HasProjectFile(string directory) =>
         Directory.Exists(directory)
         && (Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any()
             || File.Exists(Path.Combine(directory, "package.json")));
     ```

   - In `RealEntriesHaveAdapterAndFaultRow`, after line 62, add:

     ```csharp
     Assert.True(
         string.Equals(Path.GetFileNameWithoutExtension(entry.Catalog), entry.Adapter, StringComparison.Ordinal),
         $"{entry.Project} names adapter '{entry.Adapter}' but catalog '{entry.Catalog}'; the catalog file is named for its adapter.");
     ```

   - In `lab-coverage.json`, delete the entries for the six projects that VS-01 parked: lines 16 (Tasks), 17 (Files), 19 (Mods), 20 (Billing), 21 (Agent.Linux) and 24 (ShoppingCart). Remove the trailing comma from the `src/Dhadgar.Scope` line, which becomes the last entry. Fifteen entries remain. Parked projects are outside `src/` discovery. If one is unparked, `ManifestIsComplete` asks for its entry again.
   - Verify:
     - Run the new stale check before the JSON edit. It must fail and name all six, even on this machine, where their `bin`/`obj` folders remain. Then make the JSON edit and the check passes.
     - Temporarily add `{ "project": "src/Dhadgar.Gone", "status": "stub", "note": "x" }`. `ManifestIsComplete` fails and names it. Revert.
     - Temporarily point the Gateway entry's catalog at `nodes.toml`. `RealEntriesHaveAdapterAndFaultRow` fails. Revert.

## Checks (in order)

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~UatRunnerTests|FullyQualifiedName~ShippedSpecsValidateTests|FullyQualifiedName~LabCoverageTests"
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&FullyQualifiedName!~DockerTests&FullyQualifiedName!~E2ETests"
```

- The broad filter matches `worknotes/L51.md:13`. Its known failure is only the pre-existing Docker case `LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten`, and only if it is selected.
- Under D-ML74, a `uat_run` of `catalog-gateway` and `catalog-nodes` on Docker happens at the wave boundary. The orchestrator records the outcomes of `problem-json`, `problem-json-empty`, `outbox-drained` and `readyz-not-ready` there. That run confirms or clears F69.

## Packet facts

- **Worktree / branch:** `.claude/worktrees/meridian-lab` on `campaign/meridian-lab`, the same as the R3 fix pass f4e0824d. Commit with explicit paths only.
- **Commit subject:** `fix(lab): apply wave 4 R2 review fix notes, pass 1 [L22-L24]`
- **Size:** about 30k working tokens. One packet.
- **Docs owed:** none beyond the catalog files above. The architect has already updated the ledger.
- **Security review:** yes, `security-advisor` on item 2 (fault heal on cancel, `fault_reset` path).

## Ledger changes made by this ruling

- README:
  - F69, F70 and F71 added.
  - D-ML74 recorded (orchestrator).
  - D-ML75 added (the orchestrator's renumbered "D-ML73").
  - D-ML76 added.
- `work-packets.md`, L23 file 2:
  - `nodes-enroll-malformed` now has two steps with a problem+json clause per step at `/result/headers/Content-Type` (D-ML76).
  - The `nodes-rabbit-down-outbox` outbox clause counts `OutboxMessage` rows plus `OutboxState` rows with `Delivered` null.
