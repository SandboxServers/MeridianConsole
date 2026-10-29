# Ruling: wave 5 reviews R1, R2, R3 and security, pass 1

Brief: rule the `decision` items of the three wave 5 reviews and consolidate every fix note (reviews and security) into at most four independent fix packets. Reports: `.claude/reports/meridian-lab/wave5-R1-review-1.md`, `wave5-R2-review-1.md`, `wave5-R3-review-1.md`, `wave5-security-1.md`; Docker run notes `worknotes/wave5-docker-run.md` (the live-gate continuation was still appending; its section 3 is treated as in progress). Read at campaign tip `339c6ca1` in `.claude/worktrees/meridian-lab`. No source or test file was edited for this ruling.

- **Decisions:** D-ML95 (orchestrator, recorded), D-ML96 to D-ML104 (architect). All Accepted.
- **BlockedDecision:** one, OD-W5-1 (BundleStaging concurrency). It has a default and blocks nothing.
- **Findings:** F90 to F95.
- **Fix packets:** `wave5-fix-A` (results and known issues), `wave5-fix-B` (jobs, hunter, fuzz, soak), `wave5-fix-C` (chaos, history, Docker test harness), `wave5-fix-D` (mutants and self-check). Security review on all four.
- **Follow-up packets (ledger rows, outline only):** L05b, L25g, L28b, L52b.

## Decisions

### D-ML95 (recorded): `UatRunTool` and `ResultsService`

The orchestrator ruled it during the wave 5 merges (RESUME line 8): `UatRunTool` keeps `GetRequiredService<ResultsService>()`; test providers register `ResultsService`; setup changes are additive only. It had no ledger row. Recorded as **Accepted (orchestrator; recorded by the architect)**.

### D-ML96: dead known-issue seeds (R1 item 1)

A known issue ships only when two things hold: a signature that `SignatureBuilder` (or `FuzzOracle`) can produce matches it, and the failure it excuses is the issue's real symptom. An excuse broader than the symptom hides real failures, which is worse than no entry.

| Id | Ruling | Reason |
| --- | --- | --- |
| KI-DV13 | Remove; documentation only | The pattern is `http:2xx`; `BuildAll` emits http for 5xx only. A missing auth check is a missing refusal: it needs a row clause (a later S-L row), not a signature. |
| KI-DV12 | Remove; documentation only | The 4xx half is unreachable. The 5xx half would excuse every 5xx on `/api/v1/auth/`, and a cookie-domain problem never causes a 5xx. |
| KI-DV3 | Remove; documentation only | DV-3 says "untested", not "fails". A 5xx on `/refresh` must surface as new. |
| KI-DELAYED-PLUGIN | Remove; documentation only | The pattern is on exchange text, which no exception frame carries. A type-only pattern would overlap KI-RABBIT-HOST. |
| KI-94 | Remove; documentation only | #94 is a missing signature check. It throws nothing, so an exception through `CommandValidator` would be a different bug. The agent is not on the lab stack yet. |
| KI-RABBIT-HOST | Reshape | Pattern `^exc:RabbitMQ\.Client\.Exceptions\.BrokerUnreachableException\|`, `services = ["notifications"]`. The exc text starts with the exception type, never the service name. |
| KI-DISCORD-SECRETS-PORT | Reshape | Pattern `^exc:System\.Net\.Http\.HttpRequestException\|`, `services = ["discord"]`. Keep the "Unverified against live signatures" comment. |
| KI-F69-HTTP | Keep | Reachable through `FuzzOracle` (`FuzzOracle.cs:109`), not row evidence. Add a comment that says so. |
| KI-ENROLL-RETRY, KI-F67, KI-F69, KI-NOTIF-OUTBOX | Keep | KI-F67 is re-scoped by D-ML97. |

A new test, `KnownIssuesReachabilityTests`, holds one sample `RowFailure` per shipped id, built only through `SignatureBuilder`. It asserts that `KnownIssues.Match` returns that id. A shipped id with no sample fails the test, so every future entry must come with a reachable sample. The removed ids get a comment block at the end of `known-issues.toml` naming them as documentation-only, with D-ML96 as the reason. `PROJECT-STATE.md` never calls a seed "detected". A grep of `PROJECT-STATE.md` finds no such claim today, so it needs no edit.

### D-ML97: KI-F67 drops the hunter scope (R1 item 2)

KI-F67 goes back to D-ML94's row scope: `sections = ["catalog-nodes"]`, `rows = ["nodes-pg-down-enroll"]`. The hunt and enrollment scope can never match, for two reasons:

- `requires` names the catalog step clause, which the hunter row never carries.
- The scope is a sections × rows product.

The hunter already classifies F67 in-process: `ModelHunter.cs:314-317` counts a 5xx enroll inside a PgDown window as `known`, and `ModelHunter.cs:428` allows I1 only on that op. So the hunt needs no known-issue entry. This amends the hunter half of D-ML87 and D-ML94's "L27 widens KI-F67's scope to its hunter row".

### D-ML98: L25e scope item 1 deviation accepted, follow-up L25g (R1 item 4)

The deviation is accepted. Today the step method and route are not recorded, `error_code` is always null, and exc evidence exists only when the log message carries the type and the `at` frames. The status doc lists `exc` and `http` as built with that limit and never as complete.

New follow-up packet **L25g**:

- record each step's method, route template and `error_code` (problem+json `type` or `errorCode`) in the row evidence;
- parse the log `Exception` field into `ExceptionEvidence`.

R1 item 3 is a risk: a real enroll 500 now yields an `exc:` signature that KI-F67 does not list, so the pg row may end `claimed_mismatch`. That is checked on the live gate. If the row does end `claimed_mismatch`, L25g adds the exact exc text from the live bundle to KI-F67's `signatures`, never a wildcard type. L25g is outline only; the architect specs it before dispatch.

### D-ML99: L52 narrowed to `invariant_check`, follow-up L52b (R2 D1)

L52 ships as it is: `CoverageCollector` records `invariant_check` from `invariants.json`. Note R2-N13 corrects the type summary so it no longer claims the other four kinds are live. Until **L52b** lands:

- `coverage_report` lists `route_status`, `fault_target`, `error_code` and `transition` as "not collected". It never lists them as "unhit".
- The status doc claims no route coverage.

New follow-up packet **L52b** feeds the four kinds:

- `route_status` from Hosting.Diagnostics lines in the L07 log store;
- `fault_target` from the run's `faults`;
- `error_code` from step results, which needs L25g;
- `transition` from `TransitionSnapshotter`, called at each row end by the runner.

The wave-boundary "unhit keys" gate covers `invariant_check` only until L52b.

### D-ML100: every job ends with an invariants check, which makes M4 and M5 detectable (R3 M4 and N2)

`MutationRunJob` calls `invariants_check` with `since` set to the attempt start (ISO 8601 `O` format) after each detector attempt that returned no error. It adds one text per violation, built with `SignatureBuilder.Invariant(invariant_id, service, check_id).Text`, to that attempt's signatures.

- M4 keeps `expect_pattern '^inv:I1\|nodes\|'`. A missing concurrency catch yields an unhandled 500 on Nodes, so I1 shows it.
- M5 is detectable both ways: through `ingest.signature_texts` (R3-N2, below) and through the same check.

The rule that applies to every job: soak (R2-N3), fuzz (R2-N10) and mutation each end with an `invariants_check` over the job or attempt window.

- A violation or a check error fails a soak or fuzz outcome.
- An `invariants_check` that returns an error makes a mutation attempt `unreachable`.
- `errors` entries are counted as `check_errors` in the mutant entry and do not change the verdict.

The shipped mutant files are unchanged.

