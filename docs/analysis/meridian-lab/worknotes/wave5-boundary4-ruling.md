# Wave 5 boundary review pass 4: ruling (meridian-lab)

Date: 2026-11-02. Architect ruling after boundary review pass 4 (D-ML137), which ended R1 `fix`, R2 `ship`, R3 `fix` and security `ship` at campaign tip `0c1900b3`. Boundary passes are past the 3-pass limit (campaign-packet §4), so this note decides every open note.

Reports read: `.claude/reports/meridian-lab/wave5-boundary-review-4-R1.md`, `-R2.md`, `-R3.md`, `wave5-boundary-security-4.md`; [run 10 ruling](wave5-run10-ruling.md); the ledger README.

## 1. Ruling

- **Apply now:** R1-N1, R3-N1, security H1 and H2, plus three doc items. All go into one small packet, **R11-1** (section 5). None needs Docker.
- **Keep the fixed port guard (D-ML139).** The docs are made honest; the guard is not widened.
- **Park:**
  - The chaos zero-loop pass becomes **KG-W5-20**, and L29b takes it.
  - The soak `rows` pre-check goes to L29b.
  - H3 and H4 go to L54b.
  - Old high-seed DB rows stay unmigrated (KG-W5-18, accepted).
- **New finding F123:** the non-Docker suite filter is wrong. Every run 10 prescription used `Category!=Docker`, and no test has that trait. Section 4 corrects it.
- **The boundary can close** after four steps, in order (section 7):
  1. R11-1 passes its ruling check (`ship` from both the reviewer and the security pass).
  2. R11-1 merges.
  3. The confirmation run in section 6 runs with no unparked red.
  4. The architect writes a boundary-close ruling.

  No further review pass is needed: R11-1 is a small fix packet, and its ruling check is the last review on this boundary. A ruling check that is not `ship` becomes a BlockedDecision (campaign-packet §4).

## 2. Disposition per note

| Note | Source | Disposition | Where |
| --- | --- | --- | --- |
| R1-N1: `enroll-probe.toml` is never validated against the registry | R1 fix note | Apply now. The note is re-specced so that reverting it fails the test | R11-1 (a) |
| R1: `soak_run` has no `RowFilterCheck` pre-check | R1 and R2 not fixed | Park in L29b. The soak fails slowly but honestly (`fail` with 0 loops), so no KG | L29b |
| R1: `FlakeCheckTool` has no direct test | R1 not fixed | Park. The live R9-1 probe (section 6, segment 1) is the check | KG-W5-16 |
| R1: old high-seed DB rows still stored as seed 0 | R1 not fixed | Accept. No migration (D-ML141) | KG-W5-18 |
| R1: README does not mention `enroll-probe.toml` | R1 not fixed (docs) | Apply now. One sentence | R11-1 (c) |
| R1: S-L12 `TenantIsolationEnroll_Holds` red | R1 test run | Expected red until L56. Not a boundary item | ledger S-L12 |
| R1: `Hunter_SameSeed_SameFirstOpList` timeout in the R10-1 worknote | R1 not fixed | Record. Section 6 step 2 reruns it alone once if it times out | section 6 |
| R2: chaos `Outcome()` passes with zero loops or all loops errored | R2 not fixed | Park as KG-W5-20, in L29b's scope. Not live: runs 9 and 10 had 152 and 147 loops, `workload_errors` 0 | L29b |
| R2: ledger rows R10-1, L27c and L27b stale | R2 not fixed | Fixed in this ruling's ledger edit | README |
| R2: `wf-r7.md` and `wf-r8.md` uncommitted; `ml-l27b.md` describes `_cutSinceRestart` | R2 not fixed | `ml-l27b.md` gets a superseded note in R11-1 (f). `wf-r7` and `wf-r8` go to the close-out doc sweep | R11-1 (f); doc sweep |
| R2: `I1FromThirdOp` shrink test unverified | R2 not fixed | Covered by section 6 step 1 (the non-Docker suite runs it) | section 6 |
| R3-N1: README and `--port` text promise a free port | R3 fix note | Apply now, with D-ML139's wording | R11-1 (b) |
| R3: widen `IsDaemonEndpoint` to the named port? | R3 decision | **D-ML139: no.** Architect call, within the lab's own security model | D-ML139 |
| R3: no test runs `LabCli` against a real daemon | R3 seam gap | The confirmation run gets a live CLI probe (section 6, segment 1, steps 1-3) | section 6 |
| R3: `ApplyChildEnvironment` call not pinned by a test | R3 info | Covered by A4 live. No action | section 6 A4 |
| H1: flake check `rows` echoed unbounded | security Low | Apply now | R11-1 (d) |
| H2: repro read has no size cap and no IO catch | security Low | Apply now | R11-1 (e) |
| H3: `LabBuilder.RunAsync` keeps `MERIDIAN_LAB_TOKEN` in the child environment | security Low | Park in L54b (mutant and build area, before the wave 10 gates). Safe today because every caller is the daemon, which has already cleared its token | L54b |
| H4: `TryReadDiffGitPath` does not require the `a/` and `b/` prefixes | security Low | Park in L54b. Patches are repo-owned and the scope check fails closed | L54b |
| "security-review label needed" | R1, R2, R3, security | Orchestrator applies the label on the lab PR. R11-1 gets one `security-advisor` pass | R11-1 |

