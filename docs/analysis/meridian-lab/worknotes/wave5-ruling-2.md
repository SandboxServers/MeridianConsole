# Ruling: wave 5 boundary, ruling 2

Brief: three Docker tests still fail after fixes A-G; the live gates (hunt, mutation, twin) have not been re-run since those fixes. Rule each item as fix now, park or owner decision, with a hard stop on whack-a-mole. Also decide whether wave 5 may close with Docker items parked, and set the acceptance bar.

Inputs:
- `worknotes/wave5-docker-run.md` (Continuations 4 and 6);
- `worknotes/wave5-ruling-1.md`;
- `.claude/reports/meridian-lab/wave5-diag-nodes.md` and `wave5-diag-fixF.md`;
- source read at campaign tip `fa57cd19` in `.claude/worktrees/meridian-lab`.

No source or test file was edited for this ruling.

- **Decisions:** D-ML105 to D-ML113, all Accepted by the architect except D-ML107.
- **BlockedDecision:** one, D-ML107 (fuzz 500s, product). It has a default and blocks nothing.
- **Findings:** F96 to F102. Reading the source turned up a concrete cause for two of the three "unknown" failures (F96, F97). Each fix-H diagnosis step confirms the cause live before any code changes.
- **Fix packet:** `wave5-fix-H` (one packet, about 40k, security review yes).
- **Parked follow-ups (outline rows):** L29b (soak strength), L55b (recovery after restart). Known gaps KG-W5-1 to KG-W5-11.
- **Wave close:** yes, waves 5 and 5E may close at this boundary with the listed items parked, if the bar in D-ML108 is met.

## Rulings per item

| # | Item | Ruling | Where |
| --- | --- | --- | --- |
| 1 | `MutationDockerTests.M1`: asserts `"complete"`, but the job state is `"succeeded"` | (a) Fix now. Test literal only, no product change. The `"detected"` assertion stays. | fix-H H1 |
| 2 | `SelfCheckRecoveryTests`: the child daemon has no `Lab:RepoRoot` | (a) Fix now. Lab defect in `SelfCheckRecovery`: the child env must carry the copy's root (F98). | fix-H H2 |
| 3 | `SelfCheckRecoveryTests`: "I5 reports an orphan after the restart" | (a) Fix now, diagnosis first. Leading cause: the judge counts I5 *skips* as orphans (F97). One attempt within the allowed classes, else park (KG-W5-7). | fix-H H3 |
| 4 | `TwinLatencyDockerTests`: the variant world is blocked and its enroll status is null | (a) Fix now, diagnosis first. Leading cause: the spec names the proxy `p:nodes-postgres`, the tool prefixes `p:` again, and the fault is refused before any step (F96). One attempt within the allowed classes, else park (KG-W5-8). | fix-H H4 |
| 5 | Fuzz 500s (ProblemDetailsMiddleware maps `BadHttpRequestException` to 500) | (c) Owner: D-ML107. Default: a separate product PR to `main`, outside this campaign; no known issue is added. Fuzz is informational at the boundary and runs last. | KG-W5-1 |
| 6 | Soak weak (one loop in 300 s, I7 skipped) | (b) Park to follow-up L29b. It is not a wave 5 gate. | KG-W5-2 |
| 7 | `coverage_report` shows 0 hits for route, error and transition | Expected per D-ML99. The `not_collected` flag deviation is accepted (D-ML110). fix-H H5 corrects the tool description. Feeding the kinds is L52b. | KG-W5-3 |
| 8 | The I1 run-start window starts at daemon start, so fuzz 500s leak into later runs | By design (`UatRunner.cs:186-187`). The boundary order handles it: fresh daemon, fuzz last (D-ML109). No packet. | KG-W5-4 |

## Decisions

### D-ML105: the three Docker failures go into one packet with a hard stop

`wave5-fix-H` takes items 1-4. The rules:

- Items 3 and 4 start with a **recorded diagnosis**: the coder writes the observed cause in `worknotes/wave5-fix-H.md` before changing any code for that item.
- Each item gets **one attempt** inside its allowed fix classes.
- If the recorded cause is outside those classes, or the one attempt does not turn the test green:
  - the coder stops work on that item, records the evidence, and leaves the test red and unweakened;
  - the item becomes the known gap named in its section;
  - fix-H still ships its other items.
- No fix-I follows from fix-H. A remaining item goes to the campaign-end review (D-ML74 rerun), or to an owner call through D-ML109's hard stop.

### D-ML106: how the recovery judge reads I5

`SelfCheckRecovery` stops walking the whole `invariants_check` payload for `invariant_id == "I5"`. It reads the three arrays separately:

| Array | Entry with `invariant_id == "I5"` | Judge |
| --- | --- | --- |
| `violations` | any | Problem: `I5 reports an orphan after the restart: <check_id> <service>: <detail>`, cut to 300 characters |
| `errors` | any | Problem: `I5 could not be checked after the restart: <error>`, cut to 300 characters |
| `skipped` | `source` is `queues` or `proxies`, and `reason` equals `I2OutboxInvariant.StackNotUpReason` | Accepted. The restarted daemon has no credential refs (KG-W5-5). |
| `skipped` | any other | Problem: `I5 skipped <source> after the restart: <reason>` |

A payload without a `violations` array is the problem `invariants_check returned no violations list`. This applies only once fix-H's H3 diagnosis records exactly the skip pair. If the diagnosis shows a real violation, the skip rule still lands (it is correct either way), and the violation is handled under H3's allowed classes.

### D-ML107 (BlockedDecision): fuzz 500s on malformed bodies

See the BlockedDecision table.

### D-ML108: waves 5 and 5E close at this boundary, with parked items

Waves 5 and 5E close together when all of the following hold on the campaign tip with fix-H merged:

1. **Builds and suite.** The Release build with `-p:TreatWarningsAsErrors=true` is clean, and the full non-Docker filter passes.
2. **Part A, the Docker tests, run once each (D-ML109 order).** Must pass:
   - `HunterDockerTests`
   - `SelfCheckE2ETests.SelfCheck_Lm1_IsCaught`
   - `LabSelfTestE2ETests` (S-L6)
   - `MutationDockerTests` up to and including its state assertion

   The other Docker tests either pass or fail with exactly the cause recorded in their known gap: `SelfCheckRecoveryTests` (KG-W5-7), `TwinLatencyDockerTests` (KG-W5-8), the M1 `"detected"` assertion (KG-W5-9), and `TenantTwinSeamTests` (red by plan, S-L12).
3. **Part B, the live gates, on a fresh daemon.**
   - **Wave 5 gate (hunt):** `hunt_model_run` with seed 4242 and budget 300 either finds none and says so, or finds a failure whose `repro.toml`, replayed with `hunt_model_run replay`, reproduces the same failure.
   - **Coverage, read under D-ML99 and D-ML110:** `coverage_report` runs without error; `invariant_check` reports at least one hit; and `route_status`, `fault_target`, `error_code` and `transition` each carry `not_collected: true`. This replaces "lists unhit keys" for wave 5: `invariant_check` has no universe, so it never has unhit keys.
   - **Gate MD (wave 5E):** `mutation_run` on M1, M4 and M5 ends with each verdict `detected` or `detected_other`, or `unreachable` with its named known issue open. `survived` or `stale` fails.
   - **S-L12:** red as expected (unchanged).
   - **Gate LS at wave 5E (amended):** LM1 is `caught` at the tip (Part A), and `--recovery` either passes or is KG-W5-7. The full gate LS (all eight lab mutants plus `--recovery`) stays a wave 10 gate and a campaign-end review item, unchanged. Running eight 30-minute self-checks at every boundary is out of proportion.
   - **Not gates, but recorded:** chaos, soak (KG-W5-2) and fuzz (KG-W5-1).
4. **Review.** The wave review (packet-reviewer, wave scope, reading the run record and the known-gap list) returns `ship`.

The twin latency test is L53's own Docker test, not a wave gate, so it may be parked. A parked item is listed under "Known gaps", and the campaign-end review reruns all Docker tests (D-ML74).

### D-ML109: who runs the boundary re-run, and in what order

- **Who.** The orchestrator dispatches one `packet-coder` as the boundary runner after ops merges fix-H. The runner works in `.claude/worktrees/meridian-lab` at the merged tip, in a **non-elevated** session (D-ML102), with the user's lab daemon **stopped** (D-ML90). It appends "Continuation 7" to `worknotes/wave5-docker-run.md` and commits nothing. Then `packet-reviewer` runs the wave review. A `security-advisor` read is not needed for a run with no code change.
- **Preconditions.**
  - The Release build of `src/Tools/Dhadgar.Lab`, `src/Tools/Dhadgar.Lab.SelfCheck` and `tests/Dhadgar.Lab.Tests` passes (CI=true, warnings as errors).
  - No `meridian-lab=1` containers and no `meridian-lab-net` network exist.
  - Docker is reachable.
  - No `Lab__RepoRoot` in the runner's environment, which proves H2.
- **Part A.** Run each Docker test alone with `--no-build`, in this order:
  1. `MutationDockerTests`
  2. `SelfCheckRecoveryTests`
  3. `TwinLatencyDockerTests`
  4. `HunterDockerTests`
  5. `LabSelfTestE2ETests`
  6. `SelfCheckE2ETests`
  7. `TenantTwinSeamTests` (expected red)

  A failure of a parked item does **not** stop Part B; this changes Continuation 6's rule. A failure of a must-pass test in D-ML108 item 2 does stop it.