### D-ML101: the I7 "no soak is running" skip is not-applicable, not a dead gate (R2-N1)

D-ML80 is amended. S-L6 accepts skips on rows `(run-start)` and `stack-up` as before. It also accepts, on any row, exactly the skip `invariant_id = I7`, `source = soak`, `reason = "no soak is running"`. I7 has no data outside a soak, so this skip means "not applicable". The anti-always-skip proof is the new `SoakJob_RisingTrend_FailsTheOutcome` test in wave5-fix-B, in which I7 fires. Any other I7 reason, such as `fewer than 3 samples`, still fails S-L6. R2-N2 is already done at the tip:

- `I7ResourceTrendInvariant.cs:122,134` returns both reasons.
- `I7_NoSoakRunning_Skips` and `I7_TwoSamples_Skips` exist.

So R2-N2 has no work.

### D-ML102: `selfcheck --recovery` refuses an elevated session and reports why its child died (R3-N7, docker failure 5)

The lab daemon refuses to run elevated (`LabDaemon.cs:79`). The recovery child is a real CLI daemon, so from an elevated session it exits at once. `StartDaemonAsync` then returns null, which is reported as `[Lab.Timeout]`.

1. **Elevation preflight.** `SelfCheckRecovery.RunAsync` checks elevation after the Windows check and before `IsDaemonRunning`. If `ElevationCheck.IsElevated()` returns true, it returns `[Lab.Elevated] selfcheck --recovery needs a non-elevated session`. Nothing is copied or built first.
2. **Early exit.** When the child exits before answering `/status`, the error is `[Lab.ProcessFailed] the lab daemon exited with code <n> before answering /status: <line>`. `<line>` is the first stderr line of the child that starts with `[Lab.`, cut to 300 characters, or `no [Lab.] line` when there is none.
3. **Tool failures** keep the tool's text (R3-N7).
4. **Test.** `SelfCheckRecoveryTests` becomes a `SkippableFact` that skips when `ElevationCheck.IsElevated()` returns true. The skip reason is `selfcheck --recovery needs a non-elevated session (D-ML102)`. Wave-boundary Docker runs are non-elevated: they change no machine state, so D-S239 does not apply. A skipped recovery test is reported as UNVERIFIED, never as a pass.

The elevation cause is a hypothesis. The session that wrote this ruling is not elevated, and the boundary agent's elevation was not recorded. The diagnostics in items 2 and 3 settle it on the next run.

### D-ML103: one serial gate for every Docker, stack, port-8789 or mutex test (docker failures 1, 3, 5, 6)

`"LabHome"` has no `CollectionDefinition`, and the project has no `xunit.runner.json`, so collections run in parallel. Batch 1 ran four lanes at once on one Docker engine:

- the LabHome lane: TwinLatency, Mutation, LabSelfTest, TenantTwin;
- `SelfCheckRecoveryTests`, with no collection;
- `SelfCheckE2ETests`, with no collection;
- the LabDocker gate.

Batch 2 did the same with `BlastRadiusDockerTests` beside the LabHome lane. The stack containers have fixed names, so these lanes collide. D-ML84 assumed serial runs.

The rule:

1. Every test class that needs Docker, the lab stack, port 8789 or the user's single-instance mutex carries `[Collection(LabDockerGate.Name)]`. `DisableParallelization = true` runs that collection alone, after the parallel ones, so moving a class out of `"LabHome"` loses no isolation.
2. Classes to move:
   - from `"LabHome"`: `MutationDockerTests`, `EnrollHistorySeamTests`, `TenantTwinSeamTests`, `TwinLatencyDockerTests`, `FaultSmokeE2ETests`, `LabSelfTestE2ETests`;
   - from no collection: `BlastRadiusDockerTests`, `FaultKindsDockerTests`, `SelfCheckRecoveryTests`, `SelfCheckE2ETests`.
3. A guard test, `LabDockerGateTests`, fails for any class without the gate when either holds:
   - it carries `Trait("RequiresDocker", "true")` at class or method level;
   - it is named in an explicit list, starting with `SelfCheckE2ETests`.
4. Docker tests acquire their lease with `ttl_s = 3600`. Teardown releases the lease and stops or removes the stack without throwing, so the original failure stays the reported one.

### D-ML104: a lease-checked call keeps its lease alive while it runs (F95), follow-up L05b

`LabToolDispatcher` touches the caller's lease once, at call start (`LabToolDispatcher.cs:113`), and cancels the call if the lease ends while it runs (`:102-103`). A synchronous call that runs longer than the lease's TTL is therefore cancelled part-way, and the caller sees `Lab.LeaseRequired` on its next call. Jobs avoid this by renewing their own lease (`JobRunner.LeaseTtlSeconds` 120).

New follow-up packet **L05b**:

- `LeaseWatch` touches the lease every TTL/3 while the call runs and stops when the call ends;
- a release or a takeover by someone else still cancels the call;
- each call stays bounded by its own budget, so a dead client holds the lease no longer than the call's budget.

Until L05b lands, Docker tests use `ttl_s = 3600` (D-ML103).

## BlockedDecision (owner)

| ID | Question | Default until ruled |
| --- | --- | --- |
| OD-W5-1 | BundleStaging concurrency (R1 item 8): add a per-folder lock around `Commit` and `Recover`, or accept and document? A reader's `Recover` during a `Commit` can make the attest report `[Lab.Unavailable]`. There is no data loss. | Accept and document. `uat_attest` is Leased and has one writer, and the failure is visible and retryable. The wave5-fix-A worknote records it. |

R1 item 7 (ingest is not transactional; stale `signature_hits` after a re-ingest; signature `state` is last-writer-wins) is accepted by the architect for a local index. It is recorded in the wave5-fix-A worknote and needs no owner call.

## Findings

| ID | Finding | Evidence |
| --- | --- | --- |
| F90 | Every history run is stored as UNVERIFIED. The history `row.json` and `run.json` write `bundle.Grade.ToString()` ("Fail", "Pass"), and `RowVerdicts.ParseGrade` maps anything except the upper-case ledger words to Unverified. A passing history run therefore shows as a new failure. The ingest test asserts no grade. | `BundleWriter.cs:328,373`; `RowVerdicts.cs:175-183`; `BundleIngestorTests.cs:171-192` |
| F91 | Five of the eleven shipped known issues can never match, and two put the service name where the exception type goes. A claim on any of them would be `claimed_mismatch`. | `known-issues.toml:74-166`; `SignatureBuilder.cs:80,148` |
| F92 | The hunter reads `violation.rule`, but `invariants_check` serializes `InvariantViolation` with `invariant_id`. In production `rule` is always empty, so the hunter ignores every I1 and I3 violation. The test fake answers with `rule`, which hides this. | `ModelHunter.cs:422`; `InvariantsCheckTool.cs:64`; `ModelHunterTests.cs:194-197` |
| F93 | Two shipped mutants can never be `detected`. M4's `history_run` detector yields only `hist:` texts, but its pattern is `inv:`. M5's `uat_run` payload carries a signature count without the texts. `MutationDockerTests` also has no repo root and hides the error body. | `tools/lab/mutants/M4-no-concurrency-catch/mutant.toml`; `MutationRunJob.cs:322-371`; `ResultsService.cs:249-270`; `MutationDockerTests.cs:38-44,62` |
| F94 | The Docker test classes run in up to four parallel lanes on one engine with fixed container names: `"LabHome"` is not a serial gate, and four classes have no collection. This collision is the likeliest shared cause of docker failures 3, 5 and 6. | `LabDockerGate.cs:7`; no `CollectionDefinition("LabHome")`; `wave5-docker-run.md` checks 1-2 |
| F95 | A lease-checked synchronous call that outlives its lease TTL is cancelled part-way. The test's `finally` release then throws `Lab.LeaseRequired` and replaces the original error. `EnrollHistorySeamTests` acquired the default 600 s lease and failed at 10 m 10 s. | `LabToolDispatcher.cs:102-123`; `EnrollHistorySeamTests.cs:53-57,110-117`; `wave5-docker-run.md` failure 6 |

