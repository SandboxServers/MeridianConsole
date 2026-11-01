# Wave 5 boundary run 7: diagnosis and fix wave R7

Architect, 2026-11-01. Read-only diagnosis of the run 7 red gates from the recorded evidence. No Docker or lab tool was run. One read-only `git apply --check` was run in the M1 build tree that run 7 left on disk.

Sources:
- the run record `.claude/reports/meridian-lab/wave5-docker-run-7.md`;
- `worknotes/wave5-docker-run.md`, Continuation 7;
- the Part B lab home `scratchpad\w5r7-home-B` (`audit\audit-20261101.jsonl`, `logs\nodes.jsonl`, `uat-runs\`, `builds\`);
- the scratchpad JSON files `w5r7-B-*.json`.

`<S>` below is the session scratchpad, `C:\Users\Steve\AppData\Local\Temp\claude\C--Users-Steve-source-projects-MeridianConsole\ccdea26c-3b56-47f8-a99f-6e960555bca4\scratchpad`.

## Verdict

Run 7 shows three separate lab defects. Each one has a cause named in the recorded evidence. None of them is a product regression, and none is a detector weakness:

- **Cause B (F103):** the history client sends the enroll body with the lab's snake_case tool options. M4 and M1 (Part A) survive because the enroll race is never reached.
- **Cause A (F104, F105):** the hunt restarts Nodes while Postgres is cut, and Nodes does not start without its database. The hunt fails, and the I1 it leaves behind contaminates the twin and M5 on the same daemon.
- **Cause C (F106):** M1 Part B is `stale` because git hit the Windows MAX_PATH limit in the long scratchpad home.
- **Cause D (F107)**, informational: the fuzz job error is a 1 MiB boundary case the lab sizes too large, so the lab's own HTTP client refuses it.

Items 2 to 5 do not share one root cause. Items 2 and 3a share cause B, item 4 is cause A, item 3b is cause C, and item 5 is cause D. The shape of the request is the common thread for B and D, but they are two different defects in two different files.

## Gate by gate

| # | Gate | Cause | Evidence | Coder reads code first? |
| --- | --- | --- | --- | --- |
| 1 | hunt `Lab.Unavailable` | A: a `RestartNodes` op ran inside a `PgDown` window. Nodes cannot reach Postgres at startup: it logs an unhandled `NpgsqlException` (I1) and never becomes healthy. `svc_restart` times out, and the hunter maps the failure to an infrastructure error. CsCheck then idles to the 300 s budget, because the sample lambda returns at once after an infrastructure error. | Audit: `fault_proxy_disable` 17:17:44 ok; `svc_restart nodes` 17:17:47.628 returned `Lab.Timeout` after 95 402 ms; `fault_reset` 17:19:23; job failed 17:22:44. `uat-runs\…-1e5e60\invariants.json`: I1 nodes "Unhandled exception. Npgsql.NpgsqlException (0x80004005): Failed to connect to 172.23.0.5:20020" at 17:17:52 (the proxy port). Code: `Hunt/EnrollmentModel.cs:149-150` (RestartNodes has no state guard); `Hunt/ModelHunter.cs:55-58` (generator), `:187-200` (the sample keeps running), `:366-371` (failure → `Infrastructure`). | No. |
| 2 | M4 `survived` | B: `HistoryClient.BuildBody` serializes `EnrollNodeRequest` with `LabJson.Options` (snake_case), so the body carries `public_key`. The Nodes minimal API binds with Web defaults (camelCase), so `PublicKey` is empty. `token` is one word, so it binds and the token SELECT runs. The CA then rejects the key and Nodes returns 400 `invalid_public_key` before the token is consumed. With zero 201s the race never happens, so neither mutant can show. | `logs\nodes.jsonl` 17:23:58-59 (M4 window): four "Rejected agent public key for node …: Public key is empty.", then "Failed to issue certificate … invalid_public_key", then `enrollment.failed`, then "HTTP POST /agents/enroll responded 400". The same pattern repeats in all five detector runs. Code: `History/HistoryClient.cs:266`; `Tools/LabJson.cs` (SnakeCaseLower); `Services/CertificateAuthorityService.cs:188-197`. The unit test hides it: `tests/…/History/HistoryClientTests.cs:150-151` deserializes with the same `LabJson.Options`. Correct senders already use Web: `Tools/Nodes/NodesTools.cs:105`, `Fuzz/FuzzTargets.cs:24`. | No. |
| 3a | M1 Part A `survived` | B: the same detector (`history_run enroll-race`, clients 4) and the same `HistoryClient`. | `tools/lab/mutants/M1-token-no-concurrency/mutant.toml` (detector); D-ML115 facts for M4 (4 arrivals in 0.2 ms, 4 × 400, 0 nodes, token unused). | No. |
| 3b | M1 Part B `stale` | C: MAX_PATH. The extracted file path is 268 characters, because the lab home sits in the 147-character scratchpad. `git apply --check` there fails with "Filename too long". `MutantBuilder` maps any git exit to `stale`. M4 (246) and M5 (238) are under 260. Git's `core.longpaths` is unset. Windows `LongPathsEnabled` is 1, so dotnet built M4's 319-character `obj` paths without trouble. Only git is limited. | Re-check in `<S>\w5r7-home-B\builds\mutant-M1-token-no-concurrency-f5f8cdc5\src`: `git apply --check -v …\M1…\change.patch` gives `error: src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs: Filename too long`, exit 1. The M4 patch in the same tree exits 0. Both files and both patches are CRLF on both sides, so line endings are ruled out. Code: `Mutants/MutantBuilder.cs:73,76,193-200`. | No. |
| 4 | twin I1 precondition | A, then KG-W5-4: the twin's run-start checkpoint covers the time since the daemon started. It therefore reads the hunt's 17:17:52 I1. The twin's own enroll returned 201. | `<S>\w5r7-B-09-twin.json`: base 1e5e60 and variant 5525c3 both carry `inv:I1\|nodes\|unhandled` with diffs `[]`; the I1 detail is the hunt's NpgsqlException at 17:17:52, and the rows ran at about 17:29. The twin is not a gate (D-ML108). | No. |
| 4b | M5 `detected_other` | A, then KG-W5-4: M5's `uat_run` at 17:24:21 sees the same 17:17:52 I1. Gate MD would accept `detected_other`, but this signature is not M5's. | Job status `w5r7-B-job-status.json` (run efd92c, `inv:I1\|nodes\|unhandled`). | No. Treat it as UNVERIFIED and re-run it. |
| 5 | fuzz `Lab.InvalidArgument` | D (high confidence; the first test confirms it): the boundary value `1MiB-minus-envelope` assumes a 256-byte envelope. The enroll body's envelope is 312 to 329 bytes for 4 of its 5 top-level properties, so the body is 1 048 632 to 1 048 649 bytes. `LabHttp.SendRawAsync` refuses it ("[Lab.InvalidArgument] body is larger than 1 MiB"), `FuzzJob` aborts the job on any refusal, and `JobRunner.ErrorCode` drops the message (F108). | Envelope arithmetic over the `FuzzTargets.ValidBody` JSON (334 bytes): replacing `token` gives an envelope of 312, `publicKey` 314, `agentVersion` 324, `platform` 329, `hardware` 130. Hit rate: 1/10 kinds × 1/9 boundary values × 4/5 properties ≈ 0.9 % per case, about 36 % over 50 cases. That is consistent with run 3's different seed passing. No tool calls were audited between 17:49:44 and 17:50:45, so the refusal was before any send. Code: `Fuzz/Mutators.cs:43-49,104-108`; `Http/LabHttp.cs:193-195`; `Fuzz/FuzzJob.cs:116-119`; `Jobs/JobRunner.cs:290,416-420`. Fuzz is informational at the boundary (D-ML107). | Yes, one step: write the seed-replay test first and see it red on unchanged code (packet 4). |
| 6 | S-L12 `Lab.NotFound` | Expected red: there is no such spec until wave 7 (L53, D-ML108). | Run record A7. | No. |
| - | coverage 0/72, 0/29, 0/22 | KG-W5-3: these kinds are not collected (L52b). The D-ML108 bar is met: no error, `invariant_check` hit 1. | Run record, coverage line. | No. |
| - | soak 1 loop, 1 sample | KG-W5-2: one `catalog/nodes.toml` `uat_run` took 480 439 ms, longer than the 300 s duration. L29b makes this `weak`. Not a gate (D-ML112). | Audit soak `uat_run` 480 439 ms. | No. |

## Fix wave R7 (wave5-fix-R7)

There are four packets, each a `packet-coder` packet of about 40k tokens, with disjoint files. Each ships as its own PR into `campaign/meridian-lab`. Merge order: R7-1, R7-2, R7-3, R7-4; the order is free because no files overlap. Each packet gets one `packet-reviewer` pass, then the four get one wave review, then run 8. None touches a security-review area: lab tool code only, no product, agent, `CommandEnvelope` or process-manager code. R7-1 keeps the token handling of `HistoryClient` byte for byte. R7-1 to R7-3 are needed for the boundary. R7-4 is informational and may slip without holding the boundary.

Repo rules for every packet:
- Use `Result<T>` for validation, IO and network failures; never throw.
- Nullable is enabled and CI builds with warnings as errors.
- No package versions in a `.csproj` and no new packages (Central Package Management).
- Put `using` on every `IDisposable` and `Process`.
- Every path goes through `PathValidator`.
- Child processes use `ArgumentList`, never a string command line.
- Tests follow `tests/Dhadgar.Lab.Tests`: xUnit `[Fact]` and `[Theory]`, `[SkippableFact]` with `Skip.If…` for platform gates, `[Trait("Seam", "S-Lx")]` on seam tests, sealed test classes, `Assert.True(result.IsSuccess, result.Error)`.

Checks for every packet, in order:

```powershell
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\<worktree>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\<worktree>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

Each packet writes `worknotes/wf-r7-<n>.md`: what changed, the revert check (the new test red on the old code), and the check output.

### R7-1 history-body (cause B; M1, M4)

- Worktree `.claude/worktrees/wf-r7-history-body`, branch `ml/wf-r7-history-body`. Commit subject: `fix(lab): send history enroll bodies in the product wire shape [wf-r7-history-body]`.
- Contract: `Dhadgar.Contracts.Nodes.EnrollNodeRequest(Token, Platform, Hardware, PublicKey, AgentVersion)`, posted to Nodes `POST /agents/enroll` (Gateway `POST /api/v1/agents/enroll`). The minimal API binds it with `JsonSerializerDefaults.Web`, so property names are camelCase: `token`, `platform`, `hardware.cpuCores`, `publicKey`. A missing `publicKey` gives 400 `invalid_public_key`.
- Files:
  1. New `src/Tools/Dhadgar.Lab/Http/LabWireJson.cs`:

     ```csharp
     namespace Dhadgar.Lab.Http;

     /// <summary>The JSON shape product endpoints bind (minimal API Web defaults, camelCase). Only for product request and
     /// response bodies; tool arguments and results use LabJson.Options (D-ML117).</summary>
     public static class LabWireJson
     {
         public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
     }
     ```

  2. `src/Tools/Dhadgar.Lab/History/HistoryClient.cs:266`: serialize with `LabWireJson.Options`. Keep `LabJson.Options` at lines 204 and 316; those are observation lines, not wire.
  3. `src/Tools/Dhadgar.Lab/Tools/Nodes/NodesTools.cs:105`: delete `WireOptions` and use `LabWireJson.Options` at its uses. `src/Tools/Dhadgar.Lab/Fuzz/FuzzTargets.cs:24`: same for `JsonOptions`. This is single-sourcing only, with no behaviour change.
  4. Grep `src/Tools/Dhadgar.Lab` for `SerializeToUtf8Bytes|SerializeToElement|PostAsJsonAsync|JsonContent.Create|ReadFromJsonAsync|Deserialize<` and list every site that writes or reads a product body in the worknote. Change any that uses `LabJson.Options` for a product body. Leave the tool-surface sites alone.
  5. `tests/Dhadgar.Lab.Tests/History/HistoryClientTests.cs:150-151`: `Deserialize` uses a literal `new JsonSerializerOptions(JsonSerializerDefaults.Web)`, not the helper, so a helper regression cannot hide. In `Enroll_Body_IsTheSharedEnrollNodeRequest`, also assert that the raw body contains `"publicKey"` and not `"public_key"`, and that `body.Hardware.CpuCores > 0`. Move `FakeSink` into `tests/Dhadgar.Lab.Tests/History/FakeObservationSink.cs` (internal) so the seam test can share it.
  6. New seam test `tests/Dhadgar.Lab.Tests/Seams/HistoryEnrollSeamTests.cs`, `[Trait("Seam", "S-L2h")]`. It is non-Docker and modelled on `NodesEnrollSeamTests`:
     - set up `LabNodesFactory`, a `SecretVault`, and `NodesLabAdapter.MintTokenAsync` into that vault;
     - run `new HistoryClient(runId, 1, "key-a", "linux", tokenRef, new Uri(client.BaseAddress!, "/agents/enroll"), factory.CreateClient(), sink, vault, redactor, TimeProvider.System)` with `RunAsync(HistoryRetry.None)`;
     - assert that the completed observation's status is 201 (read `CompleteAsync` and `HistoryClientOutcome` for the field) and that `factory.CreateDbContext().Nodes.Count() == 1`;
     - then run a second client with the same token and assert 401, which shows the token step is reached.
- Revert check: the seam test gets 400 and 0 nodes, and the raw-body assertion fails.
- Docs: the S-L2h row in the README seam table (already added, "written red in R7-1").

### R7-2 mutant-longpath (cause C; M1 Part B)

- Worktree `.claude/worktrees/wf-r7-mutant-longpath`, branch `ml/wf-r7-mutant-longpath`. Commit subject: `fix(lab): let mutant patches apply under long build paths [wf-r7-mutant-longpath]`.
- Files:
  1. `src/Tools/Dhadgar.Lab/Mutants/MutantBuilder.cs:72-76`: `ApplyCheckArgs` becomes `["-c", "core.longpaths=true", "apply", "--check", patchPath]` and `ApplyArgs` becomes `["-c", "core.longpaths=true", "apply", patchPath]`. Update the two doc comments, and the line 192 comment if needed. Do not change `ArchiveArgs`; the archive writes one tar file.
  2. `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckRunner.cs:182,197`: add the same `-c core.longpaths=true` before `-C` or after it; git accepts both.
  3. `tests/Dhadgar.Lab.Tests/Mutants/MutantBuilderTests.cs`:
     - `ApplyArgs_EnableGitLongPaths`: both arg lists start with `-c`, `core.longpaths=true`.
     - `ApplyCheck_UnderPathLongerThanMaxPath_Succeeds`, a `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())`:
       - create a temp folder whose nested target file path is more than 270 characters (`Skip.If` creation throws `PathTooLongException`);
       - write a three-line file and a one-hunk patch for it;
       - run `LabBuilder.RunAsync("git", MutantBuilder.ApplyCheckArgs(patch), folder, ct)` and assert success;
       - delete the folder in `finally`.
- Revert check: the long-path test fails with `[Lab.ProcessFailed] git exited with code 1`, and the args test fails.
- Not in scope: telling "patch does not apply" apart from other git failures. The child runner drops stderr, which is recorded as F106's second half for the campaign-end review.

### R7-3 hunt-restart (cause A; hunt, and through it the twin and M5)

- Worktree `.claude/worktrees/wf-r7-hunt-restart`, branch `ml/wf-r7-hunt-restart`. Commit subject: `fix(lab): skip nodes restarts inside a postgres cut in the hunt [wf-r7-hunt-restart]`.
- Rule (D-ML119): a `RestartNodes` op while `model.PgDown` sends nothing and changes nothing. Nodes does not start without its database (F104), and the model asserts nothing about that. A restart outside a cut is unchanged. Replay and the CsCheck sample share `RunCaseAsync`, so both follow the rule.
- Files:
  1. `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs:366-373`:

     ```csharp
     case HuntOpKind.RestartNodes:
         if (model.PgDown)
         {
             // D-ML119: Nodes does not start while its database is cut (F104); the model asserts nothing there.
             break;
         }

         Result<JsonElement> restarted = await CallAsync("svc_restart", new JsonObject { ["service"] = Service }, ct).ConfigureAwait(false);
         ...
     ```

  2. `src/Tools/Dhadgar.Lab/Hunt/EnrollmentModel.cs:34`: the `RestartNodes` summary adds "Skipped while PgDown (D-ML119)."
  3. `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`, new test `Replay_RestartInsideCut_IsNotSent`. Its fake invoke answers `svc_restart` with an error result and records every tool name. Call `hunter.ReplayAsync([PgDown, RestartNodes, PgUp, Mint], ct)` and assert success, zero `svc_restart` calls, and a non-diverged result. A second test, `Replay_RestartOutsideCut_IsSent`, gives `[Mint, RestartNodes]` with `svc_restart` answering ok and asserts exactly one call.
- Revert check: the first test returns `Failure("[Lab.Unavailable] svc_restart did not complete")`.
- Not in scope: making the sample stop early on an infrastructure error. Throwing would make CsCheck shrink and re-run tool calls. This is recorded in F104.

### R7-4 fuzz-boundary (cause D; informational)

- Worktree `.claude/worktrees/wf-r7-fuzz-boundary`, branch `ml/wf-r7-fuzz-boundary`. Commit subject: `fix(lab): size the 1 MiB fuzz boundary to the envelope [wf-r7-fuzz-boundary]`.
- Diagnosis first, with a hard stop:
  1. Add `Run7Seed_First50Cases_AreAllSendable` to `tests/Dhadgar.Lab.Tests/Fuzz/MutatorsTests.cs`. For seed `14372532009034476853` and indices 0 to 49 on `FuzzTargets.Enroll`, every `Mutators.Build(...).Body.Length <= LabHttp.MaxRequestBytes`.
  2. Run it on unchanged code. If it is green, F107 is wrong for run 7. In that case, record the case list in the worknote, revert nothing, change nothing else and report `blocked`.
- Files (only if step 2 is red):
  1. `src/Tools/Dhadgar.Lab/Fuzz/Mutators.cs:45-56,104-108`: keep the label `1MiB-minus-envelope` and its list position, so replay indices keep their kinds. When that label is drawn, compute the value per body: set `root[name] = string.Empty`, take `envelope = Encoding.UTF8.GetByteCount(root.ToJsonString())`, then set `root[name] = new string('b', LabHttp.MaxRequestBytes - envelope)`. The body is then exactly `MaxRequestBytes`. Update the doc comment.
  2. `MutatorsTests.cs`: `Boundary1MiB_FitsTheRequestLimit_ForEveryProperty` scans indices 0 to 20 000 for seed 1. Every `boundary:1MiB-minus-envelope` case must have `Body.Length == LabHttp.MaxRequestBytes`. Collect the distinct replaced property names by diffing against `ValidBody`, and assert all 5 are covered.
- Revert check: both tests fail (bodies of 1 048 632 bytes and up).
- Not in scope: F108. Keeping a safe job-error detail touches R-LOG redaction and goes to the campaign-end review.

## Run 8 (after the wave review `ship`)

D-ML121 replaces the D-ML109 order. It is the same runner and the same rules, with one fresh daemon per segment. Each segment does `stack_up wipe=true` and the services it needs. Keep the scratchpad lab home, so the F106 condition is reproduced.

| Segment | Gates | Must show |
| --- | --- | --- |
| A | Docker tests one at a time | A3 `MutationDockerTests` M1 `detected` (`detected_other` is KG-W5-9); A7 S-L12 red; the rest as in run 7. |
| 1 | hunt seed 4242, 300 s, then replay | The job `succeeded`, with a `repro.toml` that replays to the same failure or "none found in budget". In the audit, no `svc_restart` falls between a `fault_proxy_disable` and its heal or reset. |
| 2 | `mutation_run` M1 + M4 | M1 is not `stale`. Every detector run has at least one 201 (D-ML115 facts: per-client outcomes, nodes rows). `nodes.jsonl` has no "Public key is empty". M1 is `detected` (`hist:enrollment\|(E1\|E6)\|enroll-race`) and M4 is `detected` (`inv:I1\|nodes\|…`). |
| 3 | `mutation_run` M5 alone | `detected` or `detected_other` with a signature produced inside M5's own run window. |
| 4 | twin, chaos, soak, `coverage_report` | Informational, except the D-ML108 coverage bar. The twin precondition is no longer I1. |
| 5 | fuzz enroll, 50 cases, seed 14372532009034476853 | The job `succeeded` (overall may be `fail` under KG-W5-1). |

Hard stop (D-ML118): run 8 is the last run at this boundary. If a must-pass item is still red, it is parked as its known gap with an owner BlockedDecision. No further fix wave follows.

## D-ML116 recommendation

Recommendation: **hold the boundary for wave5-fix-R7, and do not park M1 or M4 now.**

- D-ML116 is contingent on "D-ML115 names no cause", and that condition no longer holds. The cause is named: a request-shape defect in the lab workload (F103), a class outside R1-R4. Its evidence is the four "Public key is empty" lines per detector run, the 400s, and the unused token.
- The mutants were never tested. Parking them as KG-W5-12 would record gate MD as unmet for a lab bug, when the fix is one line and has a seam test.
- M1's Part B `stale` also has a named cause (F106), again with a small fix.
- Park M1 or M4 as KG-W5-12 only if run 8 shows the race reached (201s in every detector run) and the mutant still ends `survived`.