## 3. Decisions

- **D-ML139 (Accepted, architect).** `LabHosts.IsDaemonEndpoint` keeps accepting only `127.0.0.1:8789`. The narrowing is deliberate: the CLI must never send the daemon bearer to another loopback port, where a lab service or a proxy listens. `daemon run` may still bind another `Lab:Port` for tests, but the CLI cannot reach such a daemon. The README and the option texts must say so (R11-1 b). MCP clients that call `/mcp` directly are not affected. If the owner later wants a free port, the overload described in R3's report is the route, and it would need a test against an ephemeral-port daemon.
- **D-ML140 (Accepted, architect; amends D-ML137).** The confirmation run is section 6 of this note, and nothing else:
  - every non-Docker suite uses `--filter "RequiresDocker!=true"` (F123);
  - the CLI probe is added;
  - the R9-1 probe checks the bundle's `rows/<section>/<row>.json` files, not `rows.jsonl`;
  - the run brief quotes section 6 verbatim and cites no earlier procedure.
- **D-ML141 (Accepted, architect).** Park dispositions from section 2:
  - KG-W5-20 and the soak `rows` pre-check go to L29b;
  - H3 and H4 go to L54b;
  - pre-R10-1 rows are not migrated (KG-W5-18);
  - `FlakeCheckTool` gets no direct unit test while the live probe stands in for it.

  D-ML138 was carried out on option A: R10-1 (`1ce5584a`) and L27c (`0c1900b3`) merged before pass 4. The owner can still ratify it.

## 4. Finding F123 (prescription defect, F118 class)

- **What is wrong.** `--filter "Category!=Docker"` excludes nothing. No test in `tests/Dhadgar.Lab.Tests` has a `Category` trait. Docker tests carry `[Trait("RequiresDocker","true")]` (16 files) and `[Collection(LabDockerGate.Name)]`, and CI filters on `RequiresDocker!=true` (`.github/workflows/_dotnet-build-test.yml:26`).
- **Where it came from.** Run 9 ruling (line 86) and run 10 ruling §6 step 1 and §7 used the bad filter. The R10-1 and L27c worknotes followed them.
- **What it caused.** Those "non-Docker" runs ran every Docker test, including the expected reds S-L12 and S-L15. That explains the "2 failed, environmental" lines in `ml-wf-r10-seed-u64.md` and `ml-l27c.md`.
- **The fix.** The correct filter is `RequiresDocker!=true`, and section 5 and section 6 use it. The other occurrences in the ledger are history and go to the close-out doc sweep. They are not rewritten here.

## 5. Packet R11-1: wave 5 boundary pass 4 fixes