## Docker boundary failures: product or environment

| # | Test | Class | Reason |
| --- | --- | --- | --- |
| 1 | `MutationDockerTests.M1_TokenNoConcurrency_IsDetected` | Test defect (not product) | `LabDaemonOptions` has no `RepoRoot`, so `mutation_run` returns `the repo root is not configured` in 6 s (R3-N4). The test also uses a fake clock, does no build or `stack_up`, and runs in the parallel LabHome lane. Fixed in wave5-fix-C. |
| 2 | `TenantTwinSeamTests.TenantIsolationEnroll_Holds` | Expected red (S-L12) | The tenant-isolation experiment ships with L56. No action. |
| 3 | `TwinLatencyDockerTests.EnrollLatencyInvariance_Holds` | Test setup defect plus environment; product diagnostics defect | No `lab_build`, `stack_up` or `svc_start` before `twin_run`, so the first `stack_reset_data` hit a cold stack, possibly while another lane held the fixed-name containers (F94). `TwinRunner.ResetAsync` drops the cause, which is a product defect (wave5-fix-A). |
| 4 | `LabSelfTestE2ETests` I7 skip | Test and spec contract gap, not a product defect | D-ML101. Fixed in wave5-fix-C. |
| 5 | `SelfCheckRecoveryTests` `[Lab.Timeout]` | Cause unknown; product diagnostics defect | Candidates: (a) an elevated session, where the child refuses to start and exits, and `StartDaemonAsync` reports that as a timeout; (b) a collision with the parallel lanes (F94): the LabHome stack and the nested run of `SelfCheckE2ETests`. The reports cannot tell which. D-ML102 and D-ML103 remove both and make the next failure self-describing. |
| 6 | `EnrollHistorySeamTests` `Lab.LeaseRequired` | Test defect plus a product limit (F95) | The 600 s lease ended while a long `history_run` call ran. The teardown release then threw and hid the call's own error. The history run's own duration is unexplained until the masking is gone. Fixed in wave5-fix-C (TTL, teardown); L05b fixes the product side. |

Passed: `HunterDockerTests` (it passed partly because of F92), `SelfCheckE2ETests`, `BlastRadiusDockerTests`, `FaultSmokeE2ETests`, `ToxiproxyTimeoutToxicDockerTests`, `ToxiproxySeamTests`. The live gates (hunt, fuzz, chaos, soak, mutation, twin, coverage_report) were still running. Re-read the mutation and twin gates after wave5-fix-A and wave5-fix-D are merged.

## Fix notes ruled

