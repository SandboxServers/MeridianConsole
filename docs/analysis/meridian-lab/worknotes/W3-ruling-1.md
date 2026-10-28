# W3-ruling-1: wave 3 review pass 1 and security pass 1

Inputs: `.claude/reports/meridian-lab/wave3-review-1.md` (verdict `fix`, N1-N10) and `.claude/reports/meridian-lab/wave3-security-1.md` (verdict `decision`, N1-N5). Campaign tip `e996406`. Lab tests at the tip: 552 pass, 0 fail, 4 skip (non-Docker). Consistent with W2-ruling-3, L17-ruling-1 and D-ML66.

Outcome: one new decision (D-ML67, attestation), one scope ruling (D-ML68, C9 narrowing for wave 3), three fix packets W3-fix-A, W3-fix-B and W3-fix-C that touch disjoint files and run in parallel, one follow-up packet L16f, and one owner action (O2). No BlockedDecision.

Owner decision recorded here: **D-ML39 = one PR at the end** (owner, 2026-10-28). D-ML16 stands: the campaign ships as one PR after L47.

## Facts

| ID | Fact | Evidence |
| --- | --- | --- |
| F57 | `uat_attest` never reads the caller. Every MCP call has `Caller = "mcp"` (`Mcp/LabMcpServer.cs:212`), so any MCP client holding the token and the lease can attest. The only barriers are the driver's tool list (D-ML18) and one sentence in `.claude/skills/lab-uat/SKILL.md:48`. A second token in the user environment would not change that, because shell-capable agents run as the same user and read the same environment. | `Tools/Uat/UatReportTools.cs:154`; security N1 |
| F58 | `BundleAttestor` regrades from stored clause states only (`BundleAttestor.cs:113-118`). The runner's synthetic `(true, NotHeld)` input for an `expect_error` step that succeeded (`UatRunner.cs:317-320`) is never stored, so attesting a human clause on that FAIL row produces PASS. The same path turns a SKIPPED row into PASS: the regrade passes `ran: true` whatever the stored grade. A hidden failing input can only produce FAIL, because FAIL outranks UNVERIFIED and NEEDS_HUMAN. | security N2; `RowGrader.cs:51` |
| F59 | The bundle is redacted and scanned as serialised JSON text (`BundleWriter.cs:281`, `BundleAttestor.cs:233`, `SecretScanner.cs:63`). A vault value with `"`, `\`, a control character or a non-ASCII or HTML-sensitive character is written escaped by `JavaScriptEncoder.Default`, so neither the redactor nor the scanner sees it. This is latent while vault values are base64url or hex. | security N4 |
| F60 | The daemon under S-L6 starts with an empty target registry, so its `stack_up wipe` cannot see containers left by an earlier daemon. The test works around that with a label sweep that force-removes every `meridian-lab=1` container on the host, which would also destroy a live lab. | review N1, N10; security N5 |

## Rulings

### D-ML67. Attestation: who may attest, and what is recorded

Rules on security N1. It stays inside the accepted boundary (same-user processes are trusted) and needs no owner question. The owner may override with option (b).

1. **Option (b), a second credential, is rejected.** `MERIDIAN_LAB_ATTEST_TOKEN` would live in the same user environment as `MERIDIAN_LAB_TOKEN`, and any agent with a shell can read it (F57). A console prompt in a CLI verb is bypassed by calling `/mcp` directly. Neither separates the agent from the user.
2. **Human presence is enforced in the harness: owner action O2.** The owner adds `mcp__meridian-lab__uat_attest` to `permissions.ask` in `.claude/settings.json`. Claude Code then shows the user every attest call, with its exact arguments, before it runs. Agents never edit settings, so this is an owner action like O1. A session that bypasses permission checks also bypasses O2; the lab README says so.
3. **The designed flow is unchanged (L18).** The main session asks the user what they saw, relays the user's verdict through `uat_attest`, and the user approves the call (O2). `uat-driver` never has the tool (D-ML18, pinned by `DriverToolListTests`).
4. **Attribution.** Each attestation records `lease_owner` (the owner of the lease the call carried) and `caller` (`LabCallContext.Caller`). The ledger shows one line per attestation: `- Attested: <clause_id> <verdict> by <lease_owner> via <caller> at <time>; note: <note>`.
5. **Vocabulary.** The verdict follows the contract: `held` or `not_held`. `pass` and `fail` are refused.
6. **Which rows.** Only a row whose stored grade is `NEEDS_HUMAN` or `UNVERIFIED` is attested. FAIL, BLOCKED, SKIPPED and PASS rows are refused. This closes security N2 and the SKIPPED path in F58 without persisting the synthetic input: a row with a hidden failing input is always stored FAIL. It also covers future downgrades (L21 invariants, L22) that will not be stored as clauses.
7. **Bounds.** `note` is at most 1000 characters and holds no control characters (`char.IsControl`). `clause_id` must match the spec id pattern `^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$`. The ledger renderer sanitises every free-text value: CR, LF and other control characters become a space, and `|` in a table cell becomes `\|`.
8. **Fidelity.** After an attestation, `run.json` `fidelity` becomes `evidence_only`, as `FidelityRules` already defines for attested runs.

### D-ML68. C9 completeness in wave 3

Rules on review N8 (the reviewer offered a narrowing by decision). C9 is the contract, so the evidence that makes a FAIL row diagnosable lands now (W3-fix-B). The rest moves to the follow-up packet **L16f** (wave 4, depends on W3-fix-B). L16f covers:

- `logs/<process>.jsonl` slices for the run window;
- `build.commit`, `build.dirty`, `build.checkout` and the build id in the folder name, from the last `lab_build`;
- `tools[]`, `lease_owner` and `members[]` (members stay empty until L30);
- row `started_at`, `ended_at` and `member`;
- clause `text`, `op` and `value`.

Until L16f lands, these fields keep the empty, null or zero values that C9 already allows ("Fields from later packets are present from L16 with empty, null or zero values").

### Contract changes (applied to `work-packets.md` by this ruling)

- **C4 `uat_attest`:** `verdict` is `held` or `not_held`. `note` is at most 1000 characters, with no control characters. `clause_id` matches the spec id pattern. Only a `NEEDS_HUMAN` or `UNVERIFIED` row is attested. The call needs harness approval (O2). Output: `{run_id, section, row, clause_id, grade}`, a superset of `{section, row, grade}`.
- **C4 `uat_run`:** the row said `specs`, but the code and the CLI use one `spec` plus `specs_dir?`, and the row now says so. `spec` is optional when `replay` is given, and exactly one of the two is required. C4 already lists `bundle` in the result, but the code never returned it (review N6). It is the bundle folder name, and null for `plan_only`.
- **C9 row JSON:** `attestations:[{clause_id, verdict, note, at, lease_owner, caller}]`.
- **C9 `run.json`:** `secret_scan_removed:[<bundle-relative path>]`. It is empty when the scan passed.

## Consolidated fix list

Each item is tagged with its source (R = wave3-review-1, S = wave3-security-1). Line numbers are at `e996406`.

| # | Source | Packet | File:line | Change (summary; exact text in the packet) | Verify |
| --- | --- | --- | --- | --- | --- |
| 1 | S-N1, R (vocabulary) | A | `Uat/Bundle/BundleAttestor.cs:38-41,98,105-111`; `Tools/Uat/UatReportTools.cs:139-169` | D-ML67 items 4 and 5: `held`/`not_held`; attestation carries `lease_owner` and `caller` | `UatAttestTests`: `pass` refused; record names owner and caller |
| 2 | S-N2 | A | `BundleAttestor.cs:77-80` | D-ML67 item 6: only `NEEDS_HUMAN` or `UNVERIFIED` rows | expect_error-succeeded row stays FAIL, `overall` stays `fail`; SKIPPED refused |
| 3 | S-N3 | A | `BundleAttestor.cs:35-46`; `LedgerRenderer.cs:39-100` | note cap and control-character check, `clause_id` pattern, ledger sanitiser, `Attested:` line | 1001-char note, note with `\n`, `../x` refused; a note with `\| PASS \|` cannot add a table line |
| 4 | D-ML67 item 8 | A | `BundleAttestor.cs:201-223` | `fidelity = "evidence_only"` after an attestation | attest test reads `run.json` |
| 5 | S-N4 | B | `Secrets/Redactor.cs:45-78`; `Uat/Bundle/SecretScanner.cs:47-71`; new `Secrets/SecretForms.cs` | redact and scan the raw form and each JSON-escaped form of every vault value | vault value `ab"cd\ef+ghé!`, written through the real `BundleWriter`, is masked and never found raw or escaped |
| 6 | R-N7 | B | `Uat/UatRunner.cs:258` | `Task.Delay(TimeSpan.FromMilliseconds(wait), _time, ct)` | `FakeTimeProvider` test |
| 7 | R-N8 (D-ML68) | B | `UatRunner.cs:23,28,304-328`; `BundleWriter.cs:150-160,253-260` | row JSON `observed`, `attempts`, `checked_at`, `steps[]`, `attestations: []`; `redactions` counted | new `RowEvidenceTests` |
| 8 | R-N9 | B | `BundleWriter.cs:183-188,248-251` | the secret-failed pass skips `fingerprint.json`, records `secret_scan_removed` and rescans | planted value: `run.json` lists the file, ledger BLOCKED, no file holds the value |
| 9 | R-N1 | C | `Stack/LabStack.cs:83-110,765-829` | wipe also removes labelled orphans; `stack_up` without `wipe` refuses when orphans exist | `LabStackTests` with `FakeDocker` |
| 10 | R-N2 | C | `Cli/LabCli.cs:252,322-325,392-405` | tool error JSON becomes `[code] message`; `[Lab.NotFound]` exits 2 | `LabCliExitCodeTests` |
| 11 | R-N3 | C | `Tools/Uat/UatRunTool.cs:22-31,110,146` | `spec` optional; exactly one of `spec` and `replay`; load with `specName` | `ReproTests`; `{}` returns `[Lab.InvalidArgument]` |
| 12 | R-N6 | C | `UatRunTool.cs:204`; `LabCli.cs:276-280` | result returns C4's `bundle`; CLI prints the real path | `ReproTests` `bundle` fact |
| 13 | R-N4 | C | `Mcp/Areas/UatArea.cs:31-32`; `Processes/LabProcessSupervisor.cs` | `RingFor(name)` under `_gate`; `LogSource` wired to it | `LogSource` test over a real supervisor ring |
| 14 | R-N5 | C | `docs/guides/uat-specs/lab-selftest.toml:4,280` | `since = "run_start"`; comment line rewritten | S-L6 `nodes-log` PASS |
| 15 | R-N10, S-N5 | C | `tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs:24-125` | no pre-run sweep; fail fast on labelled containers unless `MERIDIAN_LAB_S6_WIPE=1`; context from config; skip without Docker; `nodes-log` must PASS | S-L6, run by the orchestrator |