- **Branch, worktree, commit.** Branch `ml/wf-r11-boundary4-fixes` from the campaign tip (`0c1900b3` or later). Worktree `.claude/worktrees/ml-wf-r11-boundary4`. Commit subject `fix(lab): apply wave 5 boundary review pass 4 fix notes [R11-1]`.
- **Security review:** yes, one `security-advisor` pass. The packet touches lab tool input handling (`FlakeCheckJob`, `HuntModelTool`) and the CLI's documented token target.
- **Size:** small, about 25k tokens. One Sonnet `packet-coder`.
- **Ruling check:** one fresh `packet-reviewer` and one `security-advisor`. If either is not `ship`, it becomes a BlockedDecision.

### Rules (inline; `packet-coder` skips CLAUDE.md)

- **R-RESULT:** validation and IO failures return `Result<T>` with a `[Lab.<Code>]` message. Never throw for them.
- **R-PATH:** every file path goes through `IPathValidator`. Do not add a raw `Path.Combine` on user input. The repro path is already validated, so keep that line.
- **R-USING:** `using` or `await using` on every `IDisposable`.
- **R-CPM:** no package versions in `.csproj`, and no new package.
- **Build rules:** nullable is enabled, and CI builds with warnings as errors. Do not add `!` to silence a warning that has a real null path.
- **Tests:** xUnit tests are named `Method_Case_Result`. Use the conventions of the existing file you add to.
- **Never log or echo** a lease id, a token or caller-supplied text in an error message (H1 is exactly this).
- **No `restore -r` and no `publish -r`.** If a `packages.lock.json` changes, revert it and report it.
- **Edit only the files listed below.** If a step's precondition is false, stop and report `blocked`.

### Changes

**(a) R1-N1: validate every top-level shipped spec.**

In `tests/Dhadgar.Lab.Tests/Uat/ShippedSpecsRegistryValidateTests.cs`:

- Replace line 44, so the file list is every top-level `*.toml` except `known-issues.toml`, ordered:

  ```csharp
  var files = Directory.EnumerateFiles(specs, "*.toml", SearchOption.TopDirectoryOnly)
      .Where(f => Path.GetFileName(f) is not "known-issues.toml")
      .Order(StringComparer.Ordinal)
      .ToList();
  ```

- Keep the catalog block as it is.
- After the loop, next to the existing `files.Count` assert, add:

  ```csharp
  var names = files.Select(f => Path.GetFileName(f)).ToList();
  Assert.Contains("lab-selftest.toml", names);
  Assert.Contains("enroll-probe.toml", names);
  ```

  These two asserts are what make a revert fail. The old list never held `enroll-probe.toml`.
- In the class summary (line 13), change "(`lab-selftest.toml` and `catalog/*.toml`)" to "(every top-level `*.toml`, today `lab-selftest.toml` and `enroll-probe.toml`, and `catalog/*.toml`)".
- If the test goes red on an `enroll-probe.toml` line, stop. Report `blocked spec` with the failure lines. Do not edit the spec or the test's expectations.

**(b) R3-N1: honest port documentation (D-ML139).**

- `src/Tools/Dhadgar.Lab/README.md` line 273: replace the first sentence with:

  > Both commands talk only to `http://127.0.0.1:8789` (`LabDefaults.DaemonPort`). `--port` defaults to 8789; any other port is refused as `[Lab.Refused] the target is not a lab endpoint` (`LabHosts.IsDaemonEndpoint`, D-ML139), so the CLI cannot reach a daemon started with another `Lab:Port`. MCP clients that call `/mcp` directly are not affected.

  Keep the token, redirect and proxy sentences.
- `src/Tools/Dhadgar.Lab/Cli/LabCli.cs` lines 51 and 67: change `Description = "Daemon port on 127.0.0.1."` to `Description = "Daemon port on 127.0.0.1; only 8789 (LabDefaults.DaemonPort) is accepted."`. Change nothing else on those lines.
- `src/Tools/Dhadgar.Lab/LabHosts.cs` doc comment of `IsDaemonEndpoint` (lines 50-53): replace "The daemon's own /status" wording with "The CLI's loopback calls (`status`, `uat run`) and the self-check recovery's calls to its child daemon use this". This is a comment-only change.

