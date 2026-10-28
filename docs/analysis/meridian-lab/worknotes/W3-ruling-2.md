# W3-ruling-2: wave 3 review pass 3 (final)

Input: `.claude/reports/meridian-lab/wave3-review-3.md` (verdict `fix`, N1-N2, documentation only; fix range `3dbab5c..7cf1c5c`). Campaign tip `7cf1c5c`. Lab tests at the tip: 595 pass, 0 fail, 4 skip (filter `RequiresDocker!=true&FullyQualifiedName!~DockerTests`). Consistent with W3-ruling-1 and D-ML67.

Outcome: both notes accepted and widened to the other places that carry the same stale text. One fix packet, **W3-fix-3**, that one Sonnet implementer applies. No new decision, no BlockedDecision. Pass 3 found no code defect, so wave 3 closes after W3-fix-3 and one ruling check.

## Facts

| ID | Fact | Evidence |
| --- | --- | --- |
| F61 | O2 is not applied: `.claude/settings.json` has no `permissions` block and no `mcp__meridian-lab__uat_attest`. Five texts still state per-call user approval as present fact: the skill, the lab README table row, the `uat_attest` tool description (which the model reads in `tools/list`), the C4 contract row and the D-ML67 ledger row. Only lab `README.md:203` states it correctly. | `SKILL.md:48`; `README.md:178`; `UatReportTools.cs:155`; `work-packets.md:363`; ledger `README.md:164` |
| F62 | The note check is `LedgerRenderer.IsUnsafeText` (Control, Format, LineSeparator, ParagraphSeparator, PrivateUse, OtherNotAssigned) and the id pattern ends in `\z`. Three texts still say "no control characters" or show `$`. | `BundleAttestor.cs:28,39,57`; `LedgerRenderer.cs:160-162`; lab `README.md:207`; `work-packets.md:363` |

## Rulings

| Note | Ruling | Why |
| --- | --- | --- |
| N1 (SKILL.md:48, README.md:178) | Accepted, widened | The reviewer's replacement texts are taken as written. Widened to `UatReportTools.cs:155`: the tool description is the text a session actually reads before calling the tool, so leaving "The user approves each call." there keeps the contradiction where it matters most. The C4 row and the D-ML67 ledger row are fixed in this commit (ledger files, outside the reviewed range). |
| N2 (README.md:207, work-packets.md:363) | Accepted, widened | The reviewer's README text is taken as written. Widened to the `note` doc comment at `BundleAttestor.cs:39`, which still says "no control characters". The work-packets row is fixed in this commit. |

No change to D-ML67 itself. The amended ledger row states what pass 2 security established: O2 is pending, O2 covers only the MCP tool path, and attestation is attribution under the same-user boundary.

## Ledger changes (applied in this commit)

- Ledger `README.md`: D-ML67 row (O2 pending, attribution not proof, note limits as coded, `\z`, link to this ruling); packet rows W3-fix-A/B/C set to Merged locally; new row W3-fix-3.
- `work-packets.md:363`: the C4 `uat_attest` row (note limits as coded; approval once O2 is applied; attribution, not proof).
- `RESUME.md`: state at `7cf1c5c`, next actions, and the O2 follow-up (when O2 lands, drop "pending" at lab `README.md:178,203` and the "until then" sentence at `SKILL.md:48`).

## W3-fix-3. Stale attest texts (pass-3 N1, N2)

- **Branch / worktree / commit:** `ml/w3-fix3-docs` from `campaign/meridian-lab` at this ruling's commit / `ml-w3f3` / `docs(lab): state O2 as pending and the attest note limits as coded [W3-fix-3]`
- **Files (only these):** `.claude/skills/lab-uat/SKILL.md` (line 48 only), `src/Tools/Dhadgar.Lab/README.md` (lines 178 and 207 only), `src/Tools/Dhadgar.Lab/Tools/Uat/UatReportTools.cs` (line 155 only), `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleAttestor.cs` (line 39 only). Do not edit `work-packets.md` or the ledger `README.md`; this ruling did.
- **Security review:** yes, as part of the one campaign PR (D-ML16, D-ML39). No separate pass: no behaviour changes.

Rules inline:

- Touch only the four lines named. If another line must change, stop and return `blocked`.
- Keep each replacement on one line. Code spans use single backticks; do not add or remove table pipes on line 178.
- R-NULL: nullable is enabled and CI builds with `CI=true` (warnings as errors). A string literal and an XML doc comment change only; keep the XML well formed (`<c>` for code, no raw `<` or `&`).

### Ordered application list