## Rejected or changed

| Item | Ruling | Why |
| --- | --- | --- |
| S-N1 option (b), second credential and console prompt | Rejected | F57: same-user agents read the same environment, and `/mcp` bypasses a CLI prompt. O2 puts the user in the loop instead. |
| S-N2 "persist the synthetic input" | Replaced by D-ML67 item 6 | The grade allow-list closes the hole for every hidden input, not just this one. The step evidence (item 7) still records `ok` and `expected_error`. |
| S-N4 "redact structured values before serialising" | Not chosen | Two mechanisms would drift. One escaped-form mechanism in `Redactor` and `SecretScanner` covers `BundleWriter`, `BundleAttestor` and any later JSON writer. |
| R-N9 "re-render `ledger.md` from the BLOCKED rows" | Already done | `BundleWriter.cs:199-201,263` re-renders the ledger with BLOCKED rows. The real defects are the re-written `fingerprint.json`, the missing rescan and the unnamed files (item 8). The test still pins the ledger. |
| R-N9 field name `scan.removed` | Renamed `secret_scan_removed` | `secret_scan` is already a string in C9. |
| R-N10 "drop `--context`, use the default context" | Rejected | D-ML6: every docker call names its context. The test reads `Lab__Docker__LinuxContext` (the key `DockerCli.cs:23` reads through `LabDaemon.cs:211` `AddEnvironmentVariables`) and falls back to `desktop-linux`. |
| R-N10 `Assert.SkipWhen` | Use `[SkippableFact]` and `Skip.IfNot` | The project is xUnit v2 with `Xunit.SkippableFact`, so v3's `Assert.SkipWhen` does not exist. |
| R-N10 verify "stale container is gone" without consent | Changed | S-N5: the test leaves labelled containers alone unless `MERIDIAN_LAB_S6_WIPE=1`. With the variable set, the daemon's own wipe (item 9) removes them, which proves R-N1 end to end. |
| R-N8 `logs/`, build, `tools`, `members`, `lease_owner` | Deferred to L16f | D-ML68. |
| R and S "security-review label" | Noted | D-ML16 and D-ML39: one campaign PR with `security-review` at the end. |
| S "Not fixed" list | Stands | Same-user boundary. Pass 2 reads the areas both reports list as not reviewed in depth. |