- **Part B.** Start a fresh `meridian-lab daemon run` with a new home, then:
  1. acquire a lease (`ttl_s` 3600), `lab_build`, `stack_up wipe`, `svc_start nodes`, `svc_start gateway`;
  2. `hunt_model_run` (seed 4242, budget 300), plus a replay of its `repro.toml` if it found a failure;
  3. release the lease, then `mutation_run` on M1, M4 and M5 with no lease held (OwnLease);
  4. re-acquire, then `twin_run enroll-latency-invariance`;
  5. `chaos_run nodes-enroll` (duration_s 600);
  6. `soak_run catalog/nodes.toml` (duration_s 300);
  7. `coverage_report`;
  8. **last**, `fuzz_run` with 50 enroll cases.

  Record each job id, verdict and signature texts. Teardown is label-only: `stack_down wipe`, `docker rm -f` by id of labelled containers, then the network. No prune, and the dev stack is untouched.
- **Hard stop at the boundary.** If a must-pass item fails, the orchestrator does **not** dispatch another fix packet. It asks the architect for ruling 3, which may only:
  - (i) park the item with an owner BlockedDecision ("close wave 5 with this gap?", default: keep wave 5 open); or
  - (ii) spec one fix whose cause the run record already names with evidence.

### D-ML110: the `not_collected` deviation is accepted

wave5-fix-A A7 kept `unhit` lists for the four kinds that are not collected and added `not_collected: true` (`CoverageReportTool.cs:118-125`; `worknotes/wf-a.md:23`). That is accepted: the flag is in every dimension, and an existing test pins the `unhit` shape. D-ML99's "never unhit" is amended to "never unhit without `not_collected: true`". The tool description (`CoverageReportTool.cs:65`) does not mention the flag; fix-H H5 adds it. Readers of the report, such as the boundary runner and the status doc, ignore `unhit` on a `not_collected` dimension.

### D-ML111: spec fault args name proxies by their C10 name

A `proxy` argument in any shipped spec (catalog, twin, history or chaos) is a `ProxyCatalog.All` name such as `nodes-postgres`, never a target id such as `p:nodes-postgres`. The fault tools build the target id themselves (`FaultTools.cs:116,225,263`). New test `ShippedSpecProxyArgsTests` enforces this over `docs/guides/uat-specs/**/*.toml` (fix-H H4). It fixes F96 in `twins/enroll-latency-invariance.toml:15` and the latent copy in `catalog/enroll-history.toml:25`.

### D-ML112: weak soak, follow-up L29b (parked)

A soak with fewer than three workload loops, or fewer resource samples than I7 needs to judge a trend, must not read as a plain pass. Follow-up **L29b**, outline only:

- `SoakJob` ends such a run with `outcome = "weak"` and a reason naming loops and samples (UNVERIFIED in the ledger), and never `pass`;
- the boundary soak uses `duration_s` of at least three times one spec loop, or a shorter soak spec.

Until L29b, soak is not a gate, and a soak result with one loop is recorded as weak.

### D-ML113: recovery after restart, follow-up L55b (parked)

Follow-up **L55b**, outline only, due before the wave 10 gate LS:

- the restarted daemon rehydrates the stack's credential refs from `SecretVault` (or marks the stack created from the persisted registry), so I2 and the I5 queue and proxy checks run after a restart and D-ML106's accepted skip pair goes away;
- the recovery judge reads Toxiproxy's toxics on `nodes-postgres` directly, as well as `faults_active`.

L55b touches secret handling (security review: yes). The architect specs it before dispatch.

## BlockedDecision (owner)

| ID | Question | Default until ruled |
| --- | --- | --- |
| D-ML107 | Fuzz cases with malformed JSON bodies get 500 from Nodes through the Gateway, because `ProblemDetailsMiddleware` maps `BadHttpRequestException` to 500 (`wave5-diag-nodes.md` cause 1). Fix the product now in a separate PR to `main` (map `BadHttpRequestException` to its own status code, 400, as `application/problem+json`, with a ServiceDefaults test), or leave it and add a known issue? | Separate product PR to `main`, outside this campaign, scheduled by the owner. No known issue is added: under D-ML96 a fuzz-only `http:` 500 excuse would also hide real 500s on the enroll route. Fuzz stays informational at the boundary and runs last (D-ML109), and its failing cases stay new failures in the job result. |

## Findings