**(c) README: `enroll-probe.toml`.**

In `src/Tools/Dhadgar.Lab/README.md`, after the `lab-selftest.toml` paragraph (line 284), add one paragraph:

> The shipped spec `docs/guides/uat-specs/enroll-probe.toml` (section `enroll-probe`, one row `enroll-gateway` with no `depends_on`) starts nothing: it mints a token and enrolls once through the Gateway against a stack its caller already started. It is the M5 mutant's detector and the chaos `nodes-enroll` workload (D-ML123).

**(d) H1: bounded flake check `rows`, no echo.**

In `src/Tools/Dhadgar.Lab/Results/FlakeCheckJob.cs`:

- Add to the class:

  ```csharp
  private const int MaxRowArgLength = 200;
  private static readonly Regex RowArgPattern =
      new(@"^[a-z0-9-]+(/[a-z0-9-]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
  ```

  Add `using System.Text.RegularExpressions;`. The grammar is `SpecValidator`'s id grammar, `^[a-z0-9-]+\z`, with an optional `/row`. Spec ids cannot hold anything else.
- In `PlanAsync`, directly after the `reruns` check (line 65) and before `BundleFolders.Find`, add:

  ```csharp
  if (rowIds is not null && rowIds.Any(id => id is null || id.Length > MaxRowArgLength || !RowArgPattern.IsMatch(id)))
  {
      return Result<FlakeCheckPlan>.Failure("[Lab.InvalidArgument] a rows entry is not a row id or section/row key");
  }
  ```

- The existing NotFound and ambiguity messages at lines 103 and 108 may keep echoing `rowId`, because only validated ids reach them.
- **Tests** go in `tests/Dhadgar.Lab.Tests/Jobs/FlakeCheckJobTests.cs`. Use the same bundle setup as `PlanAsync_RowsAsKey_SelectsThatRow`, so the run exists, and add:
  - `PlanAsync_RowArgTooLong_IsInvalidWithoutEcho`. Rows entry: `new string('a', 201)`.
  - `PlanAsync_RowArgWithNewline_IsInvalidWithoutEcho`. Rows entry: `"heal\nx"`.

  Each asserts `IsFailure`, that the error starts with `[Lab.InvalidArgument]`, and that the error does not contain the input. **Why reverting fails:** without the check, the 201-`a` id reaches line 103 and comes back as `[Lab.NotFound] row aaa…`. The prefix assert fails, and so does the no-echo assert.

**(e) H2: capped, caught repro read.**

In `src/Tools/Dhadgar.Lab/Tools/Hunt/HuntModelTool.cs`:

- Add to the class:

  ```csharp
  internal const long MaxReproBytes = 64 * 1024;

  internal static async Task<Result<string>> ReadReproAsync(string path, CancellationToken ct)
  {
      try
      {
          var info = new FileInfo(path);
          if (!info.Exists)
          {
              return Result<string>.Failure("[Lab.NotFound] repro not found");
          }

          if (info.Length > MaxReproBytes)
          {
              return Result<string>.Failure("[Lab.InvalidArgument] repro is larger than 64 KiB");
          }

          return Result<string>.Success(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
          return Result<string>.Failure("[Lab.InvalidArgument] repro could not be read");
      }
  }
  ```

- In `ReplayAsync`, replace lines 128-133 (the `File.Exists` block and the `ReadAllTextAsync` line) with:

  ```csharp
  Result<string> text = await ReadReproAsync(reproPath.Value!, ct).ConfigureAwait(false);
  if (text.IsFailure)
  {
      return Result<object>.Failure(text.Error);
  }
  ```

  Then pass `text.Value!` to `ReproParser.Parse`.