## Packets

All three branch from `campaign/meridian-lab` at `e996406`, run in parallel in separate worktrees, and merge locally into `campaign/meridian-lab` (D-ML16) in any order. Their files do not overlap. The only shared file is `src/Tools/Dhadgar.Lab/README.md`, and each packet edits only its own named section, so the hunks are far apart and merge cleanly. Every packet has a security review (lab code; D-ML67 and redaction). After all three merge, the orchestrator runs S-L6 on the Docker host with `MERIDIAN_LAB_S6_WIPE=1`, then wave 3 review pass 2 and security pass 2.

Rules inline for every packet:

- R-RESULT: `Result<T>` for validation, IO and network errors; no new throws. Never `Result<T>.Success(null)`: the shared `Result<T>` throws on a null success value.
- R-PATH: every file path goes through `IPathValidator.ValidatePath` against its root (`BundleFolders.ResolveFile` inside bundles).
- R-ARGS: processes and docker use `ArgumentList` only; docker goes through `IDockerCli` with an explicit context (D-ML6). Lab containers are only names that start `LabDefaults.ContainerPrefix` (R-BLAST).
- R-SECRET: vault values, the token and the lease id never reach a file, log, audit line or message unredacted. Zero temporary buffers that held a secret (`CryptographicOperations.ZeroMemory`, `ArrayBufferWriter<T>.Clear()`).
- R-USING: `using` on `JsonDocument`, `Utf8JsonWriter`, `Process` and every `IDisposable`.
- R-NULL: nullable is enabled, and CI builds with `CI=true` (warnings as errors, `AnalysisMode AllEnabledByDefault`).
- No package versions in `.csproj` (Central Package Management).
- xUnit v2 in the style of the target file. Daemon tests use `[Collection("LabHome")]`. Time-based tests use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`).
- Touch only the files named in your packet. If another file must change, stop and return `blocked`.

Checks for every packet, in order (`<W>` is the packet worktree; Docker not needed):

```powershell
$env:CI = 'true'
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true"
```

Expect 552 passing, 4 skipped, plus the packet's new facts, with 0 failing.

### W3-fix-A. Attestation (D-ML67; S-N1, S-N2, S-N3)

- **Branch / worktree / commit:** `ml/w3-fix-a-attest` / `ml-w3fa` / `fix(lab): attestation is attributed, bounded and limited to open rows [W3-fix-A]`
- **Files (only these):** `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleAttestor.cs`, `src/Tools/Dhadgar.Lab/Uat/Bundle/LedgerRenderer.cs`, `src/Tools/Dhadgar.Lab/Tools/Uat/UatReportTools.cs`, `tests/Dhadgar.Lab.Tests/Uat/UatAttestTests.cs`, new `tests/Dhadgar.Lab.Tests/Uat/Bundle/LedgerSanitiseTests.cs`, `.claude/skills/lab-uat/SKILL.md` (line 48 only), `src/Tools/Dhadgar.Lab/README.md` (`## Tool tiers` row for `uat_attest` and a new `## Attestation` section before `## Probe and Windows reads` only).
- **Security review:** yes.