| ID | Finding | Evidence |
| --- | --- | --- |
| F96 | The twin variant's fault is refused before any step runs. The spec passes `proxy = "p:nodes-postgres"`, and `FaultToxicAddTool` builds the target id `ProxyTargetId(args.Proxy)`, which gives `p:p:nodes-postgres`. `FaultTargetGuard` refuses that with `[Lab.BlastRadius] target is not registered`. The row is BLOCKED with no step results, so the enroll status is null and `enroll-201` is Unverified. This matches Continuation 6 exactly. Every catalog spec uses the bare name. `enroll-history.toml` scenario `enroll-race-pg-latency` has the same defect, latent because no test runs that scenario. In Continuation 4 the I1 and I6 failures masked it. | `twins/enroll-latency-invariance.toml:15`; `catalog/enroll-history.toml:25`; `Tools/Faults/FaultTools.cs:116,225`; `Faults/FaultTargetGuard.cs:68`; `Uat/RowExecutor.cs:120-124,220-223`; `catalog/nodes.toml:24,119`; `wave5-docker-run.md:112,140` |
| F97 | The recovery judge probably reports a false I5 orphan. `HasInvariantId` walks the whole `invariants_check` payload, and `errors[]` and `skipped[]` entries also carry `invariant_id`. After a restart `LabCredentialRefs` is empty (it is in memory), so `StackCreated` is false and I5 adds two skips (`queues`, `proxies`, "stack not up (no lab credentials)"). That alone makes `i5 = true`. The problem text is fixed and names no check, so the run could not tell. | `Lab.SelfCheck/SelfCheckRecovery.cs:66-68,277,282-314`; `Tools/Invariants/InvariantsCheckTool.cs:59-67`; `Invariants/InvariantTypes.cs:37,40`; `Invariants/I5OrphanInvariant.cs:162-167`; `Invariants/I2OutboxInvariant.cs:61-62`; `Invariants/InvariantWiring.cs` `StackCreated`; `Stack/LabConnectionStrings.cs` `LabCredentialRefs` |
| F98 | The recovery child daemon gets no repo root. `Daemon.Start` sets only the token and the home. The daemon reads `options.RepoRoot ?? settings["Lab:RepoRoot"]`, so `stack_up` refuses with `Lab:RepoRoot is not set` unless the parent shell happens to export `Lab__RepoRoot`. | `Lab.SelfCheck/SelfCheckRecovery.cs:524-539`; `Daemon/LabDaemon.cs:88`; `Stack/LabStack.cs:381`; `wave5-docker-run.md:138-139` |
| F99 | `MutationDockerTests` expects job state `"complete"`, but job states are the lower-case `JobState` names (`running`, `succeeded`, `failed`, `cancelled`). | `tests/.../Mutants/MutationDockerTests.cs:97`; `Jobs/JobRunner.cs:423,459` |
| F100 | The run-start checkpoint covers the time from daemon start, by design. So a fuzz job's 500s (I1 unhandled) on a shared daemon appear in the start checkpoint of every later `uat_run`, twin world and mutation detector run. Continuation 4 ran fuzz before twin and mutation. | `Uat/UatRunner.cs:186-187`; `wave5-diag-nodes.md` cause 4 |
| F101 | `coverage_report` lists `unhit` keys for the four kinds that are not collected (with `not_collected: true`), and its description does not mention the flag. Continuation 4 read "route_status 0/72" as coverage. | `Tools/Coverage/CoverageReportTool.cs:28-31,65,118-125`; `worknotes/wf-a.md:23`; `wave5-docker-run.md:113` |
| F102 | The boundary soak ran one loop of `catalog/nodes.toml` in 300 s, so it took one sample, and I7 skipped. The job reported success. | `wave5-docker-run.md:110` |

## Shared rules for wave5-fix-H

Inline these; `packet-coder` skips CLAUDE.md.

- Use `Result<T>` for validation, IO and network errors. Never throw. Error texts start `[Lab.<Code>]`.
- Every file path goes through `IPathValidator` or `PathValidator`. Test-only file reads under the repo root may use `LabIsolationTests.FindRepoRoot` and `Path.Combine`, as existing tests do.
- Processes use `ArgumentList`, never a built command string. Every `IDisposable` and `Process` is wrapped in `using`.
- No secret value goes into a payload, a log, a report, a worknote or a test assertion message. The token stays out of every message. Bundle texts read through `bundle_read` are already redacted.
- No package versions in `.csproj` (Central Package Management). Add no package.
- Nullable is enabled and warnings are errors: build with `-p:TreatWarningsAsErrors=true`.
- xUnit 2 conventions of `tests/Dhadgar.Lab.Tests`: `[Fact]` or `[Theory]`, `[SkippableFact]` with `Skip.If`, `LabJson.Options` for JSON, and `[Collection(LabDockerGate.Name)]` on Docker classes (D-ML103). Change an existing test only where this note says so, and only as it says.
- Do not edit `docs/analysis/**` except your own worknote `worknotes/wave5-fix-H.md`.
- **Never** do any of these to make a red test green:
  - weaken an assertion;
  - add a skip;
  - add `allow_invariants`;
  - lower the twin latency;
  - drop a twin compare pointer;
  - filter I5 by `check_id`.

  If an item cannot be fixed within its allowed classes, stop that item (D-ML105).

## wave5-fix-H: boundary Docker fixes

