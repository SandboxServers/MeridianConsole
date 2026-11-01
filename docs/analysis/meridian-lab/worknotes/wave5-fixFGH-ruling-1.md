# Ruling: wave5-fix-F, G and H review, pass 1

Inputs: `.claude/reports/meridian-lab/wave5-fixFGH-review-1.md` (verdict `decision`; F `fix`, G `fix`, H `decision`) and `.claude/reports/meridian-lab/wave5-fixFGH-security-1.md` (verdict `fix`; N1 Medium, N2 Low, N3 Low). Range `08571257..87d63e80`, tip `87d63e80` on `campaign/meridian-lab`. Architect, 2026-11-01.

This is the review fix pass for F, G and H. It runs before the boundary run (D-ML109). It is not the "fix-I" that D-ML109 forbids after the boundary run.

## Rulings

| Item | Ruling | Where |
| --- | --- | --- |
| Review N1 (F, `*` stack_up excuse) | Real defect. Apply it in the D-ML114 form, not as the reviewer wrote it (see "Why not review N1 as written"). | D-ML114, packet FGH-a |
| Review D2 (F, excuses not time-matched) | Architect call, not owner: excuses are matched against the event time and the call's end time. Amends C11's I6 rule. | D-ML114, packet FGH-a |
| Security N1 (Medium) | Fix (a) and (b) in full. Fix (c): `*` stays only for `stack_up wipe=true` and `stack_down`, under both (a) and (b), and bounded to the call. | D-ML114, packet FGH-a |
| Security N2 (Low) | Fix: record only after a non-error outcome, with `At` = start and `Until` = end. | D-ML114, packet FGH-a |
| Review N3 (F, wiring) | Fix, but as an identity check. The reviewer's `svc_restart` call no longer records once N2 lands, because the call fails without a stack. | packet FGH-a |
| Review N2 (G, untested guard) | Fix as written (`DaemonUri`). | packet FGH-b |
| Security N3 (Low, silent I5 skip) | Fix: the accepted skip pair becomes a printed note, not a problem. | packet FGH-b |
| Review N4 (F, blank line) | Fix. The blank lines are `ModelHunter.cs:511-512`, not 509-510. | packet FGH-b |
| Review D1 (H, M1 `survived`) | Gate MD fails for M1 at this tip. No owner question yet. Diagnose first, in the boundary run, with read-only probes. Owner question only if no cause is named. | D-ML115, D-ML116 |
| Review D3 (security-review label) | Applies. The packet touches the I6 judge, the dispatcher, `LabHosts` callers and the recovery child. `security-advisor` reviews it, and the campaign PR carries `security-review` (D-ML16). | packet header |
| Review D4 (daemon port fixed at 8789) | Accepted, no change. A non-default CLI daemon port needs a new ruling. | none |
| Review D5 (stale rows) | The orchestrator updates the fix-F/G/H rows when this packet merges. The L54 row keeps "M1 `survived`; gate MD open" until D-ML115 resolves. | orchestrator |

## Why not review N1 as written

The reviewer's change (four infra targets, `stack_up` matched as `excuse.Target == service`) is correct for restart observations. It brings two failures back, though:

1. **Process exits.** Container log followers are supervised processes named with the short service name (`nodes`, `gateway`). See `ContainerLogFollower.cs` and `FollowAsync`. `svc_start` and `svc_restart` run `rm -f` on the old container first (`LabServices.StartAsync`). The old follower exits with code 0 and `Expected=false` before `FollowAsync` stops it. I6 checks that exit with `Excused(excuses, "nodes", "nodes")`, and no `svc_*` target `meridian-lab-nodes` equals `nodes`. Today only the `*` excuse hides those exits. The wave 5 run record shows `inv:I6|gateway|process_exit` on the twin row (`wave5-docker-run.md:112`). Remove `*` and every row with a `svc_start` gets an I6 `process_exit` again.
2. **`stack_up wipe=true` and `stack_down`.** Teardown runs `rm -f -v` on every registered container, nodes and gateway included (`LabStack.TearDownAsync`), so their followers exit inside the call. Four infra targets do not cover that.