Contract excerpt: C4 `uat_attest` as amended above; C8 "Attestation applies only to `human` clauses and re-grades the row"; C9 `attestations:[{clause_id, verdict, note, at, lease_owner, caller}]`; D-ML67.

1. `LedgerRenderer.cs`
   - Add `public sealed record LedgerAttestation(string ClauseId, string Verdict, string Note, DateTimeOffset At, string LeaseOwner, string Caller);`.
   - Add an optional trailing parameter to `LedgerRow`: `IReadOnlyList<LedgerAttestation>? Attestations = null`. `BundleWriter` constructs `LedgerRow` positionally with 7 arguments and must compile unchanged.
   - Add `internal static string Line(string text)`, which replaces `\r\n`, `\r`, `\n` and every other `char.IsControl` character with a space. Add `internal static string Cell(string text)`, which is `Line(text)` with `|` replaced by `\|`.
   - Use `Cell` for every value in the summary table (line 50-51) and `Line` for every value in the per-row block: title, system, build, expected, and each clause's locator and error inside `Saw`.
   - After `- Expected:`, for each attestation append `- Attested: <ClauseId> <Verdict> by <LeaseOwner> via <Caller> at <Time(At)>`. When the note is not empty, add `; note: <Line(Note)>`.
   - Clean text must render exactly as before. The golden `LedgerRendererTests` in `EvidenceBundleTests.cs` must pass unedited; that file belongs to W3-fix-C.
2. `BundleAttestor.cs`
   - New signature: `Attest(string folder, string runId, string section, string row, string clauseId, string verdict, string? note, DateTimeOffset at, AttestedBy by, Redactor redactor, IPathValidator validator)`, with `public sealed record AttestedBy(string LeaseOwner, string Caller);` in this file.
   - Lines 38-41: accept only `held` or `not_held`, else `[Lab.InvalidArgument] verdict must be held or not_held`.
   - Lines 43-46: `clauseId` must also match `IdPattern`, else `[Lab.InvalidArgument] section, row or clause id is not valid`. `note` must be null or at most 1000 characters with no `char.IsControl` character, else `[Lab.InvalidArgument] note must be at most 1000 characters without control characters`. All of these checks run before any file read.
   - Lines 77-80: replace the BLOCKED check with an allow-list. If the stored grade is neither `NEEDS_HUMAN` nor `UNVERIFIED`, return `[Lab.InvalidArgument] only a NEEDS_HUMAN or UNVERIFIED row is attested`.
   - Line 98: `held` → `Held`, `not_held` → `NotHeld`.
   - Lines 105-111: the attestation object gains `["lease_owner"] = by.LeaseOwner` and `["caller"] = by.Caller`.
   - `ReadRows` (line 162): read `attestations` into `LedgerAttestation`s. A missing `lease_owner` or `caller` reads as an empty string, so pre-fix bundles still render.
   - `RecomputeRun`: also set `runObj["fidelity"] = "evidence_only"`.
   - Update the doc comments (line 30 and the class summary).