| Note | Ruling | Packet |
| --- | --- | --- |
| R1-N1 | As written, both lines. | A |
| R1-N2 | As written, plus a Pass-bundle sibling test. | A |
| R1-N3 | As written. | A |
| R1 items 5, 6 | Item 5: the missing `twin` kind is an L53 omission, fixed as R3-N1 (below). Item 6: accepted; harden the sample as below. | A |
| R1 items 7, 8 | Item 7 accepted; item 8 is OD-W5-1. | A (worknote) |
| R1 item 9 | Accepted, no run needed: the reviewer's reading is enough, and wave5-fix-A runs the KnownIssues tests. | none |
| R2-N1 | Amended by D-ML101: match all three fields, not only id and source. | C |
| R2-N2 | Already done at the tip (D-ML101). No work. | none |
| R2-N3 | As written; I7 skip gives `i7 = "skipped"`, never a fail. | B |
| R2-N4 | As written. | B |
| R2-N5 | As written. | B |
| R2-N6 | Amended: wait `retry_after_s` from the probe payload when it has one, clamped to 1-60 s (FuzzJob's bound); otherwise 60 s (D-ML88). | B |
| R2-N7 | Amended: fail only when the model still accepts a token (`model.Tokens.Count < EnrollmentModel.MaxTokens`), so the fake's `{}` after 8 tokens stays legal. Also bounds-check `op.Token`. | B |
| R2-N8 | As written; also catch any other exception and log the tool name only. | B |
| R2-N9 and S-N3 | Amended: no raw secret copies. `FuzzOracle` gets a leak predicate built from `SecretScanner.HasSecret` and `InvariantWiring.DaemonTokenPredicate`, the I4 pattern, which never turns the token into a string. | B |
| R2-N10 | As written (D-ML100). | B |
| R2-N11 | Accepted with follow-up **L28b**. Writing fuzz rows through `BundleWriter` and ingesting them needs a row shape and coverage keys, which is too large for this pass. Until then the job result is the only record; the status doc says so. | L28b |
| R2-N12 | The `duration_s` cap of 900 is as written. The load-time target-name check is rejected: `FaultTargetGuard.AllowedFor` returns fault kinds per target kind, not names. The guard already refuses a non-lab target at injection, and a name list in the loader would drift from the registry. | C |
| R2-N13 | As written (D-ML99). | A |
| R2 O1, O2 | Context only. | none |
| R3-N1 | Amended to C12's shape: one `twin:` signature per pointer that did not hold, not one string per row. | A |
| R3-N2 | Amended: the texts go in `ingest.signature_texts` (`ResultsService.ToPayload`), not in a second `uat_run` field. `UatRunTool` is unchanged (D-ML95). `MutationRunJob` reads them. | A (payload), D (reader) |
| R3-N3 | As written. | C |
| R3-N3b | Keep the real clock; use `HistoryRunJob.CleanupCap + 1 s` (make the constant `internal`). Add the `HealCancelled` assert. | C |
| R3-N4 | As written, plus system clock, build, `stack_up`, `ttl_s` and the gate (D-ML103). | C |
| R3-N5 | The test half is in C. The `ResetAsync` text is in A, because A already owns `TwinRunner.cs`. | A, C |
| R3-N6 | As written. | C |
| R3-N7 | As written, plus D-ML102. | D |
| R3 L55 partial review | `SelfCheckRunner`, `SelfCheckJudge`, `TrxReader` and `SelfCheckProgram` go to the pass-2 R3 review. | none |
| S-N1 | As written; the allow-list is `history_run` and `uat_run` (every shipped product mutant's detector) plus the gate check. | D |
| S-N2 | As written (header check, fails loudly). | D |
| S-N4 | As written. | C |
| S-N5 | As written for `ChaosJob`. The same fix for the `TwinRunner` heal goes in A (the reviewer's "same Low" in Not fixed). | C, A |
| S-N6 | As written. | D |
| S-N7 | Rejected; no change. The new mutant code builds no `rm`, `rmi`, `stop` or `kill` with ids read from docker (grep: none under `Mutants/`). `LabStack` and `LabServices` use fixed `meridian-lab-` names. `DockerBuildArgs`'s only positional is a validated rooted path. Changing it would break `MutantBuilderTests.Build_UsesExactArgumentLists_AndTheMutantLabel` for no gain. | none |

## Shared rules for all four packets

Inline these. `packet-coder` skips CLAUDE.md.

- Use `Result<T>` for validation, IO and network errors. Never throw. Error texts start `[Lab.<Code>]`.
- Every file path goes through `IPathValidator` or `PathValidator`.
- Processes use `ArgumentList`, never a built command string. Every `IDisposable` and `Process` is wrapped in `using`.
- No secret value goes into a payload, a log, a report or a test assertion message. Strings from tools or processes go through `Redactor.Redact` before a file or payload.
- No package versions in `.csproj` (Central Package Management). Add no package.
- Nullable is enabled and warnings are errors: build with `-p:TreatWarningsAsErrors=true`.
- xUnit 2 conventions of `tests/Dhadgar.Lab.Tests`: `[Fact]`, `[SkippableFact]` with `Skip.If`, `FakeTimeProvider`, `LabJson.Options` for JSON. Add tests only. Change an existing test only where this note says so, and only as it says.
- Do not edit `docs/analysis/**` except your own worknote.

Each packet's checks, run from its worktree root, in this order:

```powershell
dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "<packet filter>"
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true&FullyQualifiedName!~E2ETests&FullyQualifiedName!~DockerTests"
```

The last command is the full non-Docker suite; it must be green. Docker tests run at the wave boundary (D-ML74), not in these packets.

## wave5-fix-A: results, known issues, twin signatures

Worktree `.claude/worktrees/ml-w5-fix-a`, branch `ml/w5-fix-a` from `campaign/meridian-lab`. Commit subject: `fix(lab): wave 5 results and known-issue fixes, pass 1 [L25f, L26, L52, L53]`. Size about 40k. Security review: yes (evidence kinds, known-issue matching, ingest payload).

### A1. History grade text (R1-N1, R1-N2; F90)

- `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleWriter.cs:328` and `:373`: replace `grade = bundle.Grade.ToString(),` with `grade = LedgerRenderer.ResultText(bundle.Grade),`. First grep `tests/Dhadgar.Lab.Tests` for `"Fail"` and `"Pass"` in History tests. If a test asserts the old run.json text, record it in the worknote and change that assertion to the ledger word (forced change).
- `tests/Dhadgar.Lab.Tests/Results/BundleIngestorTests.cs`: in the history test at lines 171-192, add asserts that the stored row grade is `FAIL`, the run overall is `fail`, and one stored signature text starts with `hist:`. Add `Ingest_PassingHistoryBundle_IsPassWithNoSignatures`, which uses a `RowGrade.Pass` bundle with empty violations and asserts grade `PASS`, overall `pass`, no `new_failures` and zero signatures. Both fail on the old line 328.

### A2. Issue URL digits (R1-N3)

- `src/Tools/Dhadgar.Lab/Tools/Triage/TriageTools.cs:170`: `issues/\d+\z` becomes `issues/[0-9]+\z`.
- `tests/Dhadgar.Lab.Tests/Triage/TriageMarkTests.cs`: add a case asserting that `IsIssueUrl("https://github.com/SandboxServers/MeridianConsole/issues/١٢٣")` is false.

### A3. Known issues (D-ML96, D-ML97; F91)

- `docs/guides/uat-specs/known-issues.toml`:
  - Delete KI-DV13, KI-DV12, KI-DV3, KI-DELAYED-PLUGIN and KI-94.
  - Append this comment block:

    ```toml
    # Documentation only (D-ML96): DV-13, DV-12, DV-3, the delayed-message plugin and #94 have no lab signature that shows
    # their symptom without excusing other failures. They are tracked in docs/PROJECT-STATE.md and issue #94, not here.
    ```

  - KI-RABBIT-HOST: `patterns = ['^exc:RabbitMQ\.Client\.Exceptions\.BrokerUnreachableException\|']`.
  - KI-DISCORD-SECRETS-PORT: `patterns = ['^exc:System\.Net\.Http\.HttpRequestException\|']`.
  - KI-F67 scope: `sections = ["catalog-nodes"]`, `rows = ["nodes-pg-down-enroll"]`.
  - KI-F69-HTTP: add `# Reachable through FuzzOracle (fuzz_run) only; row evidence emits http for 5xx only.`
- `tests/Dhadgar.Lab.Tests/Results/KnownIssuesShippedFileTests.cs:14`, forced change by D-ML96: `Seeds` drops KI-DV13, KI-DV12, KI-DV3, KI-DELAYED-PLUGIN and KI-94 and keeps the rest. Record it in the worknote.
- New `tests/Dhadgar.Lab.Tests/Results/KnownIssuesReachabilityTests.cs`. It loads the shipped file the way `KnownIssuesShippedFileTests` does. A dictionary maps every expected id to a `RowFailure` with `ClaimedKnownIssue = null`:

  ```csharp
  ["KI-ENROLL-RETRY"] = new("history", "enrollment", null, [SignatureBuilder.History("enrollment", "E5", "enroll-lost-response")]),
  ["KI-F67"] = new("catalog-nodes", "nodes-pg-down-enroll", null,
  [
      SignatureBuilder.Clause("catalog-nodes", "nodes-pg-down-enroll", "step:enroll-down", "NotHeld"),
      SignatureBuilder.Invariant("I1", "nodes", "unhandled"),
      SignatureBuilder.Http(new HttpEvidence("nodes", 500, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", [])),
  ]),
  ["KI-F69"] = new("catalog-nodes", "nodes-enroll-malformed", null,
      [SignatureBuilder.Clause("catalog-nodes", "nodes-enroll-malformed", "problem-json", "NotHeld")]),
  ["KI-F69-HTTP"] = new("fuzz", "enroll/1", null,
      [SignatureBuilder.Http(new HttpEvidence("nodes", 400, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", []))]),
  ["KI-NOTIF-OUTBOX"] = new("catalog-notifications", "outbox", null, [SignatureBuilder.Invariant("I2", "notifications", "outbox")]),
  ["KI-RABBIT-HOST"] = new("catalog-notifications", "broker", null, [SignatureBuilder.Exception(new ExceptionEvidence(
      "notifications", "RabbitMQ.Client.Exceptions.BrokerUnreachableException", ["at RabbitMQ.Client.ConnectionFactory.CreateConnection()"]))]),
  ["KI-DISCORD-SECRETS-PORT"] = new("catalog-discord", "secrets", null, [SignatureBuilder.Exception(new ExceptionEvidence(
      "discord", "System.Net.Http.HttpRequestException", ["at System.Net.Http.HttpConnectionPool.ConnectAsync()"]))]),
  ```

  - `EveryShippedIssue_HasAReachableSample`: for each loaded issue, the dictionary has its id. If it does not, fail with `"<id> has no reachability sample (D-ML96)"`.
  - `EverySample_MatchesItsIssue`: for each entry, `Match` returns `IssueId == id`, and `State` is neither `new` nor `claimed_mismatch`.
  - Both fail on the old file: KI-RABBIT-HOST's old pattern cannot match, and the old file has ids with no sample.
  - If a sample proves that an `HttpEvidence` or `Clause` argument means something else than shown here, fix the sample, not the pattern, and record it in the worknote.

### A4. Twin signatures (R3-N1, R1 item 5; C12 `twin:<experiment id>|<pointer>`)

- `src/Tools/Dhadgar.Lab/Uat/UatRunner.cs:38`: add `[property: JsonIgnore] IReadOnlyList<string>? TwinPointers = null` as the last parameter of `UatRowResult`.
- `src/Tools/Dhadgar.Lab/Twins/TwinRunner.cs:140-144`: set `TwinPointers = verdict.Value.Held == false ? verdict.Value.Diffs.Select(d => d.Locator).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : null`.
- `src/Tools/Dhadgar.Lab/Results/SignatureBuilder.cs`:
  - add `public static FailureSignature Twin(string experimentId, string pointer) => new("twin", "twin:" + experimentId + "|" + pointer, null, null);`;
  - add `"twin"` after `"hist"` in the `Primary` order at line 112.
- `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleWriter.cs`, row node (lines 512-539): when `row.TwinPointers` is non-empty, write `"twin": {"experiment_id": <row id without the "twin/" prefix>, "pointers": [...]}`.
- `src/Tools/Dhadgar.Lab/Results/RowVerdicts.cs`:
  - add `IReadOnlyList<FailureSignature>? Extra = null` as the last parameter of `ParsedRow`;
  - in `Parse`, read the `twin` object and build `SignatureBuilder.Twin(id, pointer)` per pointer;
  - line 61 passes `row.Extra ?? []` instead of `[]`.
- `src/Tools/Dhadgar.Lab/Results/EvidenceCoverage.cs`: move `"twin"` from `NotBuiltKinds` (line 15) to `BuiltKinds` (line 12), and update the comment at line 11. This is the same commit as the kind, per D-ML93.
- Tests:
  - `tests/Dhadgar.Lab.Tests/Twins/TwinRunnerTests.cs` `Twin_NotHeld_TwinSignatureOnBothRuns` (line 119): add asserts, nothing removed. Both ingested bundles hold a signature `twin:<id>|<pointer>` for the not-held pointer.
  - `Twin_Held_WhenBothWorldsAgree`: add an assert that no `twin:` signature exists.
  - `tests/Dhadgar.Lab.Tests/Results/EvidenceCoverageTests.cs`: add `Twin_IsBuilt`.
- `tests/Dhadgar.Lab.Tests/Observe/ReplayJudgeTests.cs:109`, forced change: `"twin:500|-|POST|/a|none"` becomes `EvidenceCoverage.NotBuiltKinds[0] + ":500|-|POST|/a|none"`. The test's intent (a not-built kind) is unchanged, and the sample now survives the next move (R1 item 6).

### A5. Signature texts in the ingest payload (R3-N2, payload half; F93)

- `src/Tools/Dhadgar.Lab/Results/BundleIngestor.cs:15`: add `IReadOnlyList<string> SignatureTexts` as the last parameter of `IngestResult`.
- `BundleIngestor.cs:208`: pass `signatureRecords.Values.Select(r => r.Text).Order(StringComparer.Ordinal).ToArray()`. These are the scrubbed texts stored in the index.
- `src/Tools/Dhadgar.Lab/Results/ResultsService.cs` `ToPayload` (line 263 on): add `["signature_texts"] = new JsonArray(result.SignatureTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray())`.
- `tests/Dhadgar.Lab.Tests/Results/ResultsServiceTests.cs`: add `ToPayload_CarriesTheStoredSignatureTexts`. It ingests a bundle with one FAIL row and an I4 `nodes` violation, using the existing fixture style, and asserts that `signature_texts` holds the same text the index stores, matching `^inv:I4\|nodes\|`.

### A6. Twin reset and heal (R3-N5 product half, S-N5 for twins)

- `TwinRunner.cs:213-219` `ResetAsync`: return `"[Lab.Unavailable] stack_reset_data failed: " + <the tool error text, cut to 300 characters>`. The dispatcher has already redacted it.
- `TwinRunner.cs:225-238` `HealAsync`: catch `Exception`, log `LogWarning("twin heal did not finish: {Tool}", "fault_reset")` and continue. No arguments in the log.
- `TwinRunnerTests`: add `Twin_ResetFails_ErrorNamesTheCause`, where the fake answers `stack_reset_data` with an error text and the failure contains it; and `Twin_HealThrows_ResultStillReturned`.

### A7. Coverage summary (R2-N13, D-ML99)

- `src/Tools/Dhadgar.Lab/Coverage/CoverageCollector.cs:11` and the `OnIngestedAsync` summary say only `invariant_check` is recorded at ingest. `route_status`, `fault_target`, `error_code` and `transition` are not collected until L52b.
- If `coverage_report` lists those four kinds as unhit, mark them `not_collected` and add `CoverageCollectorTests.NotCollectedKinds_AreNotReportedAsUnhit`.

### A filter and docs

Packet filter: `RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~BundleIngestorTests|FullyQualifiedName~TriageMarkTests|FullyQualifiedName~KnownIssues|FullyQualifiedName~TwinRunnerTests|FullyQualifiedName~EvidenceCoverageTests|FullyQualifiedName~ReplayJudgeTests|FullyQualifiedName~ResultsServiceTests|FullyQualifiedName~RowVerdictsTests|FullyQualifiedName~CoverageCollectorTests|FullyQualifiedName~UatRunToolIngestTests)`.

Docs owed: worknote `docs/analysis/meridian-lab/worknotes/wave5-fix-A.md`, with the forced test changes, OD-W5-1's default, the R1 item 7 note and the known-issue removals. No library page describes these yet; a grep outside `docs/analysis` finds none.

## wave5-fix-B: jobs, hunter, fuzz, soak

Worktree `.claude/worktrees/ml-w5-fix-b`, branch `ml/w5-fix-b`. Commit subject: `fix(lab): wave 5 job, hunter, fuzz and soak fixes, pass 1 [L27, L28, L29]`. Size about 40k. Security review: yes (job runner, lease renewal, fuzz leak oracle, daemon token predicate).

### B1. Job runner catch (R2-N4)

- `src/Tools/Dhadgar.Lab/Jobs/JobRunner.cs:303`: `catch (Exception ex) when (ex is not OperationCanceledException)` becomes `catch (Exception ex)`. Line 299 still handles a requested cancel first.
- `tests/Dhadgar.Lab.Tests/Jobs/JobRunnerTests.cs`: add `Job_UnrequestedCancellation_EndsFailed_AndTheNextStartIsAccepted`. Its work throws `new OperationCanceledException()` without `Cancel`. The test asserts state `Failed`, and that a second `Start` succeeds.

### B2. Flake keys (R2-N5)

- `src/Tools/Dhadgar.Lab/Results/FlakeCheckJob.cs:188`: `var keys = plan.Keys.Order(StringComparer.Ordinal).ToList();`.
- `tests/Dhadgar.Lab.Tests/Jobs/FlakeCheckJobTests.cs`: add `FlakeCheck_UnselectedFailingRow_IsNotAFlake`. The source run has a PASS row and a FAIL row, `rows` names only the PASS row, and the test asserts `flakes` is empty.

### B3. Hunter (R2-N6, N7, N8; F92)

All in `src/Tools/Dhadgar.Lab/Hunt/ModelHunter.cs`:

1. Line 422: `Text(violation, "rule")` becomes `Text(violation, "invariant_id")`. Also change the local's name to `invariantId`.
2. Lines 279-282, mint: `if (model.Tokens.Count < EnrollmentModel.MaxTokens) { if (Text(mint.Value, "token_ref") is not { } tokenRef) { return Infrastructure("[Lab.Unavailable] nodes_token_mint returned no token_ref"); } tokenRefs.Add(tokenRef); }`.
3. Line 298: before indexing, `if (op.Token >= tokenRefs.Count) { return Infrastructure("[Lab.Unavailable] hunt op names a token that was not minted"); }`.
4. Lines 309-312, 429 status:
   - wait `retry_after_s` from the probe payload when it is a number (clamp 1-60), otherwise 60 s;
   - the wait is `await Task.Delay(wait, _time, ct)`, then `_pacer.WaitAsync(ct)`, then `nodes_enroll_probe` again with the same arguments;
   - a second 429 returns the existing throttled `CaseResult`; any other status continues to `Compare` as normal.
5. Lines 440-444 `HealAsync`: wrap the call in `try { ... } catch (Exception) { _logger.LogWarning("hunt heal did not finish"); }`. If the class has no logger, add `ILogger<ModelHunter>? logger = null` as the last constructor parameter and use `NullLogger` when it is null. Log no ids or arguments.

Tests in `tests/Dhadgar.Lab.Tests/Hunt/ModelHunterTests.cs`:

- Forced change at lines 194-197: the fake's `invariants_check` answer becomes the real shape, `JsonSerializer.SerializeToElement(new { count = 1, violations = new[] { new InvariantViolation("I1", "nodes", "unhandled", "unhandled", DateTimeOffset.UnixEpoch, null) }, errors = Array.Empty<object>(), skipped = Array.Empty<object>() }, LabJson.Options)`. The fake's existing control over when it reports the violation stays as it is. This is forced because the fake encoded the bug (F92). Record it in the worknote.
- Add:
  - `Hunter_I1OutsidePgDown_FailsTheCase`, with the real payload shape (fails on the old `rule` read);
  - `Hunter_429ThenOk_RetriesOnce`;
  - `Hunter_429Twice_IsThrottled`;
  - `Hunter_MintWithoutTokenRef_IsInfrastructure`;
  - `Hunter_HealThrows_CaseStillEnds`.
- Advance the test's `FakeTimeProvider` past the wait, as the existing tests do for the settle.

### B4. Fuzz leak oracle (R2-N9, S-N3)

- `src/Tools/Dhadgar.Lab/Fuzz/FuzzOracle.cs` `Judge`: add `Func<string, bool>? leakCheck = null` as the last parameter. Line 98: `leaked = forbiddenMarkers.Any(...) || (leakCheck?.Invoke(observation.BodyText) ?? false)`. The finding text never contains the body or the marker.
- `src/Tools/Dhadgar.Lab/Fuzz/FuzzJob.cs` constructor (line 72): add `Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke` (required) and `Func<string, bool>? leakCheck = null`, then pass `leakCheck` at line 187.
- `src/Tools/Dhadgar.Lab/Tools/Fuzz/FuzzRunTool.cs`:
  - add `IServiceProvider services` as the last constructor parameter;
  - at call time, resolve `LabToolDispatcher` (for `invoke`, as `SoakTools.cs:107` does), `SecretVault` and `EdgeState`;
  - add `internal static Func<string, bool> LeakCheck(SecretVault vault, EdgeState edge)`, returning `body => scanner.HasSecret(body) || token(body)` with `scanner = new SecretScanner(vault)` and `token = InvariantWiring.DaemonTokenPredicate(edge)`;
  - pass both delegates at line 103. No secret is copied into a string or an array.
- Tests:
  - `FuzzOracleTests.Judge_LeakCheckTrue_IsALeak_AndTheFindingOmitsTheBody`;
  - a new `tests/Dhadgar.Lab.Tests/Fuzz/FuzzRunToolLeakCheckTests.cs`: a registered vault value is found, a body carrying the daemon token bytes is found, and a clean body is not. Build the vault and `EdgeState` the way the existing SecretScanner and I4 tests do.
- `tests/Dhadgar.Lab.Tests/Fuzz/FuzzJobTests.cs` lines 21, 38, 56, 70, 83, 96, forced change by the new required parameter: each `new FuzzJob(...)` passes `CleanInvoke`, a helper that answers `invariants_check` with `{count:0, violations:[], errors:[], skipped:[]}` and any other tool with an error. No assertion changes.

### B5. Fuzz and soak end-of-job invariants check (R2-N10, R2-N3, D-ML100)

- `FuzzJob.RunAsync`: after the last case, call `invoke("invariants_check", {since: <job start, "O">}, <context with caller "job:fuzz">, ct)`.
  - A non-empty `violations` array, an error, or a non-empty `errors` array fails the outcome.
  - The payload gains `invariants: {checked: true, violations: n, errors: m}`.
- `src/Tools/Dhadgar.Lab/Soak/SoakJob.cs`: after the `while` loop and inside the `try` (before `store.End()` in the `finally`), make the same call with `since` = soak start.
  - An I7 violation, an error, or an `errors` entry for I7 sets `overall = "fail"`.
  - An I7 entry in `skipped` sets the result field `i7 = "skipped"` and does not fail.
  - Otherwise `i7 = "checked"`.
  - A cancelled soak does not call it.
- Tests:
  - `FuzzJobTests.FuzzJob_InvariantViolationAfterCases_FailsTheJob`;
  - `SoakJobTests.SoakJob_RisingTrend_FailsTheOutcome` (the fake answers an I7 violation);
  - `SoakJobTests.SoakJob_I7Skipped_IsReportedNotFailed`.
- `tests/Dhadgar.Lab.Tests/Soak/SoakJobTests.cs:141-153` `Invoke`, forced change: answer `invariants_check` with a configurable payload, clean by default. The `uat_run` branch is unchanged.

### B filter and docs

Packet filter: `RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~JobRunnerTests|FullyQualifiedName~FlakeCheckJobTests|FullyQualifiedName~ModelHunterTests|FullyQualifiedName~Fuzz|FullyQualifiedName~SoakJobTests|FullyQualifiedName~I7)`.

Docs owed: worknote `worknotes/wave5-fix-B.md`, listing the forced changes. Note there that failing fuzz cases live only in the job result until L28b.

## wave5-fix-C: chaos, history, Docker test harness

Worktree `.claude/worktrees/ml-w5-fix-c`, branch `ml/w5-fix-c`. Commit subject: `fix(lab): wave 5 chaos, history and Docker test fixes, pass 1 [L25, L29, L53, L54, L55]`. Size about 40k, mostly mechanical. Security review: yes (chaos heal in `finally`, repo-root guards).

### C1. Chaos (R2-N12, S-N4, S-N5)

- `src/Tools/Dhadgar.Lab/Chaos/ChaosPlan.cs`:
  - add `public const int MaxFaultSeconds = 900;`;
  - line 250 uses it for `duration_s`, with the failure `[Lab.InvalidArgument] duration_s must be [min, max] with 1 <= min <= max <= 900`;
  - `quiet_s` keeps `MaxSeconds`.
- `src/Tools/Dhadgar.Lab/Tools/Chaos/ChaosTools.cs` and `src/Tools/Dhadgar.Lab/Tools/Soak/SoakTools.cs`: before resolving the plan or spec folder, `if (string.IsNullOrWhiteSpace(_paths.RepoRoot)) return Result<object>.Failure("[Lab.InvalidArgument] the repo root is not configured");`. This copies `MutationRunTool.cs:108-111`.
- `src/Tools/Dhadgar.Lab/Chaos/ChaosJob.cs` `HealAllAsync`:
  - wrap each heal call: `catch (OperationCanceledException)` keeps today's behaviour, and `catch (Exception)` logs the tool name only, adds 1 to `_healFailed` and continues;
  - the payload gains `heal_failed`.
- Tests:
  - `ChaosPlanTests.Plan_FaultDurationOver900_IsRejected`;
  - `ChaosJobTests.Heal_FirstThrows_SecondStillRuns_AndHealFailedIsOne`;
  - `ChaosToolsTests` or `SoakToolsTests` (add the file if it is absent): `EmptyRepoRoot_IsRefused` for `chaos_run` and `soak_run`.

### C2. History barrier port and tests (R3-N3, N3b)

- `src/Tools/Dhadgar.Lab/History/HistoryRunJob.cs`:
  - add `int barrierPort = LabPorts.Barrier` after `maxReruns`, stored in `_barrierPort`;
  - line 361 uses `_barrierPort`;
  - line 377 uses `proxy.BoundPort.ToString(CultureInfo.InvariantCulture)`;
  - line 131: `private static readonly TimeSpan CleanupCap` becomes `internal static readonly TimeSpan CleanupCap`.
- `HistoryRunTool.cs:412` is unchanged.
- `tests/Dhadgar.Lab.Tests/History/HistoryRunJobTests.cs`:
  - every `new HistoryRunJob(...)` passes `barrierPort: 0`;
  - line 148 test: `InjectDelay = HistoryRunJob.CleanupCap + TimeSpan.FromSeconds(1)`;
  - test 1: add `Assert.False(env.HealCancelled);` after `Assert.Equal(1, env.HealCalls);`;
  - add `RunAsync_BarrierPortHeld_StillBindsAnEphemeralPort`, which holds a `TcpListener` on 25080 and passes.

### C3. `.gitattributes` (R3-N6)

Lines 4-5 become one line:

```text
# Twin spec tests build TOML from verbatim string literals and match with LF; keep LF on Windows autocrlf checkouts.
```

Check: `git check-attr eol tests/Dhadgar.Lab.Tests/Twins/TwinRunnerTests.cs` prints `eol: lf`.

### C4. Docker gate and test setup (D-ML103, D-ML101, R3-N4, R3-N5 test half, R2-N1; F94)

1. Change `[Collection("LabHome")]` to `[Collection(LabDockerGate.Name)]` in:
   - `Mutants/MutationDockerTests.cs`;
   - `Seams/EnrollHistorySeamTests.cs`;
   - `Seams/TenantTwinSeamTests.cs`;
   - `Twins/TwinLatencyDockerTests.cs`;
   - `Uat/FaultSmokeE2ETests.cs`;
   - `Uat/LabSelfTestE2ETests.cs`.
2. Add `[Collection(LabDockerGate.Name)]` to:
   - `Faults/BlastRadiusDockerTests.cs`;
   - `Faults/FaultKindsDockerTests.cs`;
   - `SelfCheck/SelfCheckRecoveryTests.cs`;
   - `SelfCheck/SelfCheckE2ETests.cs`.
3. New `tests/Dhadgar.Lab.Tests/LabDockerGateTests.cs`, `EveryDockerClass_IsInTheDockerGate`. Using reflection over the test assembly's `CustomAttributeData`, every type is checked:
   - Which types: a `TraitAttribute("RequiresDocker", "true")` on the type or on any method, or a name in `ExplicitlyGated = ["SelfCheckE2ETests"]`.
   - The check: the type must carry `CollectionAttribute` with argument `LabDockerGate.Name`. The failure message lists the offending type names.
   - Proof: the test fails on the tip, where 10 classes are wrong.
4. `SelfCheck/SelfCheckRecoveryTests.cs`, D-ML102:
   - `[Fact]` becomes `[SkippableFact]`;
   - first line: `Skip.If(Dhadgar.Lab.Daemon.ElevationCheck.IsElevated(), "selfcheck --recovery needs a non-elevated session (D-ML102)");`;
   - the assertions are unchanged.
5. `Uat/LabSelfTestE2ETests.cs:190-198`: before the row assert, `if (skip.GetProperty("invariant_id").GetString() == "I7" && skip.GetProperty("source").GetString() == "soak" && skip.GetProperty("reason").GetString() == "no soak is running") { continue; }`. Every other skip keeps today's assertion.
6. `Mutants/MutationDockerTests.cs`, R3-N4, rebuilt on `EnrollHistorySeamTests`' sequence:
   - `RepoRoot: LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)` and `Time: TimeProvider.System` in `LabDaemonOptions`;
   - lease through `lab_lease_acquire` with `ttl_s = 3600`, replacing the 1800 s `LeaseBook.Acquire`;
   - then `lab_build` (checkout = repo root), `stack_up` with `wipe = true`, and `svc_start` for `nodes` and `gateway`;
   - line 62: `Assert.False(call.IsError, call.Payload.ToString());`;
   - `finally`: release the lease and remove lab containers without throwing, using `EnrollHistorySeamTests`' `RemoveLabContainers` pattern.
   - The M1 assertions are unchanged.
7. `Twins/TwinLatencyDockerTests.cs`, R3-N5:
   - same setup as item 6 (`ttl_s = 3600`, `lab_build`, `stack_up` with `wipe = true`, `svc_start` for `nodes` and `gateway`) before `twin_run`;
   - the `finally` release does not throw.
8. `Seams/EnrollHistorySeamTests.cs`:
   - line 57: add `["ttl_s"] = 3600`;
   - lines 110-117: the release goes through a non-throwing helper `TryReleaseAsync` that writes a failure to the test output and never throws. The assertions are unchanged.
9. `Seams/TenantTwinSeamTests.cs`: `ttl_s = 3600` on its acquire, and a non-throwing release. It stays red until L56 (S-L12).

### C filter and docs

Packet filter: `RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~Chaos|FullyQualifiedName~SoakTools|FullyQualifiedName~HistoryRunJobTests|FullyQualifiedName~LabDockerGateTests)`. Run the HistoryRunJobTests filter three times, with a second copy of it running at the same time.

The Docker tests are not run here: they compile in the build step and run at the wave boundary.

Docs owed: worknote `worknotes/wave5-fix-C.md`. It records the moved classes and the D-ML103 rule.

## wave5-fix-D: mutants and self-check

Worktree `.claude/worktrees/ml-w5-fix-d`, branch `ml/w5-fix-d`, cut **after wave5-fix-A is merged** (D2 reads `signature_texts`). Commit subject: `fix(lab): wave 5 mutant and self-check fixes, pass 1 [L54, L55]`. Size about 35k. Security review: yes (mutant detector allow-list, patch scope, child daemon diagnostics).

### D1. Detector allow-list and patch scope (S-N1, S-N2)

- `src/Tools/Dhadgar.Lab/Mutants/MutantSpecLoader.cs` `ReadDetector` (after the blank check, line 162), for product mutants:
  - `private static readonly HashSet<string> ProductDetectorTools = new(StringComparer.Ordinal) { "history_run", "uat_run" };`.
  - Refuse a name that is not in the set, or whose `ToolPolicy.GateFor(tool)` is neither `Open` nor `OwnLease`, with `[Lab.InvalidArgument] <id>: detector tool is not allowed`.
  - `lab_lease_*` and `mutation_run` are therefore refused.
  - Lab mutants (`test =`) are unchanged.
- `src/Tools/Dhadgar.Lab/Mutants/MutantBuilder.cs`: before `git apply --check` (line 176), add `internal static Result PatchTouchesOnly(IReadOnlyList<string> patchLines, IReadOnlyList<string> files)`.
  - Collect the paths from `diff --git a/<p> b/<q>`, `--- a/<p>` and `+++ b/<q>`. Strip `a/` and `b/`, skip `/dev/null`, and normalise to `/`.
  - Each path must equal an entry of `spec.Files` (ordinal comparison).
  - Refuse any line starting `rename from`, `rename to`, `copy from`, `copy to`, `old mode`, `new mode` or `GIT binary patch`.
  - The failure is `[Lab.InvalidArgument] change.patch touches a file that files does not list`.
  - Read the patch through the validated path that line 147 already resolved.
- Tests:
  - `MutantSpecTests.ProductDetector_DestructiveTool_IsRefused` (`fault_disk_fill`, `mutation_run`, `lab_lease_release` as theory data);
  - `MutantBuilderTests.Build_PatchTouchesUnlistedFile_IsRefusedBeforeGitApply` (patch on `Directory.Build.targets`, and the fake runner records no `git apply` call);
  - `MutantBuilderTests.PatchTouchesOnly_RenameOrBinary_IsRefused`.
  - `MutantSpecTests.RealMutants_LoadCleanly` and `MutantPatchesApplyTests` must stay green.

### D2. Mutation job (D-ML100, R3-N2 reader half, S-N6)

All in `src/Tools/Dhadgar.Lab/Mutants/MutationRunJob.cs`:

1. **Constructor.** Add `Redactor redactor` as the last parameter. `MutationRunTool.cs:89` resolves it from `IServiceProvider`.
2. **`RunDetectorAsync` (200-270):**
   - record `attemptStart = _time.GetUtcNow()` before each detector call;
   - on `call.IsError`, return `Unreachable(spec.Id, detector.Tool + " failed: " + <redacted error text, cut to 300>, runIds)`;
   - after a successful call, call `invariants_check` with `{since: attemptStart.ToString("O", CultureInfo.InvariantCulture)}` and the same call context;
   - if that call returns an error, the result is `Unreachable(spec.Id, "invariants_check failed: " + <redacted, cut to 300>, runIds)`;
   - otherwise append `SignatureBuilder.Invariant(invariant_id, service, check_id).Text` for each `violations[]` entry to the attempt's signatures, and add `errors.length` to a `check_errors` count written in the mutant entry.
3. **`Signatures` (322-371).** The order is:
   1. the top-level `signatures` string array (as today);
   2. the history `violations[].rule` texts (as today);
   3. `ingest.signature_texts` when it is an array;
   4. `ingest.ok == false`, which fails with `[Lab.Unavailable] the detector's run was not ingested`;
   5. a count above 0 without texts, which fails as today;
   6. otherwise empty.
4. **`WriteReportsAsync` and `Markdown`.** Every string written to `mutants.json` and `mutants.md` goes through `_redactor.Redact`: title, reason, build and check errors. Table cells escape `|` as `\|`, a backtick as `` \` ``, and CR/LF as a space.

Tests in `tests/Dhadgar.Lab.Tests/Mutants/MutationRunJobTests.cs`:

- Forced changes:
  - `FakeInvoker` (lines 325-339) gains `public string InvariantsPayload { get; init; } = """{"count":0,"violations":[],"errors":[],"skipped":[]}""";` and a switch arm `"invariants_check" => new LabToolResult(false, Payload(InvariantsPayload))`;
  - `RunAsync` (line 260) passes a `Redactor` built as `CoverageReportToolTests.cs:37` builds one.
  - No assertion changes.
- Add:
  - `Detected_when_invariants_check_reports_the_expected_inv_signature` (M4's case: history detector, pattern `^inv:I1\|nodes\|`, invariants payload with an I1 `nodes` `unhandled` violation);
  - `Invariants_check_error_is_unreachable`;
  - `Detector_error_text_reaches_the_reason`;
  - `Signatures_reads_ingest_signature_texts` (`{"ingest":{"ok":true,"signatures":1,"signature_texts":["inv:I4|nodes|x"]}}`);
  - `Signatures_reads_a_real_ResultsService_payload`, the seam test with wave5-fix-A's payload. Build `ResultsService` as `ResultsServiceTests` does, ingest a bundle with an I4 `nodes` violation, wrap `ToPayload(...)` as `{"ingest": ...}`, and assert a text matching M5's pattern `^inv:I4\|nodes\|`;
  - `Reports_redact_a_vault_value_in_the_title_and_escape_pipes`.
- `Signatures_refuses_a_count_without_texts` stays green unchanged.

### D3. Self-check recovery diagnostics (R3-N7, D-ML102)

All in `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckRecovery.cs`:

- **`RunAsync` (113).** After the Windows check and before `IsDaemonRunning`: `if (ElevationCheck.IsElevated()) return Result<IReadOnlyList<string>>.Failure("[Lab.Elevated] selfcheck --recovery needs a non-elevated session");`.
- **`Daemon.Start` (460-496).** Replace the empty stderr handler at line 480 with one that keeps the first line starting `[Lab.`: strip control characters, cut to 300, and store it in `FirstLabLine`. The field is volatile and written once.
- **`StartDaemonAsync` (332-368).** When `Process.HasExited`, return `Result<Daemon>.Failure(ExitedEarly(process.ExitCode, daemon.FirstLabLine))`.
  - Add `public static string ExitedEarly(int code, string? line) => "[Lab.ProcessFailed] the lab daemon exited with code " + code + " before answering /status: " + (line ?? "no [Lab.] line");`.
  - Lines 179-182 and 211-214 pass that failure through instead of `[Lab.Timeout]`. `[Lab.Timeout]` stays for a live child that never answers, and appends the last `GetStatusAsync` failure text when there is one.
- **Line 426.** Replace the message with `ToolFailure(tool, text)`. Add `public static string ToolFailure(string tool, string text) => "[Lab.Tool] " + tool + " failed: " + (text.Length > 300 ? text[..300] : text);`.
- New `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryDiagnosticsTests.cs`:
  - `ToolFailure_KeepsTheText_Bounded`;
  - `ExitedEarly_NamesCodeAndLine`;
  - `ExitedEarly_WithoutLine_SaysSo`.
  - Do not edit `SelfCheckRecoveryTests.cs`; wave5-fix-C owns it.

### D filter and docs

Packet filter: `RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~Mutant|FullyQualifiedName~MutationRunJobTests|FullyQualifiedName~SelfCheckRecoveryDiagnosticsTests)`. `FullyQualifiedName~Mutant` excludes `MutationDockerTests`; it matches `MutantSpecTests`, `MutantBuilderTests`, `MutantPatchesApplyTests` and `LabMutantPatchesApplyTests`.

Docs owed: worknote `worknotes/wave5-fix-D.md`, listing the forced changes and the D-ML102 messages.

## Wave plan and merge order

- One PR per packet into `campaign/meridian-lab` is not used here. Each packet is merged locally by ops as `Merge ml/w5-fix-<x> into campaign/meridian-lab (wave 5 fix <x>)`, as for the wave 5 packets.
- A, B and C start together from the tip. D is cut after A is merged.
- Merge order: A, B, C, D.

| Packet | Files it owns | Shares a file with |
| --- | --- | --- |
| A | `BundleWriter`, `TriageTools`, `known-issues.toml`, `UatRunner` (record), `TwinRunner`, `SignatureBuilder`, `RowVerdicts`, `EvidenceCoverage`, `BundleIngestor`, `ResultsService`, `CoverageCollector`, their tests, `ReplayJudgeTests`, `KnownIssuesShippedFileTests` | none |
| B | `JobRunner`, `FlakeCheckJob`, `ModelHunter`, `FuzzOracle`, `FuzzJob`, `FuzzRunTool`, `SoakJob`, their tests | none |
| C | `ChaosPlan`, `ChaosJob`, `ChaosTools`, `SoakTools`, `HistoryRunJob`, `.gitattributes`, Docker and E2E test classes, `LabDockerGateTests` | none |
| D | `MutantSpecLoader`, `MutantBuilder`, `MutationRunJob`, `MutationRunTool` (one line), `SelfCheckRecovery`, their tests | reads A's payload only |

After all four are merged, the orchestrator reruns the D-ML74 boundary set with every Docker class serialised. That covers checks 1 and 2 of `wave5-docker-run.md` plus the live mutation and twin gates. Then it asks for R1, R2, R3 and security pass 2, which includes the unread L55 files.

## Ledger changes made by this ruling

- README Findings: F90 to F95 added.
- README Decisions:
  - D-ML95 recorded (orchestrator);
  - D-ML96 to D-ML104 added;
  - D-ML80, D-ML87 and D-ML94 are amended by D-ML101 and D-ML97, and the amending rows say so.
- README Packets: rows L05b, L25g, L28b and L52b added, outline only. Each says the architect specs its steps before dispatch. Rows wave5-fix-A, B and C added as Ready, and wave5-fix-D as BlockedDependency until A is merged.
- D-ML94's row notes that D-ML97 amends its hunter half.
- `work-packets.md` is not edited. C12 already defines the `twin` text (line 803). D-ML99 supersedes L52 file 4's list of the four extra kinds, and D-ML100 adds the invariants check to the L54 spec. The README rows carry both.