New finding (no reviewer raised it): **live kill and pause excuses never match.** A fault's target is the registry id `c:nodes` (`LabServices.cs:198`, `LabStack.cs:493`). The observation name is the container name `meridian-lab-nodes` (`InvariantWiring.cs:275`). The matcher compares the target against `"c:" + name`, which is `c:meridian-lab-nodes`. `I6RestartInvariantTests` uses `nodes` for both, so it stays green. D-ML114 fixes this with one canonical name.

## D-ML114: the I6 excuse rule (amends C11 I6)

An excuse is `RestartExcuse(Action, Target, At, Until)`. `Until` is the end of the call, or the heal of a fault; null means `At`.

1. **Time.** An excuse covers an event at time `t` when `t + 30 s >= At` and `t - 30 s <= Until`.
   - For a restart observation, `t` is the new `StartedAt`, or the window end when `StartedAt` is null.
   - For a process exit, `t` is `exit.At`.
   - The window prefilter on excuses is removed. A later crash of the same service is no longer excused by an earlier call in the same window (review D2).
2. **Target.** Targets and names are compared in canonical form: strip a leading `c:`, then a leading `meridian-lab-`. So `c:nodes`, `meridian-lab-nodes` and the follower `nodes` all mean `nodes`. A fault target also matches when it equals the name exactly (member ids).
3. **Restart-count rise.** A `RestartCount` rise is excused only by `fault_kill` or `fault_pause`. A recreate or `docker start` moves `StartedAt` and leaves the count unchanged. Only a restart policy raises the count, and every lab container runs `--restart no` (`LabServices.cs:168`, `LabStack.cs:461`).
4. **What each call excuses:**

   | Call | Targets | Notes |
   | --- | --- | --- |
   | `svc_start`, `svc_restart`, `svc_stop` | `meridian-lab-<service>` | `svc_stop` is added: it stops the container, so its follower exits. |
   | `member_reset` | `meridian-lab-<member>` | Unchanged. |
   | `stack_up` (wipe absent or false) | `meridian-lab-postgres`, `-rabbitmq`, `-redis`, `-toxiproxy` | Matches `LabStack.cs:236-277`. |
   | `stack_up wipe=true`, `stack_down` | `*` (every lab container and follower) | Bounded to the call by rule 1. Never excuses a count rise (rule 3). |
   | `fault_kill`, `fault_pause` | the fault's target id | `At` is the fault start, `Until` is `HealedAt`, or no end while the fault is active. |

5. **Recording.** The dispatcher records the excuse only when the tool returns a non-error outcome, with `At` = the dispatch start and `Until` = the time the tool returned. A refused, failed, cancelled or throwing call records nothing (security N2).
   - Accepted consequence: a `svc_start` that recreates the container and then fails `WaitReady` leaves that restart unexcused. That I6 `restart` is a true statement about a failed step.
   - Accepted consequence: an I6 check that runs while a call is still in flight can report that call's restart, because the excuse lands when the call returns. Today checkpoints run between calls on one job thread, so this is not hit.

## D-ML115: M1 `survived`, diagnose before ruling 3

Gate MD fails for M1 at `87d63e80` (`wf-h.md`, H-run 1: `survived`, 5 detector runs, no signature, `check_errors 0`). The earlier `detected_other` verdicts were I6 noise from the mutant's own `svc_start` (review D1). Continuation 4 also shows M4 with only `inv:I6|meridian-lab-nodes|restart` and no `inv:I1` signature. M4 depends on the same race, so it is expected to fall to `survived` too once the noise is gone.

The run record names no cause. Under D-ML109, ruling 3 may spec a fix only for a cause the run record names. So the boundary runner (continuation 7, Part B, at the mutation step) records these read-only facts for M1, and for M4 if it also ends `survived`. No fix, no retry with other arguments:

1. The verdict JSON and its `detector_run_ids`.
2. For the first detector run id: `events_read` on that run.
   - Count the `proxy_request_arrived` observations and give the spread of their `At` (first to last).
   - Give each `proxy_upstream_completed` status and `At`.
   - Give the per-client enroll outcomes (status codes) from the history.