3. `UatReportTools.cs`
   - `UatAttestTool` takes `LeaseBook leases` in addition to its current parameters. It is resolved from DI by `AddLabTool<UatAttestTool>`, so `UatArea.cs` does not change.
   - In `ExecuteAsync`, call `_leases.Touch(context.LeaseId!)`. The dispatcher has already checked the lease, so a failure here returns its error unchanged. Pass `new AttestedBy(touched.Value!.Owner, context.Caller)`.
   - Line 151 description: `Records the user's held or not_held verdict on one pending human clause of a NEEDS_HUMAN or UNVERIFIED row, then regrades the row and rewrites the ledger and overall. The user approves each call.`
   - Line 22: document the `Verdict` values in the record's doc comment.
4. `UatAttestTests.cs`, new facts:
   - `Attest_PassVocabulary_IsRefused`: `verdict:"pass"` returns `[Lab.InvalidArgument]`.
   - `Attest_RecordsLeaseOwnerAndCaller`: the row JSON's attestation has `lease_owner` and `caller`, and `ledger.md` contains `- Attested: <id> held by <owner> via mcp`.
   - `Attest_FailRowWithHiddenExpectFailure_StaysFail`: build the bundle through the real `UatRunner` and `BundleWriter`. Use a row whose `expect_error` step succeeds, plus a human clause. The row is FAIL, attest is refused, and `run.json` `overall` stays `fail`. Revert item 2's allow-list and the row becomes PASS.
   - `Attest_SkippedRow_IsRefused`.
   - `Attest_LongNote_ControlCharNote_BadClauseId_AreRefused`: a `[Theory]` over a 1001-character note, `"a\nb"` and the clause id `../x`.
   - `Attest_SetsFidelityEvidenceOnly`.
   - Update existing calls to the new `Attest` signature and to `held`.
5. `LedgerSanitiseTests.cs`: a title and an error containing `\n| x | PASS |` render on one line, and a table cell with `|` renders `\|`. Revert `Cell`/`Line` and the table gains a forged row.
6. Docs:
   - `SKILL.md:48`: say the verdict is `held` or `not_held`, and that the user approves each `uat_attest` call (O2).
   - README `## Attestation`: who may attest (D-ML67), the O2 setting, the recorded `lease_owner`/`caller`, the allowed grades and the note limits, and the stated boundary: a session that skips permission checks also skips O2.
   - README `uat_attest` table row: append `; user approval per call (O2)`.

### W3-fix-B. Evidence, redaction and the secret scan (S-N4, R-N7, R-N8, R-N9)

- **Branch / worktree / commit:** `ml/w3-fix-b-evidence` / `ml-w3fb` / `fix(lab): row evidence, escaped-secret redaction and a strict secret scan [W3-fix-B]`
- **Files (only these):** `src/Tools/Dhadgar.Lab/Uat/UatRunner.cs`, `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleWriter.cs`, `src/Tools/Dhadgar.Lab/Uat/Bundle/SecretScanner.cs`, `src/Tools/Dhadgar.Lab/Secrets/Redactor.cs`, new `src/Tools/Dhadgar.Lab/Secrets/SecretForms.cs`, `tests/Dhadgar.Lab.Tests/Secrets/RedactorTests.cs`, `tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs`, new `tests/Dhadgar.Lab.Tests/Uat/Bundle/RowEvidenceTests.cs`, new `tests/Dhadgar.Lab.Tests/Uat/Bundle/BundleSecretTests.cs`, `src/Tools/Dhadgar.Lab/README.md` (`## Secrets and audit`, the `Redactor` bullet only).
- **Security review:** yes (Secrets).
- Keep the `BundleWriter` constructor, `LedgerRow` and `LedgerClause` unchanged: `UatRunTool` (W3-fix-C) and `LedgerRenderer` (W3-fix-A) own them.

Contract excerpt: C9 row JSON `steps:[{id, tool, ok, error, error_code, expected_error, duration_ms, result}]` and `clauses:[{…, state, observed, attempts, checked_at}]`, with `result` and `observed` redacted and truncated to 16 KiB; `run.json` `redactions`, `secret_scan`, `secret_scan_removed`; the secret-scan paragraph; D-ML68.

