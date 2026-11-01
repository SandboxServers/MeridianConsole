# Wave 5 boundary run 8: diagnosis and proposed fix wave R8

Architect, 2026-11-01. A read-only diagnosis of the run 8 red gates, made from the recorded evidence. No Docker or lab tool was run, and the lab token file was not read.

Sources:
- the run record `.claude/reports/meridian-lab/wave5-docker-run-8.md`;
- `worknotes/wave5-docker-run.md`, Continuation 8;
- the Part B lab home `<S>\r8-home-B`: `audit\audit-20261101.jsonl`, `logs\nodes.jsonl`, `uat-runs\`, `jobs\`;
- the scratchpad files `r8-A11.log` and `r8-B-*.json`.

`<S>` is the session scratchpad, `C:\Users\Steve\AppData\Local\Temp\claude\C--Users-Steve-source-projects-MeridianConsole\ccdea26c-3b56-47f8-a99f-6e960555bca4\scratchpad`.

## Verdict

The R7 fixes hold. M1 and M4 are `detected` in Part B, and A3 passes, so F103 and F106 are closed. The hunt's restarts outside a cut return ok, so F104 is closed. Fuzz completes, so F107 is closed.

Run 8 shows three new lab defects, one run-procedure deviation, and one known product defect. None is a product regression:

- **F109 (hunt):** the hunt sends a second `fault_proxy_disable` while the first cut is still active. `FaultBook` refuses it, and the hunter treats the refusal as an infrastructure error. This is the same class as F104, one op over.
- **F110 (M5, chaos):** a `uat_run` `rows` filter keeps `enroll-gateway` but drops the rows it depends on, so the row is SKIPPED every time. M5's detector has never run its enroll. The chaos PASS ran 251 workload loops with no enroll in any of them.
- **F111 (twin, M5 signature):** run 8 used one daemon for all of Part B and ran M5 in the same `mutation_run` as M4, which D-ML121 forbids. M4's intended I1 at 20:15:35 then reaches every later run through the run-start checkpoint (KG-W5-4).
- **F112 (A11):** this is most likely a Postgres first-start race. `pg_isready` over the container's Unix socket answers "ready" while the image's temporary init server is still running, and the first `CREATE DATABASE` then hits its shutdown. It cannot be proven from the record: the lab drops psql's stderr, and the container is gone. This is a flake made possible by a lab readiness defect.
- **KG-W5-1 (fuzz):** 28 of the 50 cases got 500 at the binding stage. This is the known `ProblemDetailsMiddleware` defect, owner decision D-ML107. It is not new.

**Do reds 1, 3 and 4 share a cause with the fuzz 500s? No.**
- The fuzz ran last (20:36-20:37), after every other gate.
- The `inv:I1|nodes|unhandled` in the twin and M5 is the M4 mutant's own planted defect: three "Unhandled exception (fallback handler)" lines at 20:15:35.254, trace `6061a215…`, inside M4's detector run `bf7df5c6`.
- That is the same middleware producing the same 500, so the signature matches. The cause differs, though: M4's patch removes the concurrency catch, while the fuzz 500s are body-binding failures.
- The hunt (red 1) has no I1 and no 500 involved.

## Gate by gate

| # | Gate | Class | Cause | Evidence | Coder reads code first? |
| --- | --- | --- | --- | --- | --- |
| 1 | hunt seed 4242 `Lab.Unavailable` after 300 s | Lab defect (F109) | A generated `PgDown` op runs while `model.PgDown` is already true. The second `fault_proxy_disable` on `nodes-postgres` clashes with the active fault, and `FaultBook` returns `[Lab.FaultActive]`. `CallAsync` maps every tool error to `[Lab.Unavailable] fault_proxy_disable did not complete`, so the case ends as infrastructure. CsCheck then idles to the budget (F104's second half, still out of scope). Run 7 died one op earlier, at the restart, which hid this. | Audit, job 770e5546d09e: case reset 20:09:30.541; `fault_proxy_disable` ok 20:09:30.624; state checks; a second `fault_proxy_disable` 20:09:33.083 returns an error in 62 ms; `fault_reset` 20:09:33.117 (the `finally` heal). No hunt call follows until the job fails at 20:14:12. Two `svc_restart` calls outside a cut (20:09:24, 20:09:29) are ok, so R7-3 works. Code: `Hunt/ModelHunter.cs:338-352` (no state guard; compare `:366-371`); `Faults/FaultBook.cs:173-189`; `Hunt/EnrollmentModel.cs:143-145` (the model treats the op as idempotent; the run does not). | No. |
| 2 | fuzz 28/50 status 500 | Product defect, known (KG-W5-1, D-ML107) | Malformed bodies fail minimal-API binding. Nodes throws `BadHttpRequestException` ("Failed to read parameter `EnrollNodeRequest request` from the request body as JSON"), and `ProblemDetailsMiddleware` catches every exception and answers 500. A body that binds gets 400. The two `boundary:*` cases are consistent with a string replacing the `hardware` object (a type mismatch), but that is not verified per case. | `<S>\r8-B-13-fuzz-status.json`: sent 50, known 8 (KI-F69-HTTP, 4xx without problem+json), 28 failures, all signature `http:500\|-\|POST\|/api/v1/agents/enroll\|none`. By kind: truncated-json 8, swap-type 6, duplicate-key 5, empty-body 5, numeric-extreme 2, boundary 2 (`empty`, `control`). Nodes logs "Unhandled exception (fallback handler)" in the 20:36-20:37 window, and the 500s answer in 0-3 ms, before any handler work. The kinds and the exception match the earlier live repro, `.claude/reports/meridian-lab/wave5-diag-nodes.md`. Code: `src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs`; `Fuzz/Mutators.cs` (`boundary` sets a string on a random top-level property). The job `succeeded`, which is all segment 5 asks. | n/a (no lab packet) |
| 3 | twin `enroll-latency-invariance` held null | Lab procedure (F111) through KG-W5-4 | The base and the variant both carry M4's 20:15:35 I1 in their run-start checkpoint, so the twin precondition fails. The twin's own enrolls worked. | `<S>\r8-B-09-twin.json`: base `16f315` (20:16:08-20:17:18) and variant `6c8b2a`, each `signature_texts ["inv:I1\|nodes\|unhandled"]`, diffs `[]`. Both `invariants.json` files list exactly three I1 at 20:15:35.254, trace `6061a215…`, which is M4's detector run. Nodes logs a "marked as used" line at 20:16:24. Run 8 had one daemon (pid 38092) for all of Part B, against D-ML121. | No. |
| 4 | M5 `detected_other` `inv:I1\|nodes\|unhandled` | Lab defect (F110) plus F111 | (a) The detector `uat_run lab-selftest.toml rows=["enroll-gateway"]` filters out `gateway-up`, so `RowExecutor` SKIPs the row ("depends on gateway-up"). No token is minted or enrolled, so the planted `LogInformation("Enrollment token {Token}", …)` never runs and I4 cannot fire. (b) The only signature left is M4's I1, again through the run-start checkpoint. M5 was `detected_other` the same way in run 7, so it has never truly been detected. | Detector run `06082a`, 20:15:51.13-20:15:52.16 (1 s): row `enroll-gateway` SKIPPED "depends on gateway-up"; violations are the three 20:15:35 I1 only. Code: `tools/lab/mutants/M5-token-logged/mutant.toml:8`; `Uat/RowExecutor.cs:87-93`; `Uat/UatRunner.cs:209-212` (the filter drops rows with no dependency check); `docs/guides/uat-specs/lab-selftest.toml:174` (`depends_on = ["gateway-up"]` → `nodes-up` → `stack-up`, which is `stack_up wipe=true`). Note that "add the dependencies to the filter" is wrong: `stack-up` wipes the stack and would remove the mutant Nodes that `mutation_run` started. | No. |
| 4b | chaos `nodes-enroll` PASS | Lab defect (F110), informational | The workload has the same filter, so `enroll-gateway` was SKIPPED in all 251 loops. "workload_errors 0, violations 0" exercised no enroll at all. | `docs/guides/uat-specs/chaos/nodes-enroll.toml:9`. In the chaos window (20:17:25-20:27:27) the audit has 251 `uat_run` calls and no `nodes_token_mint` or `nodes_enroll_probe` from them; the window's only probes are the twin's. Every chaos `uat-runs` bundle lasts about 0.9 s and is `fail` on the stale 20:15:35 I1. Likely the same in run 7. | No. |
| 5 | A11 S-L7 `could not create meridian_lab_nodes` | Environment flake, enabled by a lab readiness defect (F112); medium confidence | The failure is at `stack.UpAsync(wipe: true)` (`ToxiproxySeamTests.cs:96-97`). In `ProvisionDatabasesAsync` the roles were created and the `pg_database` SELECT for `meridian_lab_nodes` succeeded (it did not return 1), and then `CREATE DATABASE` exited non-zero. The two calls are back to back with no concurrency (the `LabDockerGate` collection, Part A one at a time), so the server changed between them. The official `postgres:17` entrypoint runs a temporary server on the Unix socket only (`listen_addresses=''`) during first-start init, and stops it before the real start. `docker exec … pg_isready -U postgres` uses that socket, so it says "ready" during init. `nodes` is the first database, so it takes the first statement after the stop. | `<S>\r8-A11.log`: fails at 44 s, message only `[Lab.Docker] could not create meridian_lab_nodes`. Code: `Stack/LabStack.cs:663` (socket probe), `:713-729` (SELECT, then CREATE), `:223-224` (`Require` drops stderr), `:38` (`nodes` first). **A leftover from A10 is ruled out:** A10's `finally` runs `DownAsync(wipe: true)` (`ToxiproxyTimeoutToxicDockerTests.cs:138`). A11's `UpAsync(wipe: true)` also removes labelled orphans (`LabStack.cs:844-875`). A surviving Postgres would still hold `meridian_lab_nodes`, so the SELECT would return 1 and the CREATE would be skipped. **A naming race is ruled out:** the name is fixed, and one process runs at a time. Disk pressure on the Docker VM cannot be excluded, because the stderr is gone. The empty `meridian-lab-net` left behind is the best-effort `network rm` at `LabStack.cs:900`, which is harmless. | Yes, one step: re-run A11 three times after the fix. If it fails again, the new error text names the psql cause. |
| 6 | A7 S-L12 `Lab.NotFound` | Expected | There is no such spec until wave 7 (D-ML108). | Run record A7. | No. |
| - | soak 1 loop, 1 sample, PASS-WEAK | Known (KG-W5-2) | Its run `721f84` (20:27:42-20:35:37) carries its own I1s at 20:28:10, 20:30:37, 20:35:20 and 20:35:36. Those are pg-down rows of `catalog/nodes.toml` (the F67 class, KI-F67), not reds. | `uat-runs\…-721f84\invariants.json`. | No. |
| - | coverage 0/72, 0/29, 0/22 | Known (KG-W5-3) | The D-ML108 bar is met: no error, and `invariant_check` hit 1. | Run record. | No. |

## Proposed fix wave R8 (needs D-ML125 option A)

D-ML118 says run 8 is the last run at this boundary. This wave runs only if the owner picks option A of D-ML125. Otherwise the reds are parked as KG-W5-13 to KG-W5-15.

There are three `packet-coder` packets of about 40k tokens each, with disjoint files. Each ships as its own PR into `campaign/meridian-lab`, in the order R8-1, R8-2, R8-3. The order is free because no files overlap. Each packet gets one `packet-reviewer` pass, then the three get one wave review, then run 9. None touches a security-review area: the changes are lab tool code and lab specs only. R8-3 adds stderr text to an error message, but only for statements that carry no credential (see its rule). R8-1 and R8-2 are needed for must-pass items (hunt, gate MD). R8-3 is needed for S-L7.

Repo rules for every packet:
- Use `Result<T>` for validation, IO and network failures; never throw.
- Nullable is enabled, and CI builds with warnings as errors.
- No package versions in a `.csproj` and no new packages (Central Package Management).
- Put `using` on every `IDisposable` and `Process`.
- Every path goes through `PathValidator`.
- Child processes use `ArgumentList`, never a string command line.
- Never put a secret value in an error, a log line or a test assertion message.
- Tests follow `tests/Dhadgar.Lab.Tests`: xUnit `[Fact]` and `[Theory]`, `[SkippableFact]` with `Skip.If…` for platform gates, `[Trait("Seam", "S-Lx")]` on seam tests, sealed test classes, `Assert.True(result.IsSuccess, result.Error)`.

Checks for every packet, in order:

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\<worktree>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\<worktree>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

Each packet writes `worknotes/wf-r8-<n>.md` with three things: what changed, the revert check (the new test red on the old code), and the check output.

### R8-1 hunt-pgdown (F109; hunt)

- Worktree `.claude/worktrees/ml-wf-r8-hunt-pgdown`, branch `ml/wf-r8-hunt-pgdown`. Commit subject: `fix(lab): send no second postgres cut while one is active in the hunt [wf-r8-hunt-pgdown]`.
- Rule (D-ML122): a `PgDown` op while `model.PgDown` sends nothing and changes nothing in the run. The model already treats it as a no-op (`EnrollmentModel.cs:143-145`). `model.Apply` runs after the switch (`ModelHunter.cs:382`), so `model.PgDown` in the switch is the state before the op. Replay and the CsCheck sample share `RunCaseAsync`, so both follow the rule.
- Files:
  1. `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs:338-352`:

     ```csharp
     case HuntOpKind.PgDown:
         if (model.PgDown)
         {
             // D-ML122: the cut is already active; a second fault_proxy_disable clashes in FaultBook (F109).
             break;
         }

         ranPgDown = true;
         Result<JsonElement> cut = await CallAsync("fault_proxy_disable", new JsonObject { ["proxy"] = Proxy }, ct).ConfigureAwait(false);
         ...
     ```

  2. `src/Tools/Dhadgar.Lab/Hunt/EnrollmentModel.cs:28-29`: add "A PgDown while PgDown sends nothing (D-ML122)." to the `PgDown` summary.
  3. `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`. Give `FakeLab` a `DisableClashes` flag. When it is set, a `fault_proxy_disable` while an earlier disable has not been followed by `fault_heal` or `fault_reset` gets an error result, as `FaultBook` does. Read `FakeLab` first and follow how `RestartFails` answers. Then add two tests:
     - `Replay_PgDownInsideCut_IsNotSentTwice`: `DisableClashes = true`, replay `[PgDown, PgDown, PgUp, Mint]`. Assert success, `Diverged == false`, exactly one `fault_proxy_disable`, and exactly one `fault_heal`.
     - `Replay_PgDownAfterHeal_IsSentAgain`: `DisableClashes = true`, replay `[PgDown, PgUp, PgDown, PgUp]`. Assert two `fault_proxy_disable` and two `fault_heal`.
- Revert check: the first test returns `Failure("[Lab.Unavailable] fault_proxy_disable did not complete")`.
- Not in scope: F104's second half (the sample idling to the budget after an infrastructure error) and F108 (the job error keeps only its code).

### R8-2 filter-deps (F110; M5, chaos)

- Worktree `.claude/worktrees/ml-wf-r8-filter-deps`, branch `ml/wf-r8-filter-deps`. Commit subject: `fix(lab): refuse row filters that drop a dependency; give M5 and chaos a standalone enroll probe [wf-r8-filter-deps]`.
- Rule (D-ML123):
  - A `uat_run` whose `rows` filter keeps a row but not one of that row's `depends_on` is refused with `[Lab.InvalidArgument] rows filter keeps <row> without its dependency <dep>`.
  - A filter that names a row not in the spec is refused with `[Lab.NotFound] row <id> is not in the spec`.
  - Mutant detectors and chaos workloads that run against a stack someone else started use a spec whose row has no `depends_on` and no `stack_up` or `svc_*` step.
- Files:
  1. New `src/Tools/Dhadgar.Lab/Uat/RowFilterCheck.cs`: `internal static Result Check(UatSpec spec, IReadOnlyCollection<string> filter)`. For each id in the filter, the row must exist. For each kept row, every `DependsOn` entry must be in the filter. Checking every kept row this way covers transitive dependencies too. The errors are the two above, naming the first offender in file order.
  2. `src/Tools/Dhadgar.Lab/Tools/Uat/UatRunTool.cs:177-181`: after the section-filter check, if `rowFilter is { Count: > 0 }`, call `RowFilterCheck.Check(loaded.Spec, rowFilter)` and return its failure as `Result<object>.Failure`. Twin pre rows go through `UatRunner` (`:283-298`), not this filter, so they are unchanged.
  3. New `docs/guides/uat-specs/enroll-probe.toml`. Copy the header shape of `lab-selftest.toml:1-12`:
     - section `id = "enroll-probe"`, `title = "Enroll probe on a running stack"`, `sequential = true`, `requires = ["docker"]`, `allow_invariants = []`;
     - a comment saying it starts nothing, runs against the stack and services the caller started (`mutation_run`, chaos), and makes 1 enroll-route call per run;
     - one row `enroll-gateway` with no `depends_on`. Its steps and expect are copied from `lab-selftest.toml:176-194`, with the mint label `probe-gw-${run_id}`.
  4. `tools/lab/mutants/M5-token-logged/mutant.toml:8`: `args = { spec = "enroll-probe.toml", rows = ["enroll-gateway"] }`. Keep `expect_pattern`.
  5. `docs/guides/uat-specs/chaos/nodes-enroll.toml:9`: `workload = { spec = "docs/guides/uat-specs/enroll-probe.toml", rows = ["enroll-gateway"] }`. Keep the pacing comment.
  6. Grep `src/` and `tests/` for `rows = [`, `"rows"`, `RowFilter` and `SoakRun(`. List every caller that passes a filter in the worknote. Any shipped or test caller that would now be refused moves to `enroll-probe.toml`, for example `tests/Dhadgar.Lab.Tests/Soak/SoakJobTests.cs:88` if its run reaches `UatRunTool`.
  7. Tests:
     - New `tests/Dhadgar.Lab.Tests/Uat/RowFilterCheckTests.cs`, three cases over `SpecLoader.LoadText` of an inline three-row spec (`a`, `b` depends on `a`, `c` depends on `b`): `["c"]` is refused, naming `b`; `["a","b","c"]` passes; `["zz"]` is `NotFound`.
     - New `tests/Dhadgar.Lab.Tests/Uat/ShippedFilteredRunsTests.cs`. For every `tools/lab/mutants/*/mutant.toml` whose detector tool is `uat_run`, and every `docs/guides/uat-specs/chaos/*.toml` workload, load the named spec and assert `RowFilterCheck.Check(...)` succeeds. Also assert that no kept row has a `stack_up` step. Read the existing mutant and chaos loaders (`MutantSpec`, `ChaosPlan`) and reuse them; do not parse the TOML by hand.
- Revert check: `ShippedFilteredRunsTests` fails for M5 and `nodes-enroll`, and the refusal test passes a filter that should be refused.
- Docs: `docs/guides/uat-specs/` README or guide, if it lists specs (check `docs/meta/doc-update-map.md`). Add one line to the mutant-detector rule in the lab guide: a detector spec starts nothing.

### R8-3 pg-ready (F112; S-L7)

- Worktree `.claude/worktrees/ml-wf-r8-pg-ready`, branch `ml/wf-r8-pg-ready`. Commit subject: `fix(lab): probe postgres readiness over tcp and keep the psql error on database provisioning [wf-r8-pg-ready]`.
- Rule (D-ML124):
  - The Postgres readiness probe uses TCP inside the container, which the image's init-time temporary server does not listen on.
  - A failed database-provisioning statement keeps psql's first `ERROR:` or `FATAL:` line in its error. Role statements never do, because psql can echo the statement and that would expose the expanded password.
- Files:
  1. `src/Tools/Dhadgar.Lab/Stack/LabStack.cs:663`: `["exec", ContainerName("postgres"), "pg_isready", "-h", "127.0.0.1", "-p", "5432", "-U", SuperUser]`, with a comment: "TCP, not the socket: the image's first-start init server listens on the socket only, and stops before the real start (F112)."
  2. `LabStack.cs`, next to `Require` (`:223-224`): add `private static Result<DockerOutput> RequirePsql(Result<DockerOutput> call, string message)`. On a non-zero exit it returns `[Lab.Docker] {message}: {detail}`. `detail` is the first `StdErr` line that contains `ERROR:` or `FATAL:`, trimmed, with control characters removed and cut to 200 characters. With no such line it is just `[Lab.Docker] {message}`. Use it for the four database calls at `:713-746` (SELECT, CREATE, GRANT CONNECT, schema grants). Leave `EnsureRoleAsync` (`:759-778`) on `Require`, with a comment: "never RequirePsql here: psql can echo the CREATE ROLE line with the expanded password."
  3. `tests/Dhadgar.Lab.Tests/Stack/LabStackTests.cs`. Give `FakeDocker` (`:214-234`) an optional `Func<IReadOnlyList<string>, DockerOutput?> Answer`, consulted first, so the default answers stay unchanged. Then add:
     - `UpAsync_PostgresReadiness_ProbesOverTcp`: the `pg_isready` call contains `-h`, `127.0.0.1`.
     - `UpAsync_CreateDatabaseFails_ErrorNamesThePsqlError`: answer the call whose args contain `CREATE DATABASE meridian_lab_nodes OWNER lab_owner` with exit 1 and stderr `psql: error: connection to server on socket "/var/run/postgresql/.s.PGSQL.5432" failed: FATAL:  the database system is shutting down`. Assert the error contains `could not create meridian_lab_nodes` and `the database system is shutting down`.
     - `UpAsync_CreateRoleFails_ErrorCarriesNoStderr`: the role `sh -c` call answers exit 1 with stderr `ERROR:  syntax error\nLINE 1: CREATE ROLE lab_owner LOGIN PASSWORD 'pw-sentinel'`. Assert the error does not contain `pw-sentinel` or `LINE 1`.

     Make the role-presence SELECT return an empty stdout so the create path runs; read how the existing fake answers it.
- Revert check: the first two tests fail (no `-h`; the error ends at the database name).
- Live check (in run 9, not in the packet): A10, then A11 three times in a row.
- Not in scope: the best-effort `network rm` at `:900`, which leaves an empty network when a container detaches slowly. It is harmless and swept by the next wipe.

## Run 9 (only under D-ML125 option A, after the wave review `ship`)

The runner and its rules are unchanged. Each segment starts a new daemon, does `stack_up wipe=true` and starts the services it needs. Write the daemon pid at the start of each segment in the run record; a segment that reuses a pid is invalid (F111). The lab home stays in the scratchpad.

| Segment | Gates | Must show |
| --- | --- | --- |
| A | A10, then A11 (S-L7) three times | All four pass. If A11 fails, its error now names the psql cause. |
| 1 | hunt seed 4242, 300 s, then replay | The job `succeeded`, with a `repro.toml` that replays to the same failure or "none found in budget". The audit has no second `fault_proxy_disable` before a heal or reset, and no `[Lab.FaultActive]`. |
| 2 | `mutation_run` M5 alone | `detected`, `inv:I4\|nodes\|…` in detector run(s) whose `enroll-gateway` row is PASS. `detected_other` counts only with a signature produced inside M5's own window. |
| 3 | twin, chaos `nodes-enroll` 600 s | The twin precondition is not I1. The chaos audit has at least one `nodes_enroll_probe` per workload loop. Both informational (D-ML108). |

M1, M4, soak, coverage and fuzz are not re-run: they met their run 8 bars.

Hard stop: run 9 is the last run at this boundary. A must-pass item still red after it is parked as KG-W5-13 to KG-W5-15, with no further fix wave.

## Owner decisions

- **D-ML125 (BlockedDecision, owner).** D-ML118 made run 8 the last run, with reds parked. Every run 8 red now has a named, small lab cause with a unit test: F109, F110 and F112, plus the procedure deviation F111.
  - **A, proposed default:** allow wave R8 (three packets) and a run 9 limited to the reds, with the same hard stop. Reason: M5 has never been tested, because its detector skips its only row. Parking it would record gate MD as unmet for a lab bug, which is D-ML116's reasoning again.
  - **B:** park now, per D-ML118. The hunt goes to KG-W5-13, M5 and the chaos workload to KG-W5-14, and S-L7 to KG-W5-15. Waves 5 and 5E close with gate MD unmet for M5 and the wave 5 hunt gate unmet. R8-1 to R8-3 move to the campaign-end review (D-ML74).
- **D-ML107 (unchanged, owner).** The fuzz 500s are KG-W5-1. Run 8 adds evidence: 28 of 50, every malformed kind, answered in 0-3 ms. Proposed default unchanged: a product PR to `main` that maps `BadHttpRequestException` to 400 problem+json in `ProblemDetailsMiddleware`. This needs no lab change.