3. The mutation job's `svc_start` result for nodes: the build id served, and whether it equals the mutant build id the job reports.
4. `db_query` on the `nodes` database: how many nodes the detector run's token created. More than one means the race happened and the checker missed it.

The runner classifies the result into one class:

| Class | Evidence | Lab or patch fix ruling 3 may spec |
| --- | --- | --- |
| R1, barrier hollow | Fewer than `clients` arrivals, or arrivals spread more than 1 s | Make the barrier result fail the attempt as UNVERIFIED (`HistoryRunJob.cs:420` discards it today), then raise detector strength. |
| R2, race serialized downstream | All arrivals inside 1 s, but upstream completions are serial and only one 201 comes back (Gateway limiter or Nodes ordering) | Detector strength: the clients and attempts, or the path, through the Gateway as the real path goes. |
| R3, mutant not served | The build id differs, or nodes fails to start on the mutant | Fix the mutant patch or the build. |
| R4, checker miss | More than one node created, or more than one 201, and no `hist:` violation | Fix the checker. |

A weaker mutant is never an option (gate MD).

## D-ML116 (BlockedDecision, owner; contingent)

The question is raised only if the continuation 7 record fits none of R1-R4.

Question: park M1 (and M4 if it shares the cause) as known gap KG-W5-12, so waves 5 and 5E close with gate MD unmet for that mutant? Or hold the boundary until the race is reproduced?

Proposed default: park.
- KG-W5-12: "M1 (and M4 if listed) `survived`; the enroll race is not shown to be reached".
- Gate MD at 5E holds on the remaining mutants.
- M1 reruns at the campaign-end review (D-ML74). Gate MD at wave 10 still needs it `detected`.

## Packets

One worktree, `ml-wf-fgh-fix` (`.claude/worktrees/ml-wf-fgh-fix`), on branch `ml/wf-fgh-fix` from `campaign/meridian-lab` at `87d63e80`.
- One `packet-coder` runs FGH-a, then FGH-b, with one commit each. No Docker.
- Security review area: yes (`security-advisor` pass on the range).
- Merge into `campaign/meridian-lab` before the boundary run (D-ML109). The child daemon runs the committed HEAD (`wf-h.md`), so commit before any Docker use.

Repo rules for both parts:
- `Result<T>` for every failure. Never `Result<T>.Success(null)`.
- Nullable is on and warnings are errors.
- No package versions in `.csproj`.
- `using` on every `IDisposable`.
- xUnit `[Fact]` and `[Theory]` with `FakeTimeProvider`, as in the neighbouring tests.
- Tables in docs have spaces around pipes.
- Edit only the files named.

### FGH-a: I6 excuses (D-ML114), dispatcher recording, wiring test

Commit subject: `fix(lab): I6 excuses matched by time and canonical target, recorded on success [wave5-fix-FGH-a]`.

1. `src/Tools/Dhadgar.Lab/Invariants/I6RestartInvariant.cs`
   - Line 15 becomes `public sealed record RestartExcuse(string Action, string Target, DateTimeOffset At, DateTimeOffset? Until = null);`. Its XML doc says `Until` is the call end or the fault heal, and null means `At`.
   - In `CheckAsync`, delete the window filter on lines 61-63: `IReadOnlyList<RestartExcuse> excuses = _excuses();`.
   - Restart loop: `bool countRose = now.RestartCount > before.RestartCount;`, `restarted = countRose || <the StartedAt test>`, and `DateTimeOffset at = now.StartedAt ?? window.To;`. The call becomes `Excused(excuses, now.Name, at, countRose)`.
   - Exit loop: `Excused(excuses, exit.Name, exit.At, countRose: false)`.
   - Replace `Excused` (lines 121-140) with:

     ```csharp
     internal static bool Excused(IReadOnlyList<RestartExcuse> excuses, string name, DateTimeOffset at, bool countRose)
     {
         string subject = Canonical(name);
         foreach (RestartExcuse excuse in excuses)
         {
             if (!Covers(excuse, at))
             {
                 continue;
             }

             bool sameTarget = Canonical(excuse.Target) == subject || excuse.Target == name;
             bool matches = excuse.Action switch
             {
                 "fault_kill" or "fault_pause" => sameTarget,
                 "svc_start" or "svc_restart" or "svc_stop" or "member_reset" => !countRose && sameTarget,
                 "stack_up" or "stack_down" => !countRose && (sameTarget || excuse.Target == LabRestartExcuses.AllTargets),
                 _ => false,
             };
             if (matches)
             {
                 return true;
             }
         }

         return false;
     }

     internal static bool Covers(RestartExcuse excuse, DateTimeOffset at) =>
         at + Margin >= excuse.At && at - Margin <= (excuse.Until ?? excuse.At);

     internal static string Canonical(string target)
     {
         string name = target.StartsWith("c:", StringComparison.Ordinal) ? target[2..] : target;
         return name.StartsWith(LabDefaults.ContainerPrefix, StringComparison.Ordinal) ? name[LabDefaults.ContainerPrefix.Length..] : name;
     }
     ```

   - Update the class XML doc (lines 17-21) to state the D-ML114 rule in two sentences.