1. `Secrets/SecretForms.cs` (new, `internal static class SecretForms`):

   ```csharp
   /// The encoders whose output can reach a bundle or log file. Add any encoder that `grep "Encoder ="` finds under src/Tools/Dhadgar.Lab.
   internal static readonly JavaScriptEncoder[] Encoders = [JavaScriptEncoder.Default, JavaScriptEncoder.UnsafeRelaxedJsonEscaping];

   /// Calls use(form) for the raw secret and then once for each JSON-escaped form (no quotes) that differs from it.
   /// Every temporary buffer is cleared before return. Returns false as soon as use returns false.
   internal static bool ForEachForm(ReadOnlySpan<char> secret, SecretFormVisitor use);

   internal delegate bool SecretFormVisitor(ReadOnlySpan<char> form);
   ```

   To build each escaped form:
   - Create an `ArrayBufferWriter<byte>` and write the secret with `using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = encoder });` then `writer.WriteStringValue(secret)` and `writer.Flush()`.
   - Decode `buffer.WrittenSpan[1..^1]` with `Encoding.UTF8` into a char array rented from `ArrayPool<char>.Shared`.
   - Call `use` when the form differs from the raw secret.
   - Then `CryptographicOperations.ZeroMemory` the used chars (`MemoryMarshal.AsBytes`), return the array, and `buffer.Clear()`.
2. `Redactor.cs`
   - `Redact(string)` line 62: inside `_vault.Use`, replace every form from `SecretForms.ForEachForm` with `***<ref>`. Keep longest-first across secrets.
   - Add `public string Redact(string text, out int count)`. `count` is the number of replacements; the vault forms and the three patterns each count once per match. `Redact(string)` calls it and discards the count.
3. `SecretScanner.cs` line 63: a hit is any form from `SecretForms.ForEachForm`.
4. `UatRunner.cs`
   - Line 23 becomes `UatClauseResult(string Id, ClauseState State, string? Error, [property: JsonIgnore] JsonElement? Observed = null, [property: JsonIgnore] int Attempts = 0, [property: JsonIgnore] DateTimeOffset? CheckedAt = null)`.
   - Line 28 adds `[property: JsonIgnore] IReadOnlyList<UatStepResult>? Steps = null` to `UatRowResult`.
   - Add `public sealed record UatStepResult(string Id, string? Tool, bool Ok, string? Error, string? ErrorCode, string? ExpectedError, long DurationMs, JsonElement? Result);`.
   - `JsonIgnore` keeps the `uat_run` tool result unchanged; the evidence goes only to the bundle.
   - Line 312: pass `evaluation.Observed`, `evaluation.Attempts` and `evaluation.CheckedAt`.
   - Build the step list in step order from `row.Steps` and the `steps` dictionary. Use `Tool = step.Tool` and `ExpectedError = step.ExpectError`. Record the outcome as received, before line 270 rewrites `Ok`/`Error`: `Ok` is the tool's own success and `ErrorCode` its code. Pass the list into the `UatRowResult` at line 328.
   - Line 258: `await Task.Delay(TimeSpan.FromMilliseconds(wait), _time, ct).ConfigureAwait(false);`.
5. `BundleWriter.cs`
   - Row JSON (line 256-258): each clause gains `observed`, `attempts` and `checked_at`, taken from the matching `UatClauseResult` in `_results`, not from `LedgerClause`. The row gains `steps` (from `UatRowResult.Steps`) and `attestations: []`.
   - Redact `result` and `observed` with `_redactor.Redact(JsonElement)`. If the redacted raw text is longer than 16384 characters, write `{"truncated": true, "text": "<first 16384 chars>"}` instead.
   - Secret-failed rows write `clauses: []` and `steps: []`.
   - `Write` (line 281) uses `Redact(text, out int n)` and adds `n` to a per-pass counter.
   - Write `run.json` last in `WriteAll`, so `redactions` is the sum for the other files of that pass.
   - Secret-scan branch (lines 183-188): if the first scan fails, keep `scan.RemovedFiles`, which are bundle-relative, with `\` replaced by `/`.
   - `WriteAll(result, secretFailed: true, removed)` skips `fingerprint.json`; the scan may have just removed it. `run.json` gains `secret_scan_removed = removed`, which is `[]` on the passing pass.
   - Then rescan. A second hit sets `WriteError = "[Lab.Unavailable] the bundle still held a secret after the rewrite"`.
6. Tests:
   - `RowEvidenceTests`:
     - A one-row spec with a fake source returning `{"rows":[{"a":1}]}`: the row file has `"observed"` containing `"a":1`, `"attempts":1`, `checked_at` and one `steps` entry with `tool`, `ok` and `duration_ms`.
     - An `expect_error` step that succeeded: the row file shows `expected_error` and `ok:true`.
     - `run.json` `redactions` is above 0 when a clause error held a vault value.
     - The `uat_run` result JSON has no `observed` or `steps`.
   - `BundleSecretTests`:
     - A vault value `ab"cd\ef+gh` + `é` + `!`, captured into a clause error and written through the real `BundleWriter`: the scan passes, a file shows `***<ref>`, and no file contains the raw value or its `JavaScriptEncoder.Default` escaped form. Revert item 2 and the escaped form survives.
     - A planted value that cannot be redacted (bypass the redactor with a raw `File.WriteAllText` into `rows/` before `RunFinished`, or use a fake redactor): `run.json` lists the file in `secret_scan_removed`, `ledger.md` shows BLOCKED, `fingerprint.json` is not re-created, and no file contains the value.
   - `RedactorTests`: the escaped form alone is masked; `HasSecret` finds the escaped form alone.
   - `UatRunnerTests`: a `wait_ms = 5000` step with `FakeTimeProvider`. The row is not done before `Advance(5 s)` and is done after it. Revert item 4's `Task.Delay` and the test hangs, so give the test an xUnit `Timeout`.
