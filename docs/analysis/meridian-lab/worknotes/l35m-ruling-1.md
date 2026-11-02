# L35m ruling 1

Architect, 2026-11-02. Rules on review pass 1 of L35m: worktree `ml-l35m`, branch `ml/l35m-dbquery-mask`, range `1cf07e96..04d28e15` (head `04d28e15`). Inputs: [packet-reviewer pass 1](../../../../.claude/reports/meridian-lab/l35m-review-1.md) (verdict `decision`) and [security-advisor pass 1](../../../../.claude/reports/meridian-lab/l35m-security-1.md) (verdict `fix`). Both are in the `ml-l35m` worktree under `.claude/reports/meridian-lab/`. Spec: [next-batch plan](next-batch-plan.md) section 7.3 (D-ML154).

**Outcome:** fix pass, with one Sonnet packet-coder (section 4). No owner decision is needed. The Docker test `LabDbDockerTests` **has not been run** and still needs the user's yes before merge (section 5).

New IDs: F133, F134, D-ML160, D-ML161, KG-W7-6. KG-W5-24 is not used. Next free after this ruling: **F135, D-ML162, KG-W5-24, KG-W7-7**.

## 1. Facts checked for this ruling

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | The expression rule is a deny-list of six types (`text`, `character varying`, `character`, `bytea`, `json`, `jsonb`). `token::name`, `token::xml`, `xmlelement(...)`, `to_tsvector(token)`, `token::"char"`, `ARRAY[token]` (`text[]`) and `ROW(token)` (`record`) all leave it unmasked. | `src/Tools/Dhadgar.Lab/Data/ColumnMask.cs:27-30,48` |
| 2 | `BuildResult` treats a column with a non-zero table OID but no catalog match, and a whole-row column (OID set, attnum 0), as an expression. So the expression rule is also the fallback for views, UNION legs and whole-row reads. | `src/Tools/Dhadgar.Lab/Data/LabDb.cs:310` |
| 3 | `BuildResult`, `ColumnInfo` and `RawRows` are `private`; no non-Docker test covers the mask wiring, the null rule or the fallback. | `LabDb.cs:214,217,294` |
| 4 | The Identity names in `Sensitive` match the EF snapshot: `refresh_tokens."TokenHash"` (`character varying(128)`), `OpenIddictTokens."Payload"`, `"ReferenceId"`, and `OpenIddictApplications."ClientSecret"`. | `src/Dhadgar.Identity/Data/Migrations/IdentityDbContextModelSnapshot.cs:493,513,959,1007,1116,1125,1150` |
| 5 | BetterAuth uses the default core schema: a `pg` `Pool` with no `fields`, `modelName` or `usePlural` overrides. Its only plugin is `genericOAuth`, which writes to `account`. So the camelCase names (`accessToken`, `refreshToken`, `idToken`) are expected but **not confirmed live**. | `src/Dhadgar.BetterAuth/src/auth.js:2,111-114,207,222`; `server.js:38-54` (migrations at boot) |
| 6 | The `LabDbDockerTests` stack starts only Postgres, RabbitMQ, Redis and Toxiproxy, so its `betterauth` and `identity` databases hold only the test's own fixture tables. A live-name check therefore cannot live there. `AuthStackDockerTests` does start Identity (Development, auto-migrate), BetterAuth (migrates at boot) and Nodes, and signs a user up through the Gateway. | `tests/Dhadgar.Lab.Tests/Services/AuthStackDockerTests.cs:95-139`; `Services/IdentityLabAdapter.cs:91` |
| 7 | `lab_reader` can read service tables through `ALTER DEFAULT PRIVILEGES FOR ROLE <owner> ... GRANT SELECT ON TABLES`. `pg_attribute`, `pg_class` and `pg_namespace` are readable by any role. | `src/Tools/Dhadgar.Lab/Stack/LabStack.cs:844` |
| 8 | Nodes holds two credential-like columns: `enrollment_tokens."TokenHash"` (`character varying(64)`) and `capacity_reservations."ReservationToken"` (`uuid`, a bearer capability). No lab code and no UAT spec selects either column. Lab and spec SQL against `enrollment_tokens` reads only `Id`, `UsedAt`, `UsedByNodeId` and `IsRevoked`. | `src/Dhadgar.Nodes/Data/Migrations/NodesDbContextModelSnapshot.cs:126-153,183-207`; `Hunt/ModelHunter.cs:50`, `Tools/History/HistoryRunTool.cs:43`, `Coverage/TransitionSnapshotter.cs:31`, `docs/guides/uat-specs/{lab-selftest,catalog/nodes,invariants/nodes}.toml` |
| 9 | No lab code and no UAT spec queries `identity` or `betterauth` through `db_query` today, so changing the rules for those two databases breaks no caller. | grep of `src/Tools/Dhadgar.Lab` and `docs/guides/uat-specs` |
| 10 | The lab README `db_query` paragraph ends "Masking is KG-W7-1 (L35)". That is stale: D-ML154 moved it to L35m. The work-packets L35 file 5 still says "`LabDb` masks the values ..." even though D-ML154 dropped that from L35. | `src/Tools/Dhadgar.Lab/README.md:79`; `docs/analysis/meridian-lab/work-packets.md:3688` |