2. `src/Tools/Dhadgar.Lab/Invariants/LabRestartExcuses.cs`
   - The `AllTargets` doc (line 12) becomes "The target of `stack_up wipe=true` and `stack_down`: every lab container, bounded to the call (D-ML114)".
   - Add `internal static readonly string[] StackUpContainers = ["postgres", "rabbitmq", "redis", "toxiproxy"];`.
   - `RecordCall(string tool, JsonElement arguments, DateTimeOffset at, DateTimeOffset until)` and `ForCall(string tool, JsonElement arguments, DateTimeOffset at, DateTimeOffset until)`. Every `new RestartExcuse(...)` passes `until`.
   - Add `case "svc_stop":` beside `svc_start` and `svc_restart`.
   - `case "stack_up":` gives `ReadBool(arguments, "wipe") ? [new RestartExcuse(tool, AllTargets, at, until)] : StackUpContainers.Select(c => new RestartExcuse(tool, LabDefaults.ContainerPrefix + c, at, until)).ToArray()`.
   - Add `case "stack_down": return [new RestartExcuse(tool, AllTargets, at, until)];`.
   - Add `private static bool ReadBool(JsonElement arguments, string name)`: true only for an object holding `name` with `JsonValueKind.True`.
   - Update the class doc (lines 6-8): "each successful call", plus the canonical-name sentence.
3. `src/Tools/Dhadgar.Lab/Invariants/InvariantWiring.cs:289`: the fault excuse becomes `new RestartExcuse(<kind as now>, f.TargetId, f.StartedAt ?? f.HealedAt ?? DateTimeOffset.MinValue, f.HealedAt ?? DateTimeOffset.MaxValue)`. `Covers` never adds to `excuse.At` or `Until`, so neither value overflows. Update the doc at lines 282-285.
4. `src/Tools/Dhadgar.Lab/Tools/LabToolDispatcher.cs`
   - Delete lines 151-152 (the comment and the `RecordCall`).
   - In the `try` block after `outcomeCode = ...` (line 159), add:

     ```csharp
     if (!outcome.IsError)
     {
         // D-ML114: only a call that succeeded excuses the restarts it caused, from its start to its end.
         _excuses?.RecordCall(toolName, toolArguments, started, _time.GetUtcNow());
     }
     ```

   - Add `internal LabRestartExcuses? Excuses => _excuses;` with the doc "The excuse log this dispatcher records into (wiring test)".