7. Docs: in the README `Redactor` bullet, add that each vault value is also matched in its JSON-escaped forms, and that `SecretScanner` checks the same forms.

### W3-fix-C. Stack orphans, CLI errors, replay, log ring and S-L6 (R-N1-N6, R-N10, S-N5)

- **Branch / worktree / commit:** `ml/w3-fix-c-stack-cli` / `ml-w3fc` / `fix(lab): wipe lab orphans, CLI tool errors, replay args, log ring and a safe S-L6 [W3-fix-C]`
- **Files (only these):** `src/Tools/Dhadgar.Lab/Stack/LabStack.cs`, `tests/Dhadgar.Lab.Tests/Stack/LabStackTests.cs`, `src/Tools/Dhadgar.Lab/Cli/LabCli.cs`, `tests/Dhadgar.Lab.Tests/Daemon/LabCliExitCodeTests.cs`, `src/Tools/Dhadgar.Lab/Tools/Uat/UatRunTool.cs`, `tests/Dhadgar.Lab.Tests/Uat/Bundle/EvidenceBundleTests.cs` (the `ReproTests` class only), `src/Tools/Dhadgar.Lab/Mcp/Areas/UatArea.cs`, `src/Tools/Dhadgar.Lab/Processes/LabProcessSupervisor.cs`, new `tests/Dhadgar.Lab.Tests/Uat/LogSourceRingTests.cs`, `docs/guides/uat-specs/lab-selftest.toml`, `tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs`, `src/Tools/Dhadgar.Lab/README.md` (`## CLI` section only).
- **Security review:** yes (docker blast radius, CLI).