Worktree `.claude/worktrees/ml-w5-fix-h`, branch `ml/w5-fix-h` from `campaign/meridian-lab` at the tip (`fa57cd19` or later). Commit subject: `fix(lab): wave 5 boundary Docker fixes [L53, L54, L55]`. Size about 40k. Security review: **yes** (child daemon environment in `SelfCheckRecovery`, the recovery judge, fault spec arguments).

Order: H1, H2, H5 (no diagnosis needed), then H3 and H4 (diagnosis first). The Docker runs in H3 and H4 need Docker up, a non-elevated session, the user's lab daemon stopped, and no labelled containers. Set `MERIDIAN_LAB_S6_WIPE=1` only if leftovers exist.

### H1. M1 state literal (F99)

- `tests/Dhadgar.Lab.Tests/Mutants/MutationDockerTests.cs:97`: `Assert.Equal("complete", record.State);` becomes `Assert.True(record.State == "succeeded", "job " + jobId + " ended " + record.State + ": " + record.Result?.ToJsonString());`.
- Line 101: keep `"detected"`, and give the failure the mutant entry text: `Assert.True(mutant["verdict"]!.GetValue<string>() == "detected", "M1 verdict: " + mutant.ToJsonString());`.
- Line 102: `Assert.True(record.Result["restore"]!["ok"]!.GetValue<bool>(), "restore: " + record.Result["restore"]!.ToJsonString());`.
- These are forced test changes; the worknote lists them. This is a Docker test, so its test is H-run 1 below. Revert check: put `"complete"` back and the assertion fails on `succeeded`.

### H2. Recovery child gets the copy's repo root (F98)

In `src/Tools/Dhadgar.Lab.SelfCheck/SelfCheckRecovery.cs`:

- Add, near `Daemon`:

  ```csharp
  /// <summary>The configuration key the child daemon reads its repo root from (LabDaemon.cs:88), as an environment variable.</summary>
  internal const string RepoRootEnvVar = "Lab__RepoRoot";

  /// <summary>
  /// Sets the child daemon's environment: the fresh token (A2), the temporary home and the recovery copy's root, so stack_up
  /// finds its files without relying on the caller's shell.
  /// </summary>
  internal static void ApplyChildEnvironment(IDictionary<string, string?> environment, string home, string token, string repoRoot)
  {
      environment[LabDefaults.TokenEnvVar] = token;
      environment[LabDefaults.HomeEnvVar] = home;
      environment[RepoRootEnvVar] = repoRoot;
  }
  ```

- `Daemon.Start(string dll, string home, string token)` becomes `Start(string dll, string home, string token, string repoRoot)`. Lines 537-539 become one call: `ApplyChildEnvironment(info.Environment, home, token, repoRoot);`.
- `StartDaemonAsync` (line 361) gains `string repoRoot` after `home` and passes it to `Daemon.Start`. In `RunInCopyAsync`, lines 189 and 223 pass `copy`. The copy is what `ExtractHeadAsync` wrote, and it has `docs/` and `tools/lab/`.
- Test: add `ChildEnvironment_CarriesTokenHomeAndRepoRoot` to `tests/Dhadgar.Lab.Tests/SelfCheck/SelfCheckRecoveryJudgeTests.cs`. It calls `ApplyChildEnvironment(new ProcessStartInfo().Environment, "h", "t", "r")` and asserts all three keys. With `Lab__RepoRoot` pre-set to `"inherited"` in the dictionary, the value afterwards is `"r"`. Revert check: drop the third line and the test fails on the missing key. The class is pure; `InternalsVisibleTo` exists (`worknotes/wf-e.md:8`).

### H3. Recovery I5: diagnosis first (F97, D-ML106)

**Step 1, diagnosis (record before any other H3 change).**

- In `SelfCheckRecovery.cs`, add:

  ```csharp
  /// <summary>Every I5 entry of an invariants_check payload, as problem texts (D-ML106). Empty when I5 found nothing.</summary>
  internal static IReadOnlyList<string> I5Problems(JsonElement payload)
  ```

  For now it has no skip acceptance: every I5 entry in `violations`, `errors` and `skipped` becomes a problem with the D-ML106 texts. The skip text is `I5 skipped <source> after the restart: <reason>`. Each text is cut to `MaxDiagnosticText`. Read fields with `TryGetProperty` and check `ValueKind` first (as `TwinRunner.ErrorDetail` does). A payload with no `violations` array gives `invariants_check returned no violations list`.