5. `tests/Dhadgar.Lab.Tests/Invariants/LabRestartExcusesTests.cs`
   - Every `RecordCall` gets a 4th argument (`until`).
   - `Build` gains an optional `List<ProcessExit>? exits = null` (pass `() => (exits ?? []).ToArray()`).
   - Replace `SvcStubTool` with `StubTool(string name, bool fail, FakeTimeProvider time)`.
     - Its `ExecuteAsync` calls `time.Advance(TimeSpan.FromSeconds(10))`.
     - It then returns `Result<object>.Failure("[Lab.InvalidArgument] stub failure")` when `fail`, or a success otherwise.
     - Args record: `StubArgs(string? Service = null, bool? Wipe = null)`.
   - `NewDispatcher(excuses, gateFor, LabToolRegistry? registry = null)`.

   Changed tests:

   | Test | Change |
   | --- | --- |
   | `ForCall_SvcStart_ExcusesTheRegistryNameOfTheService` | Record `(T0, T0+4s)` and also assert `Until == T0+4s`. |
   | `ForCall_StackUp_ExcusesEveryContainer` | Rename to `ForCall_StackUp_ExcusesTheInfraContainers`. Args `{}` and `{"wipe":false}`; assert the four `meridian-lab-*` infra targets, in order. |
   | `I6_ExcuseOutsideTheWindow_Expires` | Rename to `I6_ExcuseLongBeforeTheRestart_Expires`. Record `(T0-5min, T0-5min+5s)`. The new observation keeps `RestartCount` 0 (StartedAt T0+20s), so time alone decides. |
   | `I6_StackUpExcuse_CoversAnyContainer` | Rename to `I6_StackUpExcuse_CoversTheInfraContainers`. Container `meridian-lab-postgres`; record `(T0+5s, T0+15s)`; StartedAt moves to T0+20s with count 0. Assert empty. |
   | `Dispatcher_AcceptedSvcStart_RecordsTheExcuse` | Also assert `At == T0` and `Until == T0+10s`. |

   New tests. Each fails on the revert named:

   | Test | Setup and assert | Fails if reverted |
   | --- | --- | --- |
   | `ForCall_StackUpWipe_And_StackDown_ExcuseAllTargets` | `stack_up {"wipe":true}` and `stack_down {}` each give one `*` entry. | the wipe split |
   | `ForCall_SvcStop_ExcusesTheService` | `svc_stop {"service":"nodes"}` gives `meridian-lab-nodes`. | the `svc_stop` case |
   | `Dispatcher_FailedStackUp_RecordsNoExcuse` | Stub `stack_up` with `fail: true`, gate Open; `IsError` is true and Snapshot is empty. | security N2 |
   | `I6_StackUpExcuse_DoesNotExcuseNodes` | `stack_up {}` at `(T0+5s, T0+15s)`; nodes StartedAt T0+10s, count 0; exactly one `restart` violation. | `*` for plain stack_up |
   | `I6_StackUpExcuse_RestartAfterTheCall_Violates` | `stack_up {}` at `(T0, T0+10s)`; postgres StartedAt T0+90s, count 0, window To T0+2min; one violation. | the time match |
   | `I6_StackUpExcuse_CountRiseDuringTheCall_Violates` | Same call; postgres count 0 to 1, StartedAt T0+5s; one violation. | security N1a |
   | `I6_StackUpExcuse_RecreateDuringTheCall_NoViolation` | Same call; postgres StartedAt T0+5s, count 0; empty. | anti-always-violate |
   | `I6_StackUpWipe_FollowerExitDuringTheCall_NoViolation` | `stack_up {"wipe":true}` at `(T0, T0+10s)`; exit `("nodes", 0, false, T0+5s)`; no baseline needed (exits only); empty. | `*` scope |
   | `I6_StackUpWipe_ExitAfterTheCall_Violates` | Same call; exit at T0+90s; one `process_exit` violation. | the time bound on `*` |
   | `I6_SvcStart_FollowerExitOfTheService_NoViolation` | `svc_start nodes` at `(T0, T0+10s)`; exit `("nodes", 0, false, T0+3s)`; empty. | canonical names |
   | `I6_SvcStart_LaterCrashOfTheService_Violates` | Same call; nodes StartedAt T0+50s, count 0; one violation. | review D2 |
   | `I6_SvcRestart_CountRise_StillViolates` | `svc_restart nodes` at `(T0, T0+10s)`; count 0 to 1 at T0+5s; one violation. | rule 3 |
   | `I6_LongSvcRestart_StartNearItsEnd_NoViolation` | `svc_restart nodes` at `(T0, T0+120s)`; StartedAt T0+115s, count 0; window To T0+3min; empty. | `Until` |