Contract excerpt: C4 `uat_run` as amended above (optional `spec`, exactly one of `spec` and `replay`, result `bundle`); C8 CLI exit codes (0 pass, 1 fail, 2 config or plan error, 4 incomplete, 5 pass_with_known; 3 is the daemon's "already running"); D-ML6 (explicit context, `meridian-lab-*` names, label `meridian-lab=1`); D-ML21 blast radius.

1. `LabStack.cs`
   - Add `private async Task<Result<IReadOnlyList<string>>> ListLabelledContainersAsync(CancellationToken ct)`. It runs `Docker(["ps", "-a", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--format", "{{.Names}}"], ct)` and returns the trimmed, non-empty names that start with `LabDefaults.ContainerPrefix`. A non-zero exit is `[Lab.Docker] could not list lab containers`.
   - `TearDownAsync(wipe: true)` (line 765): after the registry loop, list again, and for each name not already removed run `rm -f -v <name>` with the same inspect-after-failure check (lines 784-797). On failure return `[Lab.Docker] could not remove <name>`. `wipe: false` is unchanged.
   - `UpAsync` (lines 90-94): when `!wipe && !hasContainers`, list. If any names come back, return `[Lab.Docker] labelled lab containers exist that this daemon does not know; run stack_up with wipe`.
   - `LabStackTests`, with `FakeDocker` returning two `ps` names (one `meridian-lab-postgres`, one `postgres`):
     - `UpAsync(wipe: true)` issues `rm -f -v meridian-lab-postgres` and never touches `postgres`.
     - `UpAsync(wipe: false)` on an empty registry is refused with the message above.
     - Revert the listing and both tests fail.
2. `LabCli.cs`
   - Add `internal static string ToolErrorText(string toolName, string text)`. It parses `{"error":{"code","message"}}` with `using var doc = JsonDocument.Parse(text)` and returns `"[" + code + "] " + message`. On a `JsonException` or a missing property it returns the text, or `[Lab.Tool] <tool> failed` when the text is blank. Use it at line 324.
   - Line 252 becomes `return ExitCodeFor(call.Error);`.
   - Line 394: add `[Lab.NotFound]` to the exit-2 list.
   - Lines 276-280: print `bundle <Path.Combine(UatRuns, bundle)>` only when the result's `bundle` is a non-empty string; otherwise print nothing.
   - `LabCliExitCodeTests`:
     - `ToolErrorText` on `{"error":{"code":"Lab.NotFound","message":"no such spec"}}` gives `[Lab.NotFound] no such spec`.
     - `ExitCodeFor("[Lab.NotFound] x")` is 2.
     - Garbage text is returned unchanged.
3. `UatRunTool.cs`
   - Line 23 becomes `string? Spec = null`. In `ExecuteAsync`, before line 98:
     - if both `Spec` and `Replay` are null or empty, return `[Lab.InvalidArgument] spec or replay is required`;
     - if both are given, return `[Lab.InvalidArgument] give spec or replay, not both`.
   - Line 110: `string specName = repro?.SpecFile ?? args.Spec!;`.
   - Line 146: `SpecLoader.LoadText(specName, text)`.
   - Check that the advertised schema's `required` list no longer holds `spec`: `LabJson.SchemaFor` follows nullability and defaults; read it and pin it in the test.
   - Line 204: serialise `run.Value!` with the options the dispatcher uses for results (`LabJson`) into a `JsonObject`. Add `["bundle"] = sink.Folder is { } folder ? Path.GetFileName(folder) : null`. Return `Result<object>.Success(payload)`. The payload is never null.
   - `ReproTests`:
     - `Replay_SpecChanged_IsRefused` sends no `spec`.
     - A call with `{}` returns `[Lab.InvalidArgument] spec or replay is required`.
     - `spec` plus `replay` is refused.
     - A real run's result has a `bundle` equal to the folder name under `UatRuns`.
4. `LabProcessSupervisor.cs`: add `public LogRing? RingFor(string name)`. Under `_gate`, it returns `_processes.TryGetValue(name, out var p) ? p.Ring : null`.
5. `UatArea.cs` lines 31-32: `services.AddSingleton<IClauseSource>(sp => new LogSource(sp.GetRequiredService<LabProcessSupervisor>().RingFor));`. Delete the comment.
6. `LogSourceRingTests`: register the area, put lines into a supervisor ring through the supervisor's own test seam (read how `LogsTailTool` tests feed a ring), and evaluate a log clause. Its state is `Held`, not `Unverified`. Revert item 5 and it is `Unverified`.
7. `lab-selftest.toml`
   - Line 280: `since = "run_start"`.
   - Line 4: `# Enroll-route calls per run: 4 token enrolls (1 direct, 3 through the Gateway or ingress), 1 reuse, 2 empty-body posts: under 25 per 60 s.`
8. `LabSelfTestE2ETests.cs`
   - `[SkippableFact(Timeout = 900_000)]`, keeping the three traits.
   - `string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } c ? c : ContainerLogFollower.DefaultLinuxContext;`. `RunDocker` passes `--context`, `context` and returns the exit code with the output.
   - First, `Skip.IfNot(docker version exit code == 0, "docker is not reachable on context " + context)`.
   - Then list `ps -aq --filter label=meridian-lab=1`. If any exist and `MERIDIAN_LAB_S6_WIPE` is not `1`, fail with `Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the self-test's stack_up wipe remove them")`, before any daemon starts. The containers are untouched.
   - Delete the before-run `RemoveLabContainers()` (line 33). Keep the `finally` cleanup at line 88, which is reached only after the guard passed.
   - `AssertRunPasses`: every row, `nodes-log` included, must be `pass`. Find the bundle from the result's `bundle` field.
9. Docs, README `## CLI` only:
   - tool errors print as `[code] message`;
   - `[Lab.NotFound]` exits 2;
   - the printed bundle path is the real folder;
   - `uat run --replay` needs no `--specs`;
   - `stack_up wipe` also removes labelled `meridian-lab-*` containers that the daemon does not know, and `stack_up` without `wipe` refuses while they exist;
   - log clauses read the supervisor's ring;
   - S-L6 needs `MERIDIAN_LAB_S6_WIPE=1` when lab containers exist.

### L16f. C9 completeness (follow-up, wave 4)

Depends on W3-fix-B. Scope: the D-ML68 list. The architect writes its packet text when wave 4 is planned; it is not a wave 3 fix.

## After the fixes

1. Merge W3-fix-A, W3-fix-B and W3-fix-C into `campaign/meridian-lab`, then run the non-Docker suite once on the merged tip.
2. The orchestrator runs S-L6 (`LabSelfTestE2ETests`) on the Docker host with `MERIDIAN_LAB_S6_WIPE=1`. Expect every row PASS, `nodes-log` included. A skip counts as not run.
3. Wave 3 review pass 2 and security pass 2 over the fix range. Pass 2 also reads the areas both pass-1 reports list as not reviewed in depth.
4. Owner action O2 can land at any time. It does not block a packet.

## Ledger changes (applied in this commit)

- README:
  - D-ML39 is Accepted (one PR at the end; D-ML16 stands);
  - D-ML16 loses "Holds until D-ML39 rules";
  - the ship line now names one PR after L47;
  - adds F57-F60, D-ML67, D-ML68 and packet rows W3-fix-A/B/C and L16f.
- `work-packets.md`: the C4 `uat_attest` and `uat_run` rows, the C9 row JSON `attestations` and `run.json` `secret_scan_removed`.
- `RESUME.md`: the state, the next actions, O2, and D-ML39 is out of the open list.