- `Judge(bool faultsEmpty, bool leaseHeld, bool i5Violated)` becomes `Judge(bool faultsEmpty, bool leaseHeld, IReadOnlyList<string> i5Problems)`. It keeps the fault and lease texts, then `problems.AddRange(i5Problems)`.
- `JudgeRestartAsync` line 277 becomes `IReadOnlyList<string> i5 = I5Problems(invariants.Value!.RootElement);`. Delete `HasInvariantId` (it has no other caller).
- Forced test change in `SelfCheckRecoveryJudgeTests`: `Judge_CleanRestart_HasNoProblems` passes `[]`. The theory's third case becomes a separate fact, `Judge_I5Problems_AreAppended`, which passes `["I5 reports an orphan after the restart: orphan-container meridian-lab-x: d"]` and asserts that single text. The theory keeps its first two cases with `[]`.
- Build, then run `SelfCheckRecoveryTests` once (H-run 2a). Paste the full problem list into the worknote under "H3 diagnosis".

**Step 2, act on the recorded cause.** The allowed classes:

| Recorded cause | Allowed fix | Test that must fail on revert |
| --- | --- | --- |
| A. Exactly the two skips `queues` and `proxies` with "stack not up (no lab credentials)", and nothing else (expected) | Judge false positive (D-ML106). In `src/Tools/Dhadgar.Lab/Invariants/I2OutboxInvariant.cs`, add `public const string StackNotUpReason = "stack not up (no lab credentials)";` and use it in `SkipReason` (no text change). `I5Problems` accepts a `skipped` entry when `source` is `queues` or `proxies` and `reason == I2OutboxInvariant.StackNotUpReason`. | New `SelfCheckRecoveryI5Tests` (pure, `JsonDocument.Parse` payloads): (1) the skip pair alone gives `[]`; (2) a `skipped` I5 entry with source `containers` is a problem; (3) a `queues` skip with another reason is a problem; (4) an I5 violation gives its `check_id` and `service` in the text; (5) an I5 error is a problem; (6) an I2 violation is ignored; (7) no `violations` array is a problem. Case 1 fails without the acceptance, and 2 and 3 fail if it is widened. |
| B. An I5 `violations` entry whose cause is a lab recovery defect in the restarted daemon's start path (registry load, `FaultBook.RecoverAsync`, persisted stack state) | Fix that start-path defect so the orphan is gone after a restart, with a unit test on fakes reproducing the recorded entry (for example, a registry reloaded from `targets.json` that misses a container the first daemon registered). The class-A skip rule also lands. | The new unit test, plus cases 1-7 above. |
| C. An I5 `violations` entry that is an I5 false positive (for example a name-form mismatch between `target.Name` and docker `{{.Names}}` in `orphan-container`) | Fix the matcher in `src/Tools/Dhadgar.Lab/Invariants/I5OrphanInvariant.cs` with a test built on the exact recorded payload; existing `I5OrphanInvariantTests` stay green unchanged. The class-A rule also lands. | The matcher test, plus cases 1-7. |
| Anything else (an I5 error, a real orphan from the test's own setup, more than one distinct cause) | None. Land class A's rule only if the skip pair is present, record the remaining entries, and park as KG-W5-7. | Cases 1-7. |

Forbidden: any change to `SelfCheckRecoveryTests.cs` assertions, a filter by `check_id`, a new skip in I5, or making `StackCreated` true without real credentials (that is L55b).

**Step 3.** Rebuild and run `SelfCheckRecoveryTests` once (H-run 2b), with `Lab__RepoRoot` **unset** in the shell (this proves H2). Record pass, or the remaining problem list and the KG-W5-7 note.

### H4. Twin variant fault: diagnosis first (F96, D-ML111)

**Step 1, diagnosis (record before the spec change).**

- `tests/Dhadgar.Lab.Tests/Twins/TwinLatencyDockerTests.cs`: before the `held` assertion at line 93, when `held` is not `True`, read both worlds' row files:
  - call `bundle_read` with `run_id = twin.RootElement.GetProperty(world).GetProperty("run_id").GetString()`, where `world` is `"base"` or `"variant"`, and `path = "rows/catalog-twin-enroll/enroll-once.json"`. These are the `twin_run` payload fields at `TwinRunTool.cs:252-253`; the row path is from `BundleWriter.cs:556`;
  - append each `text` (or the read error) to the assertion message, cut to 4000 characters per world;
  - put the reads in a `try`, so a read failure never hides the twin failure.

  Add a private helper `ReadRowAsync(Uri baseAddress, string token, string runId)` that uses the existing `CallToolAsync`. `bundle_read` is Open, so no lease is needed.
- Run `TwinLatencyDockerTests` once (H-run 3a). Record in the worknote the variant row's `reason`, its `faults` entries and its `steps` (ids, `ok`, `error`, `duration_ms`).

**Step 2, act on the recorded cause.** The allowed classes:

| Recorded cause | Allowed fix |
| --- | --- |
| A. The variant row is BLOCKED by the fault inject with `target is not registered` (expected, F96) | Spec args (D-ML111): `twins/enroll-latency-invariance.toml:15` and `catalog/enroll-history.toml:25` both use `proxy = "nodes-postgres"`. In `tests/Dhadgar.Lab.Tests/Twins/TwinSpecTests.cs:19` and `TwinRunnerTests.cs:81`, change the fixture strings to `nodes-postgres`; if an assertion reads that value, update it to match. Add `tests/Dhadgar.Lab.Tests/Uat/ShippedSpecProxyArgsTests.cs` (described below). |
| B. After class A, the variant fails because setup runs under the latency: `stack_up`, `svc_start` or `nodes_token_mint` errors or exceeds its budget. Twin extra faults always inject before the first step (`RowExecutor.cs:121`). | The experiment shape, keeping both compare pointers and 1500 ms: either (i) add a `[pre]` section to the twin spec (`TwinPre`, `TwinSpec.cs:32,137-157`) with a new setup row in `catalog/twin-enroll.toml` that runs `up`, `start-nodes` and `start-gateway`, while `enroll-once` keeps only `mint` and `enroll`. Pre rows run without the extra faults (`UatRunner.cs:194-220`), and the base world runs the setup row as part of its row list. Or (ii) if `[pre]` cannot express it, stop and park. Do not edit `RowExecutor` fault timing in this packet. |
| C. After class A, the variant reaches the enroll, and the enroll through the Gateway is not 201 under 1500 ms DB latency (for example 504 or 500) | None. This is a product finding, not a lab defect: the twin did its job. Record the status, the Gateway and Nodes log lines from the bundle, and park as KG-W5-8 with a new finding id for the next ruling. The test stays red. |
| D. Anything else | None. Record it and park as KG-W5-8. |

`ShippedSpecProxyArgsTests.ProxyArgs_AreC10Names` (class A): find the repo root with `LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)`, enumerate `docs/guides/uat-specs/**/*.toml`, and match `proxy\s*=\s*"([^"]+)"`. Assert that every capture is a `ProxyCatalog.All` name; the message lists `file:line value`. Also assert at least one match was found, so the test cannot pass empty. Revert check: restore either `p:` value and the test fails naming that file.

**Anti-hollow assertion (lands with class A).** After `held` is `True`, read the variant row as in step 1 and assert that its `enroll` step `duration_ms` is at least 1500, with the message `the latency did not reach the enroll: <duration_ms>`. This proves the toxic was in place for the enroll and was not reset by the row's own `stack_up`. If it fails, that is class B or D, not a reason to drop the assertion.

**Step 3.** Rebuild and run `TwinLatencyDockerTests` once (H-run 3b). Record held, the base and variant enroll `duration_ms`, or the KG-W5-8 note. Only one class-B attempt is allowed; if it does not hold after that, park.

### H5. `coverage_report` description (F101, D-ML110)

- `src/Tools/Dhadgar.Lab/Tools/Coverage/CoverageReportTool.cs:65`: the description becomes `"Behavioural coverage over the runs since since_run_id (default all). kind: route_status, fault_target, error_code, invariant_check or transition. Returns {dimensions:[{kind,total,hit,unhit,not_collected}], outside_universe}. A dimension with not_collected = true is not fed yet (route_status, fault_target, error_code, transition until L52b): its unhit list is not coverage."`
- Test: none new. `ToolPolicy` and the tool-list tests must stay green. If a test pins the old description text, update only that string (forced change; list it in the worknote).

### H checks

Run from the worktree root, in order:

```powershell
dotnet build tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/Tools/Dhadgar.Lab.SelfCheck/Dhadgar.Lab.SelfCheck.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresNetwork!=true&(FullyQualifiedName~SelfCheckRecoveryJudgeTests|FullyQualifiedName~SelfCheckRecoveryI5Tests|FullyQualifiedName~ShippedSpecProxyArgsTests|FullyQualifiedName~TwinSpecTests|FullyQualifiedName~TwinRunnerTests|FullyQualifiedName~I5Orphan|FullyQualifiedName~I2Outbox|FullyQualifiedName~CoverageReportTool|FullyQualifiedName~ToolPolicy)"
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true&FullyQualifiedName!~E2ETests&FullyQualifiedName!~DockerTests"
```

The fourth command is the full non-Docker suite, and it must be green. Then the Docker runs, one at a time, with `--no-build`. These are the exception to "Docker at the boundary", because H3 and H4 need live evidence:

```powershell
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MutationDockerTests"
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SelfCheckRecoveryTests"
dotnet test tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TwinLatencyDockerTests"
```

- **H-run 1 (M1).** Record the state and the verdict. If the state assertion passes but the verdict is `detected_other`, that is KG-W5-9: record the signature texts, and do not change line 101.
- **H-run 2a, 2b and 3a, 3b.** As described in H3 and H4.
- **Between Docker runs.** Check `docker ps -aq --filter label=meridian-lab=1 --filter name=^meridian-lab-` is empty. Remove leftovers label-only by id.
- **Run limits.** A Docker run never repeats more than the steps listed. A third run of the same test is not allowed.

Docs owed: `worknotes/wave5-fix-H.md` with:

- the forced test changes (H1, H3, H5);
- "H3 diagnosis" and "H4 diagnosis", with the recorded payloads, redacted;
- the class chosen for each item, or the KG it parks;
- every H-run's result line;
- a "Left" line.

## Known gaps

| ID | Gap | Until | Owner of the fix |
| --- | --- | --- | --- |
| KG-W5-1 | Fuzz cases with malformed bodies get 500 (`ProblemDetailsMiddleware`). Fuzz is informational; its failures stay new failures. | D-ML107 ruled and the product PR merged | Owner (product PR to `main`) |
| KG-W5-2 | Soak can report success on one loop and one sample (I7 skipped). | L29b | L29b |
| KG-W5-3 | `route_status`, `fault_target`, `error_code` and `transition` are not collected. Their `unhit` lists carry `not_collected: true` and are not coverage. | L52b (needs L25g) | L52b |
| KG-W5-4 | The run-start checkpoint covers the time since daemon start, so an earlier job's violations show in later runs on the same daemon. By design. Boundary runs use a fresh daemon and put fuzz last. | permanent (by design) | none |
| KG-W5-5 | After a daemon restart the credential refs are gone. Until the next `stack_up`, I2 and the I5 queue and proxy checks skip, and the recovery judge accepts exactly that skip pair (D-ML106). | L55b | L55b |
| KG-W5-6 | The recovery judge checks "no toxic" through `faults_active` (FaultBook), not through Toxiproxy itself. | L55b | L55b |
| KG-W5-7 | Conditional: if fix-H H3 hits its hard stop, `SelfCheckRecoveryTests` stays red. Gate LS's recovery leg is then UNVERIFIED until L55b or the campaign-end review. | L55b or campaign-end review (D-ML74) | next architect ruling |
| KG-W5-8 | Conditional: if fix-H H4 hits its hard stop, `TwinLatencyDockerTests` stays red. A class-C result is a product finding, not a lab gap. | campaign-end review (D-ML74) | next architect ruling |
| KG-W5-9 | Conditional: M1's `mutation_run` verdict is `detected_other` rather than `detected`. Gate MD accepts that, but `MutationDockerTests` line 101 stays red. The signature texts go into the run record. | campaign-end review (D-ML74) | next architect ruling |
| KG-W5-10 | Only LM1 of the eight lab mutants has run end to end. The full `selfcheck` (LM1-LM8) has not. | wave 10 gate LS | wave 10 run |
| KG-W5-11 | No test runs history scenario `enroll-race-pg-latency`. After H4 only `ShippedSpecProxyArgsTests` covers its fault args. | L25f or the campaign-end review | L25f |

A conditional gap (KG-W5-7, 8, 9) is live only if the fix-H worknote or the boundary run record says it was hit. Otherwise the orchestrator marks it "not hit" in RESUME.

## Wave plan and merge order

- One packet, fix-H, merged locally by ops as `Merge ml/w5-fix-h into campaign/meridian-lab (wave 5 fix H)`. Then `packet-reviewer` and `security-advisor` run on fix-H, with at most two fix-note passes before a ruling.
- Then the boundary runner (D-ML109) runs, and then the wave review (D-ML108 item 4).
- Files fix-H owns:
  - `SelfCheckRecovery.cs` and its tests;
  - `I2OutboxInvariant.cs` (one constant);
  - `I5OrphanInvariant.cs` (class C only);
  - the start-path file for class B, named in the worknote;
  - `CoverageReportTool.cs` (description);
  - `twins/enroll-latency-invariance.toml`, `catalog/enroll-history.toml` and `catalog/twin-enroll.toml` (class B only);
  - `MutationDockerTests.cs`, `TwinLatencyDockerTests.cs`, `TwinSpecTests.cs`, `TwinRunnerTests.cs`;
  - the new `ShippedSpecProxyArgsTests.cs` and `SelfCheckRecoveryI5Tests.cs`.
- No other packet is open on these files.

## Ledger changes made by this ruling

- README Findings: F96 to F102 added.
- README Decisions:
  - D-ML105 to D-ML113 added;
  - D-ML107 is a BlockedDecision with a default;
  - D-ML110 amends D-ML99;
  - D-ML108 amends the wave 5 and wave 5E gates (coverage reading; gate LS at 5E).
- README Packets:
  - rows wave5-fix-A, B, C and D set to Integrated;
  - rows wave5-fix-E, F and G added as Integrated;
  - row wave5-fix-H added as Ready;
  - rows L29b and L55b added, outline only.
- README Gates: the wave 5 and 5E lines point to D-ML108.
- README: new section "Known gaps" (KG-W5-1 to KG-W5-11).
- `work-packets.md` and `RESUME.md` are not edited. RESUME is the orchestrator's; it is stale (it still says "plan only").