6. `tests/Dhadgar.Lab.Tests/Invariants/I6RestartInvariantTests.cs`: add two tests.
   - `I6_KillFaultOnTheRegistryId_ExcusesTheContainerName`: observation `meridian-lab-nodes` count 0 to 1 at T0+20s; excuse `new("fault_kill", "c:nodes", T0.AddSeconds(10))`; empty. It fails on the old `"c:" + name` matcher.
   - `I6_HealedFaultExcuse_CoversUntilTheHeal`: through `InvariantWiring.Excuses` with a fake `IFaultHistory` entry `FaultHistoryEntry("f1", "kill", "c:nodes", T0, T0.AddMinutes(3))`; nodes StartedAt T0+3min+10s; empty. It fails if `Until` is dropped.
   - The existing six tests stay unchanged.
7. `tests/Dhadgar.Lab.Tests/Daemon/LabDaemonServiceLifetimeTests.cs`, after line 70, in `Services_BuildWithScopeAndBuildValidation_ResolveEveryLabService`:

   ```csharp
   Assert.Same(services.GetRequiredService<LabRestartExcuses>(), scope.ServiceProvider.GetRequiredService<LabToolDispatcher>().Excuses);
   ```

   Add `using Dhadgar.Lab.Invariants;`. Revert checks: delete `services.AddSingleton<LabRestartExcuses>();` (`InvariantsArea.cs:43`) and the test fails. Make the dispatcher drop its excuses argument and it fails too. I6 resolves the same singleton type (`InvariantsArea.cs:91`).
8. Docs owed:
   - `src/Tools/Dhadgar.Lab/README.md:244`, the I6 row becomes `Unexplained restarts and process exits; kill and pause faults, and the lab's own successful svc_*, stack_* and member_reset calls, excuse only their own targets at the time they ran (D-ML114)`.
   - The architect has already amended `work-packets.md` C11 I6 (line 740) and L21 file 7 (line 2385).

### FGH-b: self-check guard, I5 notes, blank line

Commit subject: `fix(lab): self-check daemon URI seam and I5 skip notes [wave5-fix-FGH-b]`.

1. `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckRecovery.cs`
   - Add, next to `LabCliUri` (line 521):

     ```csharp
     internal static Result<Uri> DaemonUri(string path)
     {
         Uri uri = LabCliUri(path);
         return LabHosts.IsDaemonEndpoint(uri)
             ? Result<Uri>.Success(uri)
             : Result<Uri>.Failure("[Lab.Refused] the target is not a lab endpoint");
     }
     ```

   - In `GetStatusAsync` (lines 452-456) and `CallToolAsync` (lines 485-489), replace the `LabCliUri` and `IsDaemonEndpoint` pair with `Result<Uri> target = DaemonUri(<the same path>); if (target.IsFailure) { return Result<JsonDocument>.Failure(target.Error); } Uri uri = target.Value!;`.
   - Add `public sealed record RecoveryVerdict(IReadOnlyList<string> Problems, IReadOnlyList<string> Notes);` with docs: Problems fail the leg; Notes are printed and never fail it.
   - Replace all 18 `Result<IReadOnlyList<string>>` in this file with `Result<RecoveryVerdict>`. All 18 are on the `RunAsync` → `RunInCopyAsync` → `JudgeRestartAsync` chain.
   - Line 355 becomes `Result<RecoveryVerdict>.Success(new RecoveryVerdict(Judge(faultsEmpty, leaseHeld, i5), I5Notes(invariants.Value!.RootElement)))`.
   - Add `internal static IReadOnlyList<string> I5Notes(JsonElement payload)`. It returns `Cut("I5 " + Field(entry, "source") + " not checked after the restart: " + Field(entry, "reason"))` for every I5 `skipped` entry where `IsAcceptedSkip` is true. It returns an empty list when there is no `skipped` array. `I5Problems` stays unchanged.