1. `.claude/skills/lab-uat/SKILL.md:48`. Replace the sentence
   `The user approves each `uat_attest` call in the harness (O2), so the call is shown with its exact arguments before it runs.`
   with
   `Once the owner applies O2 (`mcp__meridian-lab__uat_attest` in `permissions.ask`), the user approves each `uat_attest` call in the harness and sees its exact arguments; until then nothing prompts, so ask the user in chat before every attest. This is attribution, not proof of a human (see the lab README, Attestation).`
   The rest of line 48 is unchanged.
2. `src/Tools/Dhadgar.Lab/README.md:178`. Replace
   `yes, `lease_id` required; user approval per call (O2)`
   with
   `yes, `lease_id` required; user approval per call once O2 is applied (pending)`
3. `src/Tools/Dhadgar.Lab/README.md:207`. Replace
   ``The note is at most 1000 characters with no control characters. The clause id matches `^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$`.``
   with
   ``The note is at most 1000 characters with no control, format, line or paragraph separator, private-use or unassigned code points. The clause id matches `^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z` (no trailing newline).``
   The last sentence of the line (`The ledger renders free text on one line and escapes pipes in table cells.`) is unchanged.
4. `src/Tools/Dhadgar.Lab/Tools/Uat/UatReportTools.cs:155`. Replace the final sentence of the `Description` literal, `The user approves each call.`, with `Ask the user before each call; the harness prompts only once owner action O2 is applied.` The literal stays one line and ends in `.";`.
5. `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleAttestor.cs:39`. Replace
   `/// <param name="note">An optional note of at most 1000 characters with no control characters, redacted before it is written.</param>`
   with
   `/// <param name="note">An optional note of at most 1000 characters with no control, format, line or paragraph separator, private-use or unassigned code points (<see cref="LedgerRenderer.IsUnsafeText"/>), redacted before it is written.</param>`
   Indentation unchanged (4 spaces).

### Checks, in order (`<W>` is the packet worktree; Docker not needed)

```powershell
$env:CI = 'true'
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&FullyQualifiedName!~DockerTests"
```

Expect 0 build errors and 595 passed, 4 skipped, 0 failed. No new test: the change is text only, and no test pins these strings (`Grep "approves each call|pending human clause"` under `tests/` finds nothing).

### Verify (the implementer runs these; the ruling check repeats them)

| # | Grep | Path | Expect |
| --- | --- | --- | --- |
| V1 | `approves each\|approval per call\|approved by the user` | `.claude/skills/lab-uat/SKILL.md`, `src/Tools/Dhadgar.Lab/README.md`, `src/Tools/Dhadgar.Lab/Tools/Uat/UatReportTools.cs`, `docs/analysis/meridian-lab/work-packets.md` | every hit is conditional on O2 ("Once the owner applies O2", "once O2 is applied") |
| V2 | `no control characters` | `src/Tools/Dhadgar.Lab/README.md`, `src/Tools/Dhadgar.Lab/Uat/Bundle/BundleAttestor.cs`, `docs/analysis/meridian-lab/work-packets.md` | only `README.md:22` (lease `owner`/`purpose`, a different check); no hit about the attest note |
| V3 | `\{0,63\}\$` | `src/Tools/Dhadgar.Lab/README.md` | no match |
| V4 | `uat_attest\|permissions` | `.claude/settings.json` | still no match (agents never edit settings; O2 is the owner's) |

Historical worknotes (`W3-ruling-1.md`) and review reports keep their wording; they are records, not current text.

## After the fix

1. Merge `ml/w3-fix3-docs` into `campaign/meridian-lab` locally.
2. One fresh `packet-reviewer` runs the ruling check: the diff touches only the five lines above, V1-V4 hold, build and the non-Docker suite pass. `ship` closes wave 3; anything else is BlockedDecision (campaign-packet skill).
3. Wave 4 starts with L16f (D-ML68); ask the architect for its packet text.

## Not ruled here (stands from pass 3 "Not fixed")

- Non-JSON `http_request` bodies are only vault-redacted, so a runtime-minted token in plain or form text would reach `rows/*.json`. None of the stack's endpoints return such bodies today. Carried to L16f as a check, not an owner question: L16f's spec adds a fact that a `text/plain` body containing `access_token=<value>` is redacted, or records the residual in the lab README.
- The two always-true assertions (`scan is "passed" or "failed"`; lease id and CLI token absent where never given) and the untested `ReadRows` scrub: low value, carried to L16f as test hygiene.
- `PathValidator` lexical only, S-L6 leaves `meridian-lab-net`, Windows-only reads skipped off Windows, `-warnaserror` CA errors in `Dhadgar.Messaging`/`Dhadgar.Contracts`: unchanged carry-overs.