- `InternalsVisibleTo("Dhadgar.Lab.Tests")` is already declared (`Secrets/SecretVault.cs:5`).
- **Tests** go in a new file, `tests/Dhadgar.Lab.Tests/Hunt/HuntReproReadTests.cs`. Each test uses a temp folder, deleted in `finally`.
  - `ReadReproAsync_LargerThanCap_IsInvalid`: a file of `MaxReproBytes + 1` bytes returns a failure starting `[Lab.InvalidArgument]`.
  - `ReadReproAsync_Missing_IsNotFound`.
  - `ReadReproAsync_SmallFile_ReturnsText`.

  **Why reverting fails:** without the cap there is no `ReadReproAsync` method, so the tests do not compile. That is acceptable, because the oversize test pins the cap. Do not write a locked-file test; it is not portable.

**(f) Superseded note.** Append to `docs/analysis/meridian-lab/worknotes/ml-l27b.md`:

> Superseded in part by L27c (`0c1900b3`): `_cutSinceRestart` is now `_needsRestart`, which starts true for a new hunter; see [ml-l27c](ml-l27c.md).

**(g) Worknote.** Write `docs/analysis/meridian-lab/worknotes/ml-wf-r11-boundary4.md`. It records what changed, the check outputs, and any `blocked spec` lines from (a).

### Files

| File | Change |
| --- | --- |
| `tests/Dhadgar.Lab.Tests/Uat/ShippedSpecsRegistryValidateTests.cs` | (a) |
| `src/Tools/Dhadgar.Lab/README.md` | (b), (c) |
| `src/Tools/Dhadgar.Lab/Cli/LabCli.cs` | (b), two description strings |
| `src/Tools/Dhadgar.Lab/LabHosts.cs` | (b), doc comment only |
| `src/Tools/Dhadgar.Lab/Results/FlakeCheckJob.cs` | (d) |
| `tests/Dhadgar.Lab.Tests/Jobs/FlakeCheckJobTests.cs` | (d), two tests |
| `src/Tools/Dhadgar.Lab/Tools/Hunt/HuntModelTool.cs` | (e) |
| `tests/Dhadgar.Lab.Tests/Hunt/HuntReproReadTests.cs` | (e), new |
| `docs/analysis/meridian-lab/worknotes/ml-l27b.md` | (f), append only |
| `docs/analysis/meridian-lab/worknotes/ml-wf-r11-boundary4.md` | (g), new |

### Checks, in order

Docker is not needed.

```powershell
dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ShippedSpecsRegistryValidateTests|FullyQualifiedName~FlakeCheckJobTests|FullyQualifiedName~HuntReproReadTests"
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
git grep -n "only 8789" -- src/Tools/Dhadgar.Lab
```

- The last command must show the README line and both `LabCli.cs` lines.
- A failure in the full non-Docker run that the packet did not cause is recorded with its test name and is not fixed. Known examples are `SingleInstanceGuardTests` under a concurrent lab run, and a `BarrierProxyTests` port held by Docker Desktop.

### Docs owed

- The README edits in (b) and (c).
- The worknote (g).
- No `docs/PROJECT-STATE.md` change: nothing becomes "confirmed working" here.

## 6. The confirmation run (D-ML140)

- **When to run.** On the campaign tip after R11-1 merges.
- **The run brief** quotes this section verbatim and cites no earlier procedure (F118, F123).
- **Run rules:**
  - Each Part B segment gets a fresh daemon on a fresh home.
  - Teardown is label-only.
  - No `git worktree prune`, no `restore -r` and no `publish -r`.
  - Never print or log the lab token or a lease id.
  - A red is recorded and parked, not fixed (D-ML126).
  - Write the record to `worknotes/wave5-docker-run.md` as Continuation 11, and the report to `.claude/reports/meridian-lab/wave5-docker-run-11.md`.

### Step 0: preconditions

- No lab daemon is running and port 8789 is free (`Get-NetTCPConnection -LocalPort 8789` returns nothing).
- The single-instance mutex is free (A4 needs it).
- Docker Desktop is running.
- `docker ps -a --filter label=meridian-lab=1` shows 0 containers.