2. `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckProgram.cs`
   - Split `RunRecovery` (lines 60-84): it keeps the `RunAsync` call and the failure branch, then returns `ReportRecovery(result.Value!, Console.Out)`.
   - Add `internal static int ReportRecovery(RecoveryVerdict verdict, TextWriter output)`:
     - it writes each note as `selfcheck recovery: note: <note>`;
     - then `selfcheck recovery: passed` and returns 0 when there are no problems;
     - otherwise it writes each problem as now and returns 1.
3. `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryTests.cs` (Docker, compile only):
   - line 26 becomes `Result<RecoveryVerdict> result`;
   - line 34 becomes `Assert.Empty(result.Value!.Problems);`.
4. `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryGuardTests.cs`
   - Rewrite `ToolCallUrl_IsTheDaemonMcpEndpoint`: `SelfCheckRecovery.DaemonUri(LabDefaults.McpPath)` succeeds and `.Value!.ToString()` is `http://127.0.0.1:8789/mcp`.
   - Add `StatusUrl_IsTheDaemonStatusEndpoint`, the same for `LabDefaults.StatusPath`. Take the expected string from `LabDefaults.StatusPath` itself; do not guess it.
   - Revert check: make `DaemonUri` call `LabHosts.IsLabEndpoint`, and both tests go red. Port 8789 is not in `LabPorts.ServicePorts` (`LabPorts.cs:27-28`), so the old guard refuses the daemon URI. Record the run in the worknote.
   - The tests pin `DaemonUri`. That `GetStatusAsync` and `CallToolAsync` both go through it is checked by reading, by the reviewer.
5. `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryI5Tests.cs`: add two tests.
   - `SkipPairAlone_IsANote`: the payload of `SkipPairAlone_IsAccepted` gives two notes, containing `I5 queues not checked after the restart` and `I5 proxies not checked after the restart`.
   - `SkipOfContainers_IsNotANote`: `I5Notes` is empty.
6. New `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryReportTests.cs`, two tests, each with a `StringWriter`:
   - verdict `([], [note])` returns 0, and the output holds `selfcheck recovery: note: ` + note and `selfcheck recovery: passed`;
   - verdict `(["p"], [note])` returns 1, and the output holds the note and `selfcheck recovery: p`.
   - Drop the note printing and both fail.
7. `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`: delete line 512 (the second of two blank lines).
8. Docs owed: none beyond the worknote. The `selfcheck --recovery` output gains `note:` lines; if a guide quotes that output, list the guide in the worknote for the orchestrator.

### Checks (run in this order, from the worktree root)

1. `dotnet build src/Tools/Dhadgar.Lab/Dhadgar.Lab.csproj -c Release`
2. `dotnet build src/Tools/Dhadgar.Lab.SelfCheck/Dhadgar.Lab.SelfCheck.csproj -c Release`
3. `dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release` (compiles the Docker tests as well)
4. `dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LabRestartExcusesTests|FullyQualifiedName~I6RestartInvariantTests|FullyQualifiedName~LabDaemonServiceLifetimeTests|FullyQualifiedName~SelfCheckRecoveryGuardTests|FullyQualifiedName~SelfCheckRecoveryI5Tests|FullyQualifiedName~SelfCheckRecoveryReportTests|FullyQualifiedName~SelfCheckRecoveryJudgeTests|FullyQualifiedName~LabToolDispatcher|FullyQualifiedName~ModelHunter"`
5. `dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true&FullyQualifiedName!~E2ETests&FullyQualifiedName!~DockerTests"` (baseline at fix-H: 1415 passed, 5 skipped)

Revert checks for the worknote: for FGH-a, put `*` back on plain `stack_up`, restore the window prefilter, and move `RecordCall` back before the `try`, one at a time. Each named test goes red. Record each run.

Worknote: `docs/analysis/meridian-lab/worknotes/wf-fgh-fix.md`. It records forced test changes, the revert runs and the two suite counts.

## Ledger changes made by this ruling

- README decisions: D-ML114, D-ML115, D-ML116.
- `work-packets.md` C11 I6 row (line 740) and L21 file 7 (line 2385): "Amended by D-ML114" notes.
