# wf-h worknote: wave5-fix-H

Packet: wave5-fix-H (ruling 2, `worknotes/wave5-ruling-2.md`). Branch `ml/wf-h-tools`.

## Done (committed in part 1, `fix(lab): wave 5 Docker-test fixes H (part 1) [wave5-fix-H]`)

- H1: `MutationDockerTests.cs` lines 97, 101, 102 changed to the packet's literal forms (state `succeeded`, verdict message, restore message). Forced test change.
- H2: `SelfCheckRecovery.cs` gets `RepoRootEnvVar`, `ApplyChildEnvironment`; `Daemon.Start` takes `repoRoot`; `StartDaemonAsync` takes `repoRoot`; `RunInCopyAsync` passes `copy`. Test `ChildEnvironment_CarriesTokenHomeAndRepoRoot` added to `SelfCheckRecoveryJudgeTests.cs`.
- H5: `CoverageReportTool.cs` description names `not_collected`. No test pinned the old text (grep of tests for the description found none).

## H3 step 1 (code, diagnosis build)

- `SelfCheckRecovery.I5Problems(JsonElement)` added with no skip acceptance. `Judge` takes `IReadOnlyList<string> i5Problems`. `HasInvariantId` deleted.
- Forced test changes in `SelfCheckRecoveryJudgeTests.cs`: `Judge_CleanRestart_HasNoProblems` passes `[]`; the theory's third case moved to the new fact `Judge_I5Problems_AreAppended`; the theory keeps its two first cases with `[]`.
- Build: Release, TreatWarningsAsErrors, `Build succeeded`, 0 errors. Unit filter: 24 passed, 0 failed.

## Environment at start of H-run 2a

- Docker reachable (29.1.3). Dev stack containers present (`postgres-630acec0`, `messaging-630acec0`, `cache-630acec0`, `postgres-db24fb3e`): not touched.
- No `meridian-lab=1` containers, no `meridian-lab-*` network, no lab daemon process, `Lab__RepoRoot` unset, session not elevated.

## H3 diagnosis

H-run 2a (`SelfCheckRecoveryTests`, `--no-build`, Lab__RepoRoot unset in the parent, which H2 makes unnecessary): 1 failed, 0 passed, 40 s. The child stack_up ran, so the H2 repo root reached the child.

The failure is `SelfCheckRecoveryTests.cs:34` (`Assert.Empty` on the problem list). The full problem list (the xunit display truncates each text, the list has exactly these two entries):

```
I5 skipped queues after the restart: stack not up (no lab credentials)
I5 skipped proxies after the restart: stack not up (no lab credentials)
```

The reason text is the constant at `I2OutboxInvariant.cs:62` (`stack not up (no lab credentials)`). No I5 `violations` entry and no I5 `errors` entry appeared.

Recorded cause: **class A** (exactly the two skips `queues` and `proxies` with the stack-not-up reason, and nothing else), the expected cause (F97). The judge false positive is the only problem. Class A is the allowed fix.

## H3 step 2 (class A), committed in part 2

Done: `I2OutboxInvariant.StackNotUpReason` constant (used by `SkipReason`, text unchanged); `I5Problems` accepts the skip pair (`queues` or `proxies`, reason = `StackNotUpReason`); new `SelfCheckRecoveryI5Tests` (7 cases: skip pair `[]`, containers skip, queues skip with another reason, I5 violation text, I5 error, I2 violation ignored, no violations array). No change to `SelfCheckRecoveryTests.cs`.

Build: Release, TreatWarningsAsErrors, `Build succeeded`, 0 errors. Unit filter: `Passed! - Failed: 0, Passed: 65`.

## H-run 2b (after part 2, Lab__RepoRoot unset)

`Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 40 s`. Recovery restart shows no toxic, lease or I5 orphan. Lab containers before and after: 0.

Note on the copy: `ExtractHeadAsync` archives HEAD, so the child daemon runs the committed tree. Code changes meant for the child must be committed before a Docker run.

## H4 diagnosis

H-run 3a (`TwinLatencyDockerTests`, `--no-build`, after the step-1 diagnosis helper): 1 failed, 0 passed, 1 m 16 s. Lab containers before and after: 0.

The `twin_run` payload: `held: false`, `overall: fail`, `twin_row` `grade: fail`, `reason: relation not held: 2 difference(s)`. Base run `b41089`, variant run `900927`. Diffs: `/rows/enroll-once/steps/enroll/status` base `ok`, variant `null`; `/rows/enroll-once/grade` base `pass`, variant `blocked`.

Base row (`rows/catalog-twin-enroll/enroll-once.json`): `grade PASS`, `reason every required clause is held`, clause `enroll-201` Held with observed 201, steps include `up` (`stack_up`, ok, 8518 ms).

Variant row (same path), redacted and trimmed to the fields the recovery needs:

- `grade`: `BLOCKED`
- `reason`: `fault twin:enroll-latency-invariance:variant:1: [Lab.BlastRadius] step inject:twin:enroll-latency-invariance:variant:1 failed`
- `faults`: `[]` (none recorded)
- `steps`: `[]` (no step ran; the inject failed before the first step)
- clause `enroll-201`: `Unverified`, `attempts: 0`, `observed: null`

The row does not carry the inner error text of the inject. The `[Lab.BlastRadius]` refusal from `FaultTargetGuard` check 1 is `target is not registered` (`FaultTargetGuard.cs:68`). That check runs before any other BlastRadius check, so the refused id must be absent from the registry. `FaultTools.ProxyTargetId` (`FaultTools.cs:116`) returns `"p:" + proxy`, and the spec passes `proxy = "p:nodes-postgres"` (`twins/enroll-latency-invariance.toml:15`), so the id is `p:p:nodes-postgres`.

Recorded cause: **class A** (variant row BLOCKED by the fault inject, no step ran, enroll status null, F96). The inner text is inferred from the code path, not read from the row. H-run 3b confirms it through the enroll `duration_ms`.

## H4 step 2 (class A)

Planned: spec args `proxy = "nodes-postgres"` in `twins/enroll-latency-invariance.toml:15` and `catalog/enroll-history.toml:25`; fixture strings in `TwinSpecTests.cs:19` and `TwinRunnerTests.cs:81`; new `ShippedSpecProxyArgsTests.cs`; anti-hollow assertion (variant enroll `duration_ms` >= 1500) after `held` is true.

## H4 step 3 (class A), committed in part 3

Anti-hollow assertion added (variant `enroll` step `duration_ms` >= 1500 once held is true, message `the latency did not reach the enroll: <ms>`). `ShippedSpecProxyArgsTests.ProxyArgs_AreC10Names` added (the test passes on the shipped specs, which were fixed). Spec args changed in `twins/enroll-latency-invariance.toml:15` and `catalog/enroll-history.toml:25`; fixtures changed in `TwinSpecTests.cs:19` and `TwinRunnerTests.cs:81` (no assertion read the value, so no assertion changed).

H-run 3b (`TwinLatencyDockerTests`, after part 3): `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 2 m 7 s`. So `held` is true and the anti-hollow assertion holds. The exact base and variant enroll `duration_ms` values were not printed by the test, so they are not recorded here; the assertion proves variant >= 1500. Lab containers before and after: 0.

## H-run 1 (MutationDockerTests, after part 3)

`Failed! - Failed: 1, Passed: 0, Total: 1, Duration: 1 m 18 s`. The failure is `MutationDockerTests.cs:101` (the verdict assertion), so the H1 state assertion at line 97 **passed** (job state `succeeded`).

The verdict message: `M1 verdict: {"id":"M1-token-no-concurrency","verdict":"survived","detector_run_ids":[5 ids],"signatures":[],"reason":"no signature matched the expected pattern","check_errors":0}`.

This is **not** KG-W5-9 (that is `detected_other`). It is a `survived` verdict, which Gate MD fails. Line 101 stays unchanged. No fix class in H covers it, so it is recorded, not fixed. The restore assertion (line 102) was not reached. Next step for the architect: a ruling on M1's survival.

Lab containers before and after: 0.

## Full non-Docker suite

`dotnet test ... --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true&FullyQualifiedName!~E2ETests&FullyQualifiedName!~DockerTests"`: `Passed! - Failed: 0, Passed: 1415, Skipped: 5, Total: 1420, Duration: 6 m 4 s`.

## Forced test changes (listed)

- H1: `MutationDockerTests.cs` lines 97, 101, 102.
- H3: `SelfCheckRecoveryJudgeTests.cs`: `Judge_CleanRestart` passes `[]`; the I5 case is the new fact `Judge_I5Problems_AreAppended`; the theory's first two cases pass `[]`.
- H5: no test pinned the old description text. No change.
- H4: fixtures in `TwinSpecTests.cs:19` and `TwinRunnerTests.cs:81` (string value only).

## Class per item

- H1: fixed (state literal and messages). M1 verdict `survived` open (see H-run 1).
- H2: fixed (H-run 2b passes with `Lab__RepoRoot` unset).
- H3: class A. Fixed (H-run 2b passes). KG-W5-7 not hit.
- H4: class A. Fixed (H-run 3b passes, held true). KG-W5-8 not hit.
- H5: fixed (description text).

## Commits

- `6e0261a2` part 1 (H1, H2, H5). `0f237148` part 2 (H3 class A). `25f74c29` part 3 (H4 class A).

## Left

- M1 `survived` verdict (new finding, needs an architect ruling; not KG-W5-9).
- Nothing else in fix-H.