### Step 1: builds and the non-Docker suite

```powershell
dotnet build src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
```

- Use `RequiresDocker!=true`. **Not** `Category!=Docker`, which excludes nothing (F123).
- Record passed, failed, skipped and total.
- If `ModelHunterTests.Hunter_SameSeed_SameFirstOpList` (or any hunter unit test) times out, rerun that one test alone, once, with `--filter "FullyQualifiedName~Hunter_SameSeed_SameFirstOpList"`. Record both results.
- Check that `I1FromThirdOp` ran and passed (R2 not-fixed item 4).

### Step 2: Part A, serial, once each unless noted

Each runs as `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "<filter>"`:

| ID | Filter |
| --- | --- |
| A1 | `FullyQualifiedName~HunterDockerTests` |
| A4 | `FullyQualifiedName~SelfCheckRecoveryTests` |
| A5 | `FullyQualifiedName~SelfCheckE2ETests` |
| A6 | `FullyQualifiedName~LabSelfTestE2ETests` |
| A10 | `FullyQualifiedName~ToxiproxyTimeoutToxicDockerTests` |
| A11 | `Seam=S-L7`, run **twice** |

- A red in A11 makes KG-W5-15 live again.
- A4 red is KG-W5-7.

### Part B: daemon setup, used by each segment

1. Create a fresh home under the scratchpad: `...\scratchpad\r11-home-<n>`.
2. Create a token with `dotnet <lab bin>\meridian-lab.dll token new`. Write it to a scratchpad file and never print it.
3. Start `meridian-lab daemon run` on port 8789. Set `MERIDIAN_LAB_HOME` to the home, `MERIDIAN_LAB_TOKEN` from the file, and `Lab__RepoRoot` to the campaign worktree. Set these on the daemon process only.
4. Acquire a lease, then run `lab_build`, `stack_up` with `wipe=true`, `svc_start nodes` and `svc_start gateway`.
5. Release the lease before calling any OwnLease tool (`uat_run`, `results_flake_check`, `hunt_model_run`).

### Segment 1: CLI probe and R9-1 flake-check probe (fresh daemon)

**CLI probe.** Run these from a shell with `MERIDIAN_LAB_TOKEN` set from the file. Never echo it.

1. `meridian-lab status --json`: exit 0, and the body's pid is the daemon's pid.
2. `meridian-lab status --port 9000`: exit 1 with `[Lab.Refused] the target is not a lab endpoint`. No request is made.
3. `meridian-lab uat run --specs <worktree>\docs\guides\uat-specs\lab-selftest.toml --plan-only --json`: exit 0, and a plan with no bundle path. Record the exit code and the first 20 lines.

**R9-1 probe.**

4. With no lease held, call MCP `uat_run` with `spec` `lab-selftest.toml`, `seed` `12499590173714643239` and no rows filter.
   - This must be a plain run, **not** a `mutation_run` detector run (KG-W5-19).
   - Record `run_id`, `overall`, and the grade of each row file under the bundle's `rows\lab-selftest\` folder. Bundle rows are files at `rows/<section>/<row>.json`; there is no `rows.jsonl`.
5. Call `results_flake_check` with `run_id` from step 4, `rows` `["enroll-gateway"]`, `reruns` `1` and no `lease_id`. Follow its `job_id` with `job_status` until the state is terminal.
6. Call `results_flake_check` again on the same `run_id`, with `rows` `["lab-selftest/enroll-gateway"]` and `reruns` `1`. Follow it the same way.
7. Call `results_query`, view `runs`, with filter `run_id` set to step 4's run, then with each rerun's `run_id` from the job results' `runs` list. Record `seed`.

**Pass**, all of these:

- Steps 1-3 behave as stated.
- Steps 5 and 6 each return a `job_id`. Neither returns `[Lab.InvalidArgument]` or `[Lab.NotFound]`.
- Each job ends `succeeded`. Its result has:
  - `seed` = `12499590173714643239`;
  - `rows` = `["lab-selftest/enroll-gateway"]`;
  - `state` `stable`;
  - outcome `pass`.
- Each rerun bundle's `rows\lab-selftest\` folder holds exactly `stack-up.json`, `nodes-up.json`, `gateway-up.json` and `enroll-gateway.json`. That is the `depends_on` closure (R9-1). All four are `PASS`. No other row file is present, because the filter omits unselected rows (`UatRunner.cs:209-212`).
- Every `results_query` seed is `12499590173714643239`: not 0, not negative.

**Partial:** if step 4's `enroll-gateway` is not `PASS`, still run steps 5-7. Seed, rows, the closure file set and the query seed are still judged. `state` and outcome are recorded as UNVERIFIED.

**Red:**

- Any failed check from steps 5-7 keeps KG-W5-16 live, parked.
- A query seed of 0 keeps KG-W5-18 live.
- A step 1-3 failure is a new finding for the boundary-close ruling.

### Segment 2: hunt replay consistency probe (fresh daemon)

1. With no lease held, call `hunt_model_run` with `model` `enrollment`, `seed` `4242` and `budget_s` `300`. Follow it with `job_status` until it ends, and record `state`, `first_diverging_op` and `detail`.
2. If the hunt ended `diverged`, call `hunt_model_run` with `replay` `<job_id>/repro/repro.toml`. The path is relative to the jobs folder: the file is `<home>\jobs\<job_id>\repro\repro.toml`. Run it on the same daemon and record the replay payload's `state` (`reproduced` or `not_reproduced`), `first_diverging_op` and `detail`.
3. Stop the daemon, then check that port 8789 is free.
4. Run the Part B setup for a new daemon with a new home (`r11-home-<n+1>`).
5. Copy `<old home>\jobs\<job_id>\repro\` to `<new home>\jobs\<job_id>\repro\`. Then call `hunt_model_run` with the same `replay` value and record the same fields.
6. For each replay, record the audit lines from its first `fault_reset` to its `stack_reset_data`.

**Pass**, either of these:

- The hunt ends `passed`.
- The hunt ends `diverged`, both replays are `reproduced` at the same `first_diverging_op`, and each replay's audit shows, in order:
  1. `fault_reset`;
  2. `svc_restart` (nodes);
  3. `nodes_token_mint` with label `hunt-warmup`;
  4. a `nodes_enroll_probe` via `nodes` answered 201;
  5. `stack_reset_data`.

This is early evidence for KG-W5-13. The gate itself stays with the campaign review's live hunt (D-ML127).

**Red:** KG-W5-13 stays live, parked, and the record gets the audit excerpt. A product 500 at the cut is F114's product half (NR-1); record it and do not chase it.

### Teardown

1. Acquire a lease, run `stack_down` with `wipe=true`, then release the lease.
2. Stop the daemon.
3. Check all of these:
   - `docker ps -a --filter label=meridian-lab=1` shows 0 containers;
   - the lab network is absent;
   - port 8789 is free.

Remove only labelled lab containers, and only if any are left. Never prune.

### Not rerun

Gate MD, twin, chaos, fuzz, soak and coverage are not rerun. R11-1 does not touch their code, and runs 9 and 10 met them.

## 7. Closing the boundary

The wave 5 and 5E boundary closes when all of these hold:

1. R11-1's ruling check is `ship` from both the `packet-reviewer` and the `security-advisor`, and R11-1 is merged into the campaign branch.
2. Section 6 has run. Every red is either a pass or covered by a parked KG: KG-W5-7, -13, -15, -16, -17, -18 or -20, or the expected S-L12 red (L56).
3. The architect has written the boundary-close ruling, which records which KGs the run closed. KG-W5-16 and KG-W5-18 close on a full segment 1 pass.

After that, L19n (seam S-L15) runs on a fresh daemon, as D-ML132 says. Then NR-1 goes to its PR.