## 2. Decisions

| ID | Decision |
| --- | --- |
| D-ML160 | Amends D-ML154's masking rule. **(a)** The expression rule becomes an allowlist. In `betterauth` and `identity`, a column with no resolved base column is masked unless its type is in `PassThroughTypes`: `smallint`, `integer`, `bigint`, `real`, `double precision`, `numeric`, `boolean`, `date`, `time without time zone`, `time with time zone`, `timestamp without time zone`, `timestamp with time zone`, `interval`, `uuid`. **(b)** A fail-closed name rule is added in all five databases. A resolved base column whose real column name (`attname` from the catalog, never the alias) contains `token`, `secret`, `password`, `hash` or `payload` (ordinal, ignore case) is masked unless its type is in `PassThroughTypes`. **(c)** The explicit set gains `("nodes", "enrollment_tokens", "TokenHash")` and `("nodes", "capacity_reservations", "ReservationToken")`. An explicit entry masks whatever its type. **(d)** Threat model: masking prevents *accidental* disclosure of lab credentials into tool results, logs, bundles and model context. It does not stop a hostile query author who encodes a secret bit by bit into integers or booleans (`ascii(substr(token,1,1))`, `token LIKE 'a%'`, `count(*) ... WHERE token = @guess`). That residual is accepted, because the secrets are per-stack lab secrets, and the README states it. No table allowlist. |
| D-ML161 | KG-W7-1 closure is split. L35m merges once the non-Docker checks and `LabDbDockerTests` (user's Docker yes) are green, and KG-W7-1 closes at that merge. Confirming the `Sensitive` names against the **live migrated** schemas becomes KG-W7-6. KG-W7-6 closes when the new step in `AuthStackDockerTests` is green. That test needs Docker plus network, so it may ride the wave 7 Docker run (D-ML74). Merging first is safe because rule (b) fails closed for every listed name except `verification.value`, and that one is covered by the live step. |

Why the architect decides this and the owner does not: the change only narrows what a lab-only tool returns from lab-only databases, no caller depends on the narrowed output (fact 9), and it is a direct amendment of an architect decision (D-ML154). The owner can still reverse D-ML160 (d) and ask for a table allowlist. The README sentence makes that visible.

## 3. Rulings on each item

### Packet-reviewer pass 1

| Item | Ruling | Reason |
| --- | --- | --- |
| N1 (`BuildResult` wiring untested) | **Accept, amended.** Make `ColumnInfo`, `RawRows` and `BuildResult` `internal`, then add `LabDbBuildResultTests` as the reviewer specifies, plus two fallback columns: OID set with no catalog match, and whole-row (attnum 0, composite type). | The fallback (fact 2) is what makes UNION legs, views and whole-row reads fail closed. Without a test, it can be deleted silently. |
| D1 (expression deny-list) | **Decided, not BlockedDecision:** option (a), the inverted allowlist (D-ML160 a). | It is the same defect as security N1. Fact 9 shows no caller is affected. |
| D2 (view reports the view's OID) | **Accept as residual; README line.** Rule (b) also masks a view column whose real name carries a fragment. | `lab_reader` cannot create views, and none exist today. |
| D3 (UAT/hunter see `[masked]` for computed text in identity/betterauth) | **Accept, no action.** Widened by D-ML160 to "any non-pass-through expression". | Fact 9. |
| D4 (Docker fixture vs service migrations) | **Accept the reviewer's reading.** The live-name check moves to `AuthStackDockerTests` (D-ML161, KG-W7-6). | Fact 6. |
| D5 (security-review label) | **Accept.** | `db_query` masks credential output. |

### Security-advisor pass 1

| Item | Ruling | Reason |
| --- | --- | --- |
| N1 (cast to a non-listed type leaks) | **Accept, amended.** Allowlist as proposed (`uuid` kept, `time with time zone` added). The verify tests are extended with `record` and a composite type name, the name rule (D-ML160 b) is added, and Docker queries are added for `::name`, `xmlelement`, `ARRAY[...]`, a UNION leg, a CTE and a subquery. | Fact 1. `uuid` stays: no credential column is uuid-shaped except `ReservationToken`, which is now explicit. Deliberate encoding is the accepted residual (D-ML160 d). |
| N2 (names unverified live; add a Docker catalog assertion) | **Accept the intent, amend the place.** The live assertion goes into `AuthStackDockerTests`, the only Docker test that runs the migrations (fact 6), and it reads `pg_attribute` (fact 7). There are two more guards: a non-Docker `SensitiveSchemaTests` that pins the Identity and Nodes entries to their EF snapshots, and the fail-closed name rule. KG-W7-6 tracks the live check (D-ML161). | A catalog assertion in `LabDbDockerTests` would only check the test's own fixture tables, which is a hollow pass. |
| Residual: bit-by-bit probes | **Accept as residual** (D-ML160 d), stated in the README. | Per-stack lab secrets; the threat is accidental disclosure. |
| Unverified: UNION/view origin | **Covered.** A UNION column should report OID 0, so the expression rule applies and masks it. If Postgres reports the left leg instead, the new Docker UNION query (left leg is the non-sensitive base column `"userId"`) goes red. A view falls to rule (b) or to the D2 residual. | Fact 2; section 4 step 5. |
| Gaps: Nodes `TokenHash`, `ReservationToken`; emails | **Nodes columns: accept** (D-ML160 c). **Emails: reject.** | Fact 8: no caller reads the Nodes columns. Emails are lab users' synthetic `@lab.invalid` addresses, which UAT legitimately asserts on, and they are not credentials. |
| `Sensitive` keyed without schema | **No action.** | It fails closed. |

## 4. Fix packet (one Sonnet packet-coder, about 18k)

- **Worktree / branch:** `ml-l35m` / `ml/l35m-dbquery-mask` (continue on head `04d28e15`).
- **Commit subject:** `fix(lab): apply L35m ruling 1 fix notes [L35m]`
- **Security review:** yes. Re-run security-advisor pass 2 together with packet-reviewer pass 2.
- **Rules (inline):** `Result<T>` for validation, IO and network errors, and never `Result<T>.Success(null)`; never print, log or write the lab token, a lease id or a cell value; `using`/`await using` on every `IDisposable`; no package versions in `.csproj` and no package changes at all; nullable on, build with `-p:TreatWarningsAsErrors=true`; xUnit `[Fact]`/`[Theory]` with `Assert.*`, fakes over mocks; Docker tests carry `[Trait("RequiresDocker", "true")]` and `[Trait("RequiresNetwork", "true")]`; never `git worktree prune`, `publish -r` or `restore -r`.

### Step 1: `src/Tools/Dhadgar.Lab/Data/ColumnMask.cs`

Replace `ExpressionMaskedTypes` and the body of `ShouldMask` with this shape. Keep `Masked`, `ExpressionMaskedDbs` and the doc comments, updated to match.

```csharp
internal static readonly FrozenSet<(string Db, string Table, string Column)> Sensitive = new[]
{
    // ... the ten existing entries, unchanged ...
    ("nodes", "enrollment_tokens", "TokenHash"), ("nodes", "capacity_reservations", "ReservationToken"),
}.ToFrozenSet();

// Types an unattributed value may keep: numbers, booleans, dates and times, uuid (D-ML160). Npgsql DataTypeName spelling.
internal static readonly FrozenSet<string> PassThroughTypes = new[]
{
    "smallint", "integer", "bigint", "real", "double precision", "numeric", "boolean", "date",
    "time without time zone", "time with time zone", "timestamp without time zone", "timestamp with time zone",
    "interval", "uuid",
}.ToFrozenSet(StringComparer.Ordinal);

// A base column whose real name carries one of these is credential-like in every database (fail closed on schema drift).
private static readonly string[] SensitiveNameFragments = ["token", "secret", "password", "hash", "payload"];

public static bool ShouldMask(string database, (string Table, string Column)? origin, string dataTypeName)
{
    ArgumentNullException.ThrowIfNull(database);
    ArgumentNullException.ThrowIfNull(dataTypeName);

    if (origin is { } column)
    {
        if (Sensitive.Contains((database, column.Table, column.Column)))
        {
            return true;
        }

        return IsSensitiveName(column.Column) && !PassThroughTypes.Contains(dataTypeName);
    }

    return ExpressionMaskedDbs.Contains(database) && !PassThroughTypes.Contains(dataTypeName);
}

internal static bool IsSensitiveName(string column)
{
    foreach (string fragment in SensitiveNameFragments)
    {
        if (column.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
```

### Step 2: `src/Tools/Dhadgar.Lab/Data/LabDb.cs`

Change `private sealed record ColumnInfo` to `internal sealed record ColumnInfo`, `private sealed record RawRows` to `internal sealed record RawRows`, and `private static DbQueryResult BuildResult(` to `internal static DbQueryResult BuildResult(`. Make no other change. The Lab project already exposes internals to `Dhadgar.Lab.Tests`.

### Step 3: `tests/Dhadgar.Lab.Tests/Data/ColumnMaskTests.cs`

- In `OtherColumn_NotMasked`, replace `Assert.False(ColumnMask.ShouldMask("nodes", ("session", "token"), "text"));` with `Assert.False(ColumnMask.ShouldMask("nodes", ("nodes", "Name"), "text"));`. Under rule (b), the old line is now masked.
- In `Expression_InNodes_NotMasked`, add `Assert.False(ColumnMask.ShouldMask("nodes", null, "name"));`.
- Add `[Theory]` `Expression_NonPassThroughType_Masked(string db, string type)` with `[InlineData]` for `db` in `betterauth` and `identity` × `type` in `name`, `xml`, `tsvector`, `"char"`, `text[]`, `record`, `session`: 14 rows, each `Assert.True(ShouldMask(db, null, type))`. Red on revert: the deny-list passes all seven types.
- Add `[Theory]` `Expression_PassThroughType_NotMasked` over `betterauth`/`identity` × `integer`, `bigint`, `boolean`, `numeric`, `timestamp with time zone`, `uuid`, each `Assert.False`. This is the anti-always-mask control.
- Add `[Fact]` `SensitiveName_BaseColumn_Masked`. True: `("nodes", ("session","token"), "text")`, `("betterauth", ("account","access_token"), "text")` (snake-case drift), `("discord", ("guilds","BotToken"), "character varying")`, `("identity", ("users","PasswordHash"), "text")`. False: `("betterauth", ("account","accessTokenExpiresAt"), "timestamp with time zone")`, `("nodes", ("enrollment_tokens","UsedAt"), "timestamp with time zone")`, `("nodes", ("enrollment_tokens","Id"), "uuid")`. Red on revert: there is no name rule.
- Add `[Fact]` `Nodes_CredentialColumns_Masked`. True: `("nodes", ("capacity_reservations","ReservationToken"), "uuid")`; true: `("nodes", ("enrollment_tokens","TokenHash"), "character varying")`. The first is red without the explicit entry, because `uuid` passes the name rule.

### Step 4: `tests/Dhadgar.Lab.Tests/Data/LabDbBuildResultTests.cs` (new)

There is one `[Fact]`, `BuildResult_MasksByOrigin_KeepsNullsAndNames`. It calls `LabDb.BuildResult("betterauth", raw, origins)` with:

```csharp
var raw = new LabDb.RawRows(
    [
        new LabDb.ColumnInfo("t", 10u, 2, "text"),        // session.token, explicit
        new LabDb.ColumnInfo("id", 10u, 1, "integer"),    // session.id, pass
        new LabDb.ColumnInfo("x", 0u, 0, "text"),         // expression
        new LabDb.ColumnInfo("u", 99u, 3, "text"),        // OID with no catalog match (view/UNION fallback)
        new LabDb.ColumnInfo("s", 10u, 0, "session"),     // whole-row read
        new LabDb.ColumnInfo("n", 0u, 0, "bigint"),       // count(*), pass
    ],
    [
        new object?[] { "canary", 1, "canary", "canary", "(1,canary)", 7L },
        new object?[] { null, 2, null, null, null, 0L },
    ],
    false);
var origins = new Dictionary<(uint TableOid, short Attnum), (string Table, string Column)>
{
    [(10u, (short)2)] = ("session", "token"),
    [(10u, (short)1)] = ("session", "id"),
};
```

If the records are nested inside `LabDb`, refer to them as `LabDb.ColumnInfo`/`LabDb.RawRows`; otherwise drop the prefix.

Assertions:

- In row 0, `t`, `x`, `u` and `s` are `"[masked]"`, `id` is 1 and `n` is 7.
- In row 1, `t`, `x`, `u` and `s` have `ValueKind == JsonValueKind.Null`.
- `Columns` equals `["t","id","x","u","s","n"]`, and `RowCount` is 2.
- No cell's `ToString()` contains `canary`.

Red on revert: this fails if the `masks[i]` test is removed, if the `is not null` guard is removed, or if the `TableOid != 0 && Attnum != 0` fallback is changed to "treat as non-sensitive base column".

### Step 5: `tests/Dhadgar.Lab.Tests/Data/LabDbDockerTests.cs`

- Change the fixture to `CREATE TABLE session (id integer PRIMARY KEY, "userId" text NOT NULL, token text NOT NULL)`, inserting `(1, 'lab-user-1', @secret)`.
- Extend `betterAuthQueries` (one row each) with:
  - `SELECT token::name AS n FROM session`
  - `SELECT xmlelement(name t, token) AS x FROM session`
  - `SELECT ARRAY[token] AS a FROM session`
  - `SELECT q.token FROM (SELECT token FROM session) q`
  - `WITH c AS (SELECT token FROM session) SELECT token FROM c`
  - `SELECT "userId" FROM session WHERE false UNION ALL SELECT token FROM session` (the left leg is a non-sensitive base column; this tests UNION origin)
- Add an identity query: `SELECT "TokenHash"::name AS n FROM refresh_tokens`.
- Add two pass-through controls (anti-hollow), asserted directly rather than through `AssertMaskedWithoutSecret`:
  - `SELECT "userId" FROM session` returns `"lab-user-1"`.
  - `SELECT count(*) AS n FROM session` returns `1` (`GetInt64()`).
- Leave the existing four queries and the helper unchanged.

### Step 6: `tests/Dhadgar.Lab.Tests/Services/AuthStackDockerTests.cs`

Directly after the sign-up assertion (after line 139, inside the `try`), add a block. Use `new LabDb(connections)` and `ct`.

1. For each `(Db, Table, Column)` in `ColumnMask.Sensitive`, run this on `Db`, with `t` and `c` bound as `JsonSerializer.SerializeToElement(...)` and `max_rows` 1:

   ```sql
   SELECT count(*) AS n FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace s ON s.oid = c.relnamespace WHERE s.nspname = 'public' AND c.relname = @t AND a.attname = @c AND NOT a.attisdropped
   ```

   Assert success, and assert `Rows[0]["n"].GetInt64() == 1`, with the message `$"{Db}.{Table}.{Column} is not in the live schema"`. The names are schema names, not secrets.
2. Run `SELECT token FROM session` on `betterauth`, `max_rows` 10. Assert success, `RowCount >= 1` (message: "sign-up created no session row"), and every cell equals `ColumnMask.Masked`. Never print a cell.

Add `using Dhadgar.Lab.Data;` and `using System.Text.Json;` if they are missing.

### Step 7: `tests/Dhadgar.Lab.Tests/Data/SensitiveSchemaTests.cs` (new, no Docker)

There is one `[Fact]`, `SensitiveEntries_MatchEfSnapshots`.

- Find the repo root with `LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)`.
- Read `src/Dhadgar.Identity/Data/Migrations/IdentityDbContextModelSnapshot.cs` for `identity` entries, and `src/Dhadgar.Nodes/Data/Migrations/NodesDbContextModelSnapshot.cs` for `nodes` entries. Skip `betterauth` entries (KG-W7-6 covers them).
- Split each snapshot on `modelBuilder.Entity(`. For each entry, find the one chunk that contains `b.ToTable("<Table>",`. Assert that chunk contains `b.Property<` and `>("<Column>")`. The simplest form is a `Regex` built with `Regex.Escape(column)`: `b\.Property<[^>]+>\("` + escaped + `"\)`.
- The failure message names the entry.

Red on revert: renaming any Identity or Nodes entry in `Sensitive` (for example `TokenHash` to `token_hash`) fails it.

### Step 8: `src/Tools/Dhadgar.Lab/README.md` (lines 74-79)

Keep line 74. Replace lines 76-79 with:

```markdown
- Masked base columns (any type): `betterauth` `session.token`, `account.accessToken`, `account.refreshToken`, `account.idToken`, `account.password`, `verification.value`; `identity` `refresh_tokens.TokenHash`, `OpenIddictTokens.Payload`, `OpenIddictTokens.ReferenceId`, `OpenIddictApplications.ClientSecret`; `nodes` `enrollment_tokens.TokenHash`, `capacity_reservations.ReservationToken`.
- Name rule, every database: a base column whose real name (from the catalog, not the alias) contains `token`, `secret`, `password`, `hash` or `payload` is masked unless its type is numeric, boolean, date/time or `uuid`. This fails closed if a schema renames a listed column.
- Expression rule, `betterauth` and `identity`: a value with no base column (an expression, a cast, a UNION column, a whole-row read, or a column the catalog cannot resolve) is masked unless its type is numeric, boolean, date/time or `uuid`. In `nodes`, `notifications` and `discord`, expressions pass through.
- Limits (D-ML160): masking stops accidental disclosure, not a hostile query author. A statement can still read a secret a bit at a time through numbers or booleans (`ascii(substr(token,1,1))`, `token LIKE 'a%'`). A view reports its own OID, so a view over a listed column is caught only by the name rule; neither database defines one, and `lab_reader` cannot create one.

Masking is KG-W7-1 (L35m). The live check of the listed names against the migrated schemas is KG-W7-6 (`AuthStackDockerTests`).
```

### Checks (in order, non-Docker; `<W>` = `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l35m`)

```powershell
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~ColumnMaskTests|FullyQualifiedName~LabDbBuildResultTests|FullyQualifiedName~SensitiveSchemaTests|FullyQualifiedName~LabDbTests)&RequiresDocker!=true"
```

Do **not** run `LabDbDockerTests` or `AuthStackDockerTests`. The Docker classes only have to compile. In the report, record one hand mutation and then restore it: delete the `IsSensitiveName(...)` line in `ShouldMask`, show that `SensitiveName_BaseColumn_Masked` goes red, then restore and rebuild. No lock file may change. If `git status` shows a `packages.lock.json` diff, revert it and report it.

## 5. Docker and approvals

- **`LabDbDockerTests` still needs the user's yes.** It has never been run on this packet, and it is the merge gate (D-ML161). After the fix pass and green reviews, the orchestrator asks the user, then a check-only packet-coder runs `dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabDbDockerTests"` with Docker Desktop running (context `desktop-linux`). A red UNION query there means Postgres attributes UNION columns to the left leg. In that case the orchestrator brings it back to the architect; it is not a coder fix.
- **`AuthStackDockerTests`** needs Docker plus network (npm restore for BetterAuth and Panel) and about 25 minutes. It is not a merge gate. It closes KG-W7-6 and can ride the wave 7 Docker run (D-ML74). If labelled lab containers exist, it needs `MERIDIAN_LAB_S6_WIPE=1`, which is a separate user yes because it wipes them.

## 6. Ledger edits for the orchestrator

| Where | Edit |
| --- | --- |
| README findings | Add **F133**: "`db_query` expression rule was a type deny-list: `token::name`, `xmlelement(...)`, `to_tsvector(token)`, `ARRAY[token]`, `ROW(token)` returned BetterAuth/Identity secrets unmasked. Evidence: `Data/ColumnMask.cs:27-30,48` at `04d28e15`; [l35m ruling 1](worknotes/l35m-ruling-1.md). Owner: L35m fix pass 1 (D-ML160)." |
| README findings | Add **F134**: "`ColumnMask.Sensitive` names were checked only statically, and a wrong name returned plaintext (fail-open on schema drift). The Docker fixture creates its own tables, so it cannot detect a name mismatch. Evidence: `LabDbDockerTests.cs:183-189`; l35m ruling 1 fact 6. Owner: L35m fix pass 1 (name rule, `SensitiveSchemaTests`) and KG-W7-6." |
| README decisions | Add **D-ML160** and **D-ML161** (text in section 2), "Accepted (architect)", source l35m ruling 1. Append "type list amended by D-ML160" to the D-ML154 row. |
| README known gaps | KG-W7-1: owner and closer `L35` → `L35m`. It closes at the L35m merge with `LabDbDockerTests` green (D-ML161). |
| README known gaps | Add **KG-W7-6**: "**Pending live check:** `ColumnMask.Sensitive` names (BetterAuth camelCase in particular, `verification.value` most of all, because the name rule does not cover it) are not yet confirmed against the migrated schemas. \| The new `AuthStackDockerTests` step (pg_attribute lookup per entry, `session.token` masked after sign-up) \| L35m (closes on that Docker run, D-ML74 wave 7 run)". |
| README packet row L35m | Status: "Fix pass 1 ([l35m ruling 1](worknotes/l35m-ruling-1.md)); Docker yes owed (`LabDbDockerTests`)". |
| work-packets.md L35 file 5 (line 3688) | Delete the sentence that begins "`LabDb` masks the values ..." through "(KG-W7-1)." Replace it with "Masking is L35m (D-ML154, D-ML160)." This is the D-ML154 follow-up that never landed. |
| next-batch-plan.md 7.3 | Add a line under the heading: "Amended by [l35m ruling 1](l35m-ruling-1.md) (D-ML160, D-ML161): pass-through-type expression rule, name rule, Nodes entries, live-name step in `AuthStackDockerTests`." |
| RESUME | Next free IDs: **F135, D-ML162, KG-W5-24, KG-W7-7**. L35m: fix pass 1 → reviewer and security pass 2 → user's Docker yes for `LabDbDockerTests` → merge. |
| PR | `security-review` label (reviewer D5, security verdict). |
