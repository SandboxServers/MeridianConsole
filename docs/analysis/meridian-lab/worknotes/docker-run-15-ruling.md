# Ruling: Docker run 15 (L35m `LabDbDockerTests`, L37 live `cli.toml`)

Date 2026-11-02. Architect. Input: the raw report `.claude/reports/meridian-lab/docker-run-15.md` (local, gitignored), [l35m ruling 1](l35m-ruling-1.md) and [l37 ruling 1](l37-ruling-1.md). Campaign tip **2efc332f** (RESUME still says d5f45ef7; 2efc332f is a docs commit on top of it).

IDs used: F143 to F147, D-ML168 to D-ML174, KG-W7-9 and KG-W7-10. KG-W5-24 is not used. Next free: **F148, D-ML175, KG-W5-24, KG-W7-11**.

No owner decision is needed. Every decision below is the architect's: these are lab defects and their fix shape, and they change no product behaviour.

## 1. Run 15 in one paragraph

Run A built clean. Three of the four `LabDbDockerTests` passed. `DbQuery_TokenColumns_Masked` failed with `InvalidCastException: Writing values of 'System.UInt32[]' is not supported for parameters having no NpgsqlDbType or DataTypeName`, thrown from `LabDb.ResolveOriginsAsync` (LabDb.cs:280). Run B (live `cli.toml`) did not start. The agent had no lab MCP tools, no lab token and no daemon, and it rightly did not improvise. The host was clean before and after (no labelled containers, no lab network, port 8789 free).

## 2. Findings

| ID | Finding | Evidence |
| --- | --- | --- |
| F143 | **Every `LabDb` read that returns a base-table column throws.** `ResolveOriginsAsync` binds the catalog lookup as `new NpgsqlParameter<uint[]>("oids", oids)` with no `NpgsqlDbType`. Npgsql 10 has no default PostgreSQL type for `uint` (it could be `oid`, `xid` or `cid`), so it refuses the write. The three passing tests never select a table column (`create table`, `select @v::text`, `select 1`), so `oids` is empty and the lookup returns before it binds anything (LabDb.cs:270-273). `QueryAsync` catches only `PostgresException` and `NpgsqlException` (LabDb.cs:147-163), so the cast exception escapes. The same path breaks every caller that selects a column: the I2 outbox invariant (`select "SequenceNumber"`, `select "OutboxId"`, Invariants/I2OutboxInvariant.cs:132,135), I3, `DbSource` clauses (Uat/Eval/DbSource.cs), `TransitionSnapshotter.ForLabDb`, `ModelHunter`, `HistoryRunJob`, and the KG-W7-6 step in `AuthStackDockerTests`. An invariant check that throws is reported as "check threw InvalidCastException" (InvariantMonitor.cs:74-77), and a row whose window holds a check error is graded UNVERIFIED (RowGrader.cs:118-121). **Prediction (not run):** S-L6 (`LabSelfTestE2ETests`, which asserts zero invariant errors) has been red since the L35m merge at `4532ef7c`. L35m merged before its Docker gate (D-ML161), so nothing caught this. | raw report Run A; LabDb.cs:258-288, 98-163; the callers named |
| F144 | **No agent-runnable path for a live spec run exists.** `meridian-lab uat run` (README line 335) calls `uat_run` on a running daemon and reads the token only from `MERIDIAN_LAB_TOKEN`. `lab_build` is Leased and has no CLI verb (README line 200), so a CLI-only caller cannot build the member image or the CLI for the tip. `%LOCALAPPDATA%\meridian-lab\builds\latest.txt` points at a stale `b9e693f0-dirty` build. The only in-process paths are S-L6 (`LabSelfTestE2ETests`, `lab-selftest.toml`) and `FaultSmokeE2ETests`. Neither runs `cli.toml`, and no shipped spec except `cli.toml` has `requires_pool` (grep `requires_pool` over `*.toml`: one hit). | raw report Run B; README:200,335,343-345; LabSelfTestE2ETests.cs |
| F145 | **A fresh lab cannot provision a Linux member.** `LinuxContainerProvider.RunMemberAsync` runs `docker run ... --network meridian-lab-net` (LinuxContainerProvider.cs:115) without making sure the network exists. Only `LabStack.EnsureNetworkAsync` (LabStack.cs:345-362) creates it, inside `stack_up`. `uat_run` acquires the section's pool once, before any row (UatRunner.cs:190-205), and `cli.toml`'s first row (`version`) has no `stack_up`. On run 15's start state (no lab network) the acquire fails and every row is SKIPPED with `pool: [Lab.Docker] member container could not be started` (RowExecutor.cs:81-83). `LinuxPoolDockerTests` creates the network itself, which hides the gap. `IsAvailableAsync` checks only the image (LinuxContainerProvider.cs:45-57). | the cited lines |
| F146 | **`stack_up wipe` destroys the pool members the lease holds.** `ListLabelledContainersAsync` keeps every container labelled `meridian-lab=1` whose name starts with `meridian-lab-` (LabStack.cs:886-905). Members are named `meridian-lab-m-<id>` and labelled `meridian-lab=1` (LinuxContainerProvider.cs:16,113). The wipe's orphan sweep removes every listed name that is not a registered `Container` target (LabStack.cs:947-978). Members are registered as `Member` targets (PoolManager.cs:448), so they are not in `known`. In `cli.toml`, rows `auth-login`, `nodes-list`, `notifications-admin` and `gateway-down` each run `stack_up wipe = true` after the runner's per-row reset, so every later `cli_run` targets a removed container. Without wipe, `UpAsync` also refuses to start while a held member exists (LabStack.cs:99-111). | the cited lines; cli.toml rows |
| F147 | **`uat_run` never installs the CLI into the members it acquires or resets.** Only the `pool_acquire` and `member_reset` tools call `CliMemberInstall.InstallIntoMemberAsync` (PoolTools.cs:114,243). The runner calls `PoolManager.AcquireAsync` directly (UatRunner.cs:193) and resets through `pool.ResetAsync` (UatRunner.cs:98, RowExecutor.cs:111). A reset recreates the container from the bare image, and the image has no CLI. Every `cli_run` in `cli.toml` would therefore find no `/opt/lab/cli/dhadgar`. `CliMemberInstall`'s doc comment (CliRunTool.cs:277-278) names only the two tools. `RunnerPoolTests` uses a fake provider and never checks for an install. | the cited lines; grep `InstallIntoMemberAsync` (3 hits, all in CliRunTool.cs and PoolTools.cs) |

Sibling check for the `UInt32[]` bug (the brief's ask): there is no other occurrence. A grep over the campaign tree for `NpgsqlParameter<`, `uint[]` and `ANY(@` finds only LabDb.cs:263,276,278. `DbFault` binds a string with `AddWithValue` (Faults/Kinds/DbFault.cs:476-499), and that write is mapped. `LabDb.BindParameters` binds only string, bool, long, decimal or `DBNull`. `LabDataReset` binds no arrays. `ResultsDb` is SQLite. The L35m mask's helpers (`ColumnMask`, `BuildResult`) take `uint` keys in memory only.

F145 to F147 were found by reading source. Run 15 never got far enough to show them. They are listed now because the live run would otherwise go red three times in a row, needing three yeses. The L37f3 E2E test is the check that proves them.

## 3. Decisions

| ID | Decision |
| --- | --- |
| D-ML168 | **F143 fix: type the parameter, not the SQL.** An internal helper `LabDb.OidArrayParameter(string name, uint[] oids)` returns `new NpgsqlParameter<uint[]>(name, oids) { NpgsqlDbType = NpgsqlDbType.Array \| NpgsqlDbType.Oid }`, and LabDb.cs:278 uses it. Rejected options: (a) `int[]` with `::oid[]`, because an OID above 2^31-1 overflows `int`; (b) `long[]` with `::bigint[]`, because it changes the comparison type and needs a cast on `c.oid`; (c) catching `InvalidCastException` in `QueryAsync`. The throw keeps masking fail-closed: a query whose columns cannot be resolved returns no data rather than unmasked data. |
| D-ML169 | **L35m stays merged and gets a fix packet, L35mf.** It uses a new worktree `ml-l35m-fix` from the campaign tip on branch `ml/l35m-fix-oid`, because `ml-l35m` is merged and must not be reused. D-ML161 is amended: KG-W7-1 closes when all four `LabDbDockerTests` pass on a campaign tip that includes L35mf. A red caused by a mask assertion (UNION, CTE or identity queries) is not a coder fix. It comes back to the architect ([l35m ruling 1](l35m-ruling-1.md) section 5). |
| D-ML170 | **The live `cli.toml` run is a Docker test, `CliSpecE2ETests` (packet L37f3).** It mirrors S-L6: an in-process `LabDaemon` on port 0 with a temp data root, a token generated in memory with `RandomNumberGenerator` (never an environment variable, file or log), then `lab_lease_acquire`, `lab_build`, `uat_run cli.toml` and `lab_lease_release`. Agents need no lab MCP tools, no user-held token and no daemon. Agents do not use the `meridian-lab uat run` path, because it needs the token in the caller's environment and has no build verb (F144). The opt-in guard is the same as S-L6: `MERIDIAN_LAB_S6_WIPE=1` when labelled containers exist. |
| D-ML171 | **Pool members survive a stack wipe (F146).** `ListLabelledContainersAsync` also excludes the `ContainerId` (the container name) of every registered `Member` target. The registry is the source of truth, so `LabStack` takes no dependency on `PoolManager`. Unregistered `meridian-lab-m-*` containers, left by a dead daemon or another data root, are still wiped. R-BLAST is unchanged: removal still needs both the label and the prefix. The non-wipe orphan refusal uses the same listing, so a held member no longer blocks `stack_up`. |
| D-ML172 | **The Linux provider makes sure the lab network exists (F145)** before `docker run`. It runs `network inspect` and, when that exits non-zero, `network create --label meridian-lab=1 meridian-lab-net`. A failed create is followed by one more inspect, because the stack or a second provision may have created the network first. The network name comes from `LabDefaults.NetworkName`, and the provider's private `LabNetwork` constant goes. If the network still does not exist, the provision fails with `[Lab.Docker] could not create the lab network` and never runs `docker run`. |
| D-ML173 | **`uat_run` prepares members the same way the tools do (F147).** A new `Dhadgar.Lab.Pool.IPoolMemberPreparer` is implemented by `CliMemberPreparer`, which wraps `CliMemberInstall.InstallIntoMemberAsync`, and is registered in `CliArea`. `UatRunner` takes it as a trailing optional constructor parameter and calls it in two places. After the section acquire, a failure becomes `run.PoolError`: rows are SKIPPED with `pool: <error>` and the members are still released. After each row reset, a failure BLOCKS the row, as a failed reset already does. The `pool_acquire` and `member_reset` tools keep their direct calls. Moving them onto the preparer is not required. |
| D-ML174 | **Merge order and gates.** L35mf merges first, as its own unit. L37f1, L37f2 and L37f3 then go on one branch and merge as one unit after a packet-reviewer pass and a security-advisor pass. The L37 merge gate (l37 ruling 1 section 5) is now **`CliSpecE2ETests` green on a tip that contains L35mf and L37f**. Without L35mf, the I2 and I3 check errors would grade every stack row UNVERIFIED (F143). |

## 4. Known gaps

| ID | Gap | Owner | Closes when |
| --- | --- | --- | --- |
| KG-W7-9 | Lab DB reads of base columns throw (F143): `db_query` on table columns, I2/I3 invariant checks, `db` clauses. S-L6 is predicted red. | L35mf | `LabDbDockerTests` 4/4 and S-L6 green on a tip with L35mf |
| KG-W7-10 | `cli.toml` has never run live; the L37 merge gate and the live half of the D-ML163 seam are open (F144 to F147). | L37f1 to L37f3 | `CliSpecE2ETests` green |

## 5. Packets

Shared rules for all four packets. They are inlined because packet-coder skips CLAUDE.md.

1. Use `Result`/`Result<T>` for failures, never exceptions. Never return `Success(null)`: the shared `Result<T>` throws on a null success.
2. Every `docker` call goes through `IDockerCli` with an argument list (`ArgumentList`), never a built string.
3. Use `using` / `await using` on every `IDisposable` and `Process`.
4. No package changes, no versions in `.csproj`, and no lock-file changes. `git status` must show only the packet's files.
5. Nullable is enabled. Build with `-p:TreatWarningsAsErrors=true`.
6. The test project is xUnit, with `[Fact]` / `[SkippableFact]`. Docker tests carry `[Trait("RequiresDocker","true")]` and `[Trait("RequiresNetwork","true")]` and `[Collection(LabDockerGate.Name)]`.
7. Non-Docker filter: `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`. Run the full non-Docker suite once, serially, at the end. Never run two suites at once. Known flake: `ModelHunterTests.Hunter_SameSeed_SameFirstOpList`. Re-run it alone once before reporting it.
8. Never `publish -r`, `restore -r` or `git worktree prune`. Never run a Docker test: the Docker runs in section 7 need the user's yes.
9. Never print, log or write the lab token or a lease id.
10. The lab touches only containers and networks labelled `meridian-lab=1` with names starting `meridian-lab-` (R-BLAST).

### L35mf: type the oid[] catalog parameter (F143, D-ML168)

- **Worktree / branch:** ops creates `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l35m-fix` on a new branch `ml/l35m-fix-oid` from `campaign/meridian-lab` at 2efc332f. It does not reuse `ml-l35m`.
- **Size:** small (well under 40k). **Security review:** not required. The change binds one catalog parameter. The reviewer checks that no catch was widened (D-ML168 c).
- **Files:**
  - `src/Tools/Dhadgar.Lab/Data/LabDb.cs`:
    - add `using NpgsqlTypes;`;
    - add, next to `ResolveOriginsAsync`:

      ```csharp
      /// <summary>
      /// The catalog lookup's OID list. Npgsql has no default PostgreSQL type for <c>uint</c> (oid, xid or cid), so the array is typed as <c>oid[]</c> (F143).
      /// </summary>
      internal static NpgsqlParameter<uint[]> OidArrayParameter(string name, uint[] oids) =>
          new(name, oids) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Oid };
      ```

    - line 278 becomes `_ = catalog.Parameters.Add(OidArrayParameter("oids", oids));`.
    - Change nothing else. In particular, keep the catches at lines 147-163 as they are.
  - New `tests/Dhadgar.Lab.Tests/Data/LabDbOidParameterTests.cs`, namespace `Dhadgar.Lab.Tests.Data`, with `using Dhadgar.Lab.Data;` and `using NpgsqlTypes;`:

    ```csharp
    public sealed class LabDbOidParameterTests
    {
        [Fact]
        public void OidArrayParameter_IsTypedAsOidArray_SoNpgsqlCanWriteUInt32Values()
        {
            // uint.MaxValue: an OID above int.MaxValue is why the list is not bound as int[] (D-ML168).
            NpgsqlParameter<uint[]> parameter = LabDb.OidArrayParameter("oids", [16384u, uint.MaxValue]);

            Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Oid, parameter.NpgsqlDbType);
            Assert.Equal("oids", parameter.ParameterName);
            Assert.Equal([16384u, uint.MaxValue], parameter.TypedValue);
        }
    }
    ```

    Add `using Npgsql;` for `NpgsqlParameter<T>`.
- **Why it is red on revert:** without the initializer, `NpgsqlDbType` reads back as `Unknown` (or the getter throws), so the first assert fails. The test cannot prove that Npgsql *writes* the value, because that needs a server. That proof is the Docker check `LabDbDockerTests.DbQuery_TokenColumns_Masked` (section 7, run 1).
- **Checks, in order** (the worktree root is the directory above):
  1. `dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
  2. `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Dhadgar.Lab.Tests.Data&RequiresDocker!=true&RequiresNetwork!=true"`
  3. Hand mutation: delete the `{ NpgsqlDbType = ... }` initializer, rebuild, run the new test, and confirm it is red. Restore the initializer, rebuild, and confirm it is green. Report both results. Never commit the mutated form.
  4. Run the full non-Docker suite once: `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`
  5. `git status`: only the two files.
- **Docs owed:** none. The behaviour is unchanged; the defect only stopped it from working.
- **Commit subject:** `fix(lab): type the oid[] catalog parameter in db_query [L35mf]`

### L37f1: lab network for members; members survive a stack wipe (F145, F146, D-ML171, D-ML172)

- **Worktree / branch:** ops creates `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l37-live` on a new branch `ml/l37-live` from `campaign/meridian-lab` at 2efc332f. L37f2 and L37f3 follow on the same branch, in order, each with its own commit. The branch shares no files with L35mf.
- **Size:** about 25k. **Security review: yes** (R-BLAST: network create, wipe scope). The security-advisor runs once on the whole `ml/l37-live` branch, after L37f3.
- **Files:**
  - `src/Tools/Dhadgar.Lab/Pool/LinuxContainerProvider.cs`:
    - remove `private const string LabNetwork` and use `LabDefaults.NetworkName` in the run args;
    - add a private `EnsureNetworkAsync(CancellationToken)`, called at the start of `RunMemberAsync`, before the `try`. If it fails, return `Result<PoolMember>.Failure(<its error>)` without running `docker run`.
    - Shape (the args mirror `LabStack.EnsureNetworkAsync`):

      ```csharp
      private async Task<Result> EnsureNetworkAsync(CancellationToken ct)
      {
          Result<DockerOutput> inspect = await _docker.RunAsync(DockerEngine.Linux, ["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
          if (inspect.IsFailure) return Result.Failure(inspect.Error);
          if (inspect.Value.ExitCode == 0) return Result.Success();

          Result<DockerOutput> created = await _docker.RunAsync(DockerEngine.Linux, ["network", "create", "--label", LabDefaults.ContainerLabel + "=1", LabDefaults.NetworkName], ct).ConfigureAwait(false);
          if (created.IsFailure) return Result.Failure(created.Error);
          if (created.Value.ExitCode == 0) return Result.Success();

          // The stack or another provision may have created it first (D-ML172).
          Result<DockerOutput> again = await _docker.RunAsync(DockerEngine.Linux, ["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
          return again.IsSuccess && again.Value.ExitCode == 0
              ? Result.Success()
              : Result.Failure("[Lab.Docker] could not create the lab network");
      }
      ```

      Use the repo's brace style (braces on every `if`). The shape above is compressed.
    - Update the class doc comment to say the provider creates the labelled lab network when it is missing.
  - `src/Tools/Dhadgar.Lab/Stack/LabStack.cs`, `ListLabelledContainersAsync` (lines 886-905):
    - build `held` from `_targets.All.Where(t => t.Kind == LabTargetKind.Member && !string.IsNullOrEmpty(t.ContainerId)).Select(t => t.ContainerId!)` as an ordinal `HashSet<string>`;
    - add `&& !held.Contains(name)` to the prefix filter;
    - update the doc comment: "...registered or not, except the containers of registered pool members, which the pool owns (D-ML171)".
    - Before you write it, check that the fourth `Register` argument is the container name at PoolManager.cs:375, :448 and :560. If any of them is not, stop and report blocked.
  - `tests/Dhadgar.Lab.Tests/Pool/LinuxContainerProviderTests.cs`:
    - give `ScriptedDockerCli` the properties `NetworkInspectExit` (default 0) and `NetworkCreateExit` (default 0). They answer `network inspect` and `network create`. Existing tests keep passing because both default to 0.
    - Add three tests:
      - `Provision_NetworkMissing_CreatesTheLabelledLabNetworkBeforeTheRun`: `NetworkInspectExit = 1`. Assert that the calls contain exactly `["network","create","--label","meridian-lab=1","meridian-lab-net"]`, that its index is before the `run` call's index, and that the run args contain `"--network"` followed by `"meridian-lab-net"`.
      - `Provision_NetworkPresent_DoesNotCreateIt`: the defaults. Assert there is no `network create` call.
      - `Provision_NetworkCannotBeCreated_FailsWithoutRunning`: both values 1. Assert a failure containing `[Lab.Docker] could not create the lab network`, and no call with `args[0] == "run"`.
  - `tests/Dhadgar.Lab.Tests/Stack/LabStackTests.cs`, next to `UpAsync_WipeWithOrphanedLabelledContainer_RemovesOnlyTheLabPrefixedName` (line 148). Add two tests:
    - `UpAsync_Wipe_KeepsHeldPoolMembers_RemovesUnheldOnes`: `CreateStack(out LabTargetRegistry registry)`, then `registry.Register("m:lin-01", LabTargetKind.Member, "lin-01", "meridian-lab-m-lin-01")`. Set `_docker.PsOutput = "meridian-lab-postgres\nmeridian-lab-m-lin-01\nmeridian-lab-m-lin-02"` and run `UpAsync(wipe: true, ...)`. Assert `rm -f -v meridian-lab-postgres` and `rm -f -v meridian-lab-m-lin-02` are present, and that no call contains both `rm` and `meridian-lab-m-lin-01`.
    - `UpAsync_WithoutWipe_HeldPoolMemberIsNotAnOrphan`: register the same member, set `PsOutput = "meridian-lab-m-lin-01"` and run `UpAsync(wipe: false, ...)`. Assert the result error, if any, does not contain `labelled lab containers exist that this daemon does not know`. Mirror the assertion style of `UpAsync_WithoutWipeAndNoLabelledContainers_IsNotRefused`.
- **Why they are red on revert:**
  - without `EnsureNetworkAsync`, the "missing" and "cannot be created" tests fail (no create call; a `run` call happens);
  - without the `held` filter, the wipe test sees `rm -f -v meridian-lab-m-lin-01`, and the non-wipe test gets the refusal.
- **Checks:**
  1. Build, as in L35mf.
  2. `dotnet test ... --no-build --filter "(FullyQualifiedName~LinuxContainerProviderTests|FullyQualifiedName~LabStackTests|FullyQualifiedName~RunnerPoolTests|FullyQualifiedName~BlastRadiusTests)&RequiresDocker!=true&RequiresNetwork!=true"`
  3. Hand mutations, one at a time: remove the `EnsureNetworkAsync` call, then remove `&& !held.Contains(name)`. Each must turn its tests red. Restore each one and report.
  4. Run the full non-Docker suite once, as in L35mf.
- **Docs owed:** in `src/Tools/Dhadgar.Lab/README.md`, in the pool section (near line 275, the "Image" bullet), add one bullet: "The Linux provider creates the labelled `meridian-lab-net` network when it is missing; `stack_up wipe` leaves the containers of members the pool holds (D-ML171, D-ML172)".
- **Commit subject:** `fix(lab): pool members get the lab network and survive stack wipe [L37f1]`

### L37f2: install the CLI into members that `uat_run` acquires and resets (F147, D-ML173)

- **Worktree / branch:** `ml-l37-live` / `ml/l37-live`, after the L37f1 commit.
- **Size:** about 30k. **Security review: yes** (in the branch pass). It copies binaries into containers through `docker exec` and `docker cp`, reusing the existing `CliMemberInstall`.
- **Files:**
  - New `src/Tools/Dhadgar.Lab/Pool/IPoolMemberPreparer.cs`:

    ```csharp
    /// <summary>
    /// Prepares a pool member after the pool provisions or resets it, before a UAT row uses it (D-ML173).
    /// </summary>
    public interface IPoolMemberPreparer
    {
        /// <summary>Prepares one member. A failure is returned, never thrown.</summary>
        Task<Result> PrepareAsync(PoolMember member, CancellationToken ct);
    }
    ```

  - New `src/Tools/Dhadgar.Lab/Tools/Cli/CliMemberPreparer.cs`: `public sealed class CliMemberPreparer : IPoolMemberPreparer`. Its constructor takes `(IDockerCli docker, LabPaths paths, IPathValidator validator)` with null guards, and `PrepareAsync` returns `CliMemberInstall.InstallIntoMemberAsync(_docker, _paths, _validator, member, ct)`.
  - `src/Tools/Dhadgar.Lab/Mcp/Areas/CliArea.cs`: `services.AddSingleton<IPoolMemberPreparer, CliMemberPreparer>();` (R-REG: this area file only).
  - `src/Tools/Dhadgar.Lab/Uat/UatRunner.cs`:
    - add a trailing optional constructor parameter `IPoolMemberPreparer? preparer = null` and store it in `_preparer` *before* `_rows` is built;
    - the `RowExecutor` reset delegate becomes `pool is null ? null : (id, ct) => ResetAndPrepareAsync(pool, id, ct)`, with:

      ```csharp
      private async Task<Result<PoolMember>> ResetAndPrepareAsync(PoolManager pool, string memberId, CancellationToken ct)
      {
          Result<PoolMember> reset = await pool.ResetAsync(memberId, ct).ConfigureAwait(false);
          if (reset.IsFailure || _preparer is null)
          {
              return reset;
          }

          Result prepared = await _preparer.PrepareAsync(reset.Value!, ct).ConfigureAwait(false);
          return prepared.IsFailure ? Result<PoolMember>.Failure(prepared.Error) : reset;
      }
      ```

    - in the acquire block (lines 196-200), after `acquiredMembers = run.Members;`, when `_preparer` is not null, call `PrepareAsync` for each acquired member in order. On the first failure, set `run.PoolError = prepared.Error` and stop. Keep `acquiredMembers` set so that the release loop at line 291 still releases them.
    - add `internal bool PreparesMembers => _preparer is not null;` with a doc comment ("for the wiring test").
  - `src/Tools/Dhadgar.Lab/Tools/Cli/CliRunTool.cs` line 278, the doc comment only: "Used by `pool_acquire`, `member_reset` and `uat_run` (through `CliMemberPreparer`), because a reset recreates the container from the bare image."
  - `tests/Dhadgar.Lab.Tests/Pool/RunnerPoolTests.cs`:
    - add a private `RecordingPreparer : IPoolMemberPreparer`, with `List<string> Prepared` (member ids) and `string? FailWith`;
    - construct `_runner` with `preparer: _preparer`. The existing tests stay green because the preparer only records.
    - Add three tests:
      - `Section_PreparesTheMemberAfterAcquireAndAfterEachReset`: two rows (`RowA` and the `"b"` copy). Assert `_preparer.Prepared` equals `["lin-01", "lin-01"]` and both rows pass.
      - `ReuseMember_PreparesOnlyOnce`: the second row has `reuse_member = true`. Assert `["lin-01"]`.
      - `PreparerFailure_SkipsEveryRowWithThePoolError_AndReleasesTheMember`: `FailWith = "[Lab.Docker] cli install failed"`. Assert every row is `Skipped`, its reason contains `pool: [Lab.Docker] cli install failed`, and `_linux.Destroyed` equals `["lin-01"]`.
  - New `tests/Dhadgar.Lab.Tests/Pool/MemberPreparerWiringTests.cs`, `[Collection("LabHome")]`. Copy the daemon start of `InvariantsWiringTests` (port 0, temp root, in-memory token from `RandomNumberGenerator` + `Base64Url`, `Array.Clear` on the bytes, `FakeTimeProvider`). Assert `Assert.IsType<CliMemberPreparer>(daemon.Services.GetRequiredService<IPoolMemberPreparer>())` and `Assert.True(daemon.Services.GetRequiredService<UatRunner>().PreparesMembers)`. If resolving `UatRunner` needs Docker in this test, stop and report blocked rather than add a Docker trait.
- **Why they are red on revert:**
  - without the acquire call, Prepared is `["lin-01"]` in the first test and empty in the second;
  - without the reset call, it is `["lin-01"]` in the first test;
  - without the `PoolError` assignment, the failure test's rows pass;
  - without the `CliArea` registration, the wiring test fails.
- **Checks:**
  1. Build.
  2. `--filter "(FullyQualifiedName~RunnerPoolTests|FullyQualifiedName~MemberPreparerWiringTests|FullyQualifiedName~PoolAcquireCliInstallTests|FullyQualifiedName~UatRunnerTests|FullyQualifiedName~InvariantsWiringTests)&RequiresDocker!=true&RequiresNetwork!=true"`
  3. Hand mutations: remove the acquire-time call, then the reset-time call. Each must go red. Restore each one.
  4. Run the full non-Docker suite once.
- **Docs owed:** in `src/Tools/Dhadgar.Lab/README.md` line 85, the sentence saying `pool_acquire` installs the CLI gains: "and so does `uat_run` for the members a `requires_pool` section acquires and resets (D-ML173)".
- **Commit subject:** `fix(lab): install the CLI into members uat_run acquires and resets [L37f2]`

### L37f3: run the shipped `cli.toml` through an in-process daemon (F144, D-ML170)

- **Worktree / branch:** `ml-l37-live` / `ml/l37-live`, after L37f2.
- **Size:** about 25k. **Security review: yes** (in the branch pass). The test generates and handles a lab token.
- **Files:**
  - New `tests/Dhadgar.Lab.Tests/Uat/CliSpecE2ETests.cs`. Copy `LabSelfTestE2ETests.cs` and change only what is listed:
    - class `CliSpecE2ETests`; `private const string CliSpec = "cli.toml";`; the same `WipeEnvVar = "MERIDIAN_LAB_S6_WIPE"` (D-ML170);
    - `[SkippableFact(Timeout = 2_400_000)]`, `[Trait("Seam", "D-ML163")]`, `[Trait("RequiresDocker","true")]`, `[Trait("RequiresNetwork","true")]`, `[Collection(LabDockerGate.Name)]`;
    - the test name is `CliSpec_EveryRowPasses_SecretScanPasses_NoInvariantErrors`;
    - the home folder is `meridian-lab-cli-<guid>`. The lease owner is `"l37-cli-live"` and the purpose `"L37 live cli.toml"`;
    - the same pre-check: skip when docker is unreachable, and fail when labelled `meridian-lab-` containers exist without the opt-in;
    - the same sequence: `lab_lease_acquire`, then `lab_build` (`checkout` = repo root), then `uat_run` (`spec = CliSpec`, `specs_dir` = `<repo>/docs/guides/uat-specs`, `lease_id`), with `lab_lease_release` in a `finally`;
    - `CallToolAsync` HttpClient timeout: 40 minutes;
    - assertions:
      - exactly five rows, and every row `grade == "pass"`. Use the same `rows`/`grade` property names as S-L6, and read the row-id property name from the `UatRowResult` serialization before you assert the order `version`, `auth-login`, `nodes-list`, `notifications-admin`, `gateway-down`.
      - `run.json` `secret_scan == "passed"`;
      - `run.json` `violations == 0`;
      - `invariants.json` `errors` and `violations` both empty.
      - Do **not** copy S-L6's skip-row assertion. `cli.toml`'s `version` row and its rows without Nodes legitimately skip invariant sources, and S-L6 keeps the anti-always-skip check.
    - the diagnostic text is the same as S-L6 (`overall` plus `rows` raw text, truncated to 3000 characters). Never include the token or `leaseId` in any assert message.
    - `finally`: `RemoveLabContainers(context)` as S-L6 does, then remove the lab network only when it is labelled. List it with `["network","ls","-q","--filter","label=" + LabDefaults.ContainerLabel + "=1","--filter","name=^" + LabDefaults.NetworkName + "$"]`, and run `["network","rm",<id>]` for each id. Then delete the temp home.
    - keep `NewToken()` as in S-L6: in memory, with `Array.Clear` on the bytes.
  - `src/Tools/Dhadgar.Lab/README.md`, after the `enroll-probe.toml` paragraph (line 345), add a paragraph covering four points:
    - the shipped spec `docs/guides/uat-specs/cli.toml` (section `cli`, `requires_pool` Linux container) runs live through `CliSpecE2ETests`: `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~CliSpecE2ETests"`;
    - the test starts the daemon in process with a token it generates in memory, so it needs no running daemon and no `MERIDIAN_LAB_TOKEN`;
    - it builds the tip with `lab_build` first;
    - it needs Docker on `desktop-linux` and `MERIDIAN_LAB_S6_WIPE=1` when labelled lab containers exist.
  - Check `docs/meta/doc-update-map.md` for a `src/Tools/Dhadgar.Lab` or `tests/Dhadgar.Lab.Tests` entry, and do what it asks.
- **Red on revert:** this packet is the check itself, so no non-Docker test can go red for it. Its proof is section 7, run 2. The non-Docker checks prove only that the test compiles and that the Docker filter excludes it.
- **Checks:**
  1. Build.
  2. Run the full non-Docker suite once. `CliSpecE2ETests` must not run under it; confirm from the test count or the `--list-tests` output with the filter.
  3. `git status`: only the test file, the README and any doc the map names.
- **Commit subject:** `test(lab): run the shipped cli.toml through an in-process daemon [L37f3]`

### Wave and shipping

| Unit | Packets | Merge into `campaign/meridian-lab` | Review |
| --- | --- | --- | --- |
| 1 | L35mf | first | packet-reviewer |
| 2 | L37f1, L37f2, L37f3 (one branch, three commits) | second, after unit 1 | packet-reviewer on the branch + security-advisor on the branch |

The two units can be coded in parallel because their files are disjoint. They merge in that order. Neither unit opens a PR to `main`; the campaign's single PR stays as D-ML131 says.

## 6. Who does what for the live runs (B)

| Party | Provides or does |
| --- | --- |
| User | Docker Desktop running with context `desktop-linux`. A **new yes for each Docker run** in section 7. A **separate yes** to set `MERIDIAN_LAB_S6_WIPE=1`, only if labelled `meridian-lab-` containers exist at the pre-check. Nothing else: no daemon start, no token, no MCP tools. |
| Agent (ops or a packet-coder dispatched for the run, with ordinary PowerShell) | Pre-check: `docker --context desktop-linux info`, then list labelled containers (`ps -a --filter label=meridian-lab=1 --filter name=^meridian-lab-`) and labelled networks. Build the test project, as in L35mf. Run the one named test filter. Write the raw report (pass/fail counts, failure text and top frames, teardown state). Post-check that no labelled containers or networks are left. Never set, echo or store a lab token. The test makes its own token in memory and never prints the lease id. |

The fallback, a user-run `meridian-lab daemon run` with `MERIDIAN_LAB_TOKEN` set in their own terminal, is not needed and is not recommended. It cannot build the tip without an MCP client calling `lab_build` (F144).

## 7. Docker runs after the fixes (each needs a fresh user yes)

Run them serially, never beside another suite. Every red comes back to the architect with the raw report.

| Run | When | Command (`dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter ...`) | Pass means | Red routing |
| --- | --- | --- | --- | --- |
| 1 | after unit 1 merges | `"FullyQualifiedName~LabDbDockerTests"` | 4/4; closes KG-W7-1 (D-ML161 as amended by D-ML169) and half of KG-W7-9 | the cast again: architect. A mask assertion (UNION, CTE, identity): architect (l35m ruling 1 section 5) |
| 2 | after unit 2 merges (tip has both) | `"FullyQualifiedName~CliSpecE2ETests"` | all five rows pass; closes KG-W7-10, the L37 merge gate (D-ML174) and the live half of D-ML163 | the l37 ruling 1 section 5 cases (`401`, `400`/`404`, 5xx regex), plus `pool:` skips, `No such container`, or a missing `/opt/lab/cli/dhadgar` (F145 to F147 regressions): architect |
| 3 | same session, after run 2 | `"FullyQualifiedName~LabSelfTestE2ETests"` (S-L6) | green; closes the rest of KG-W7-9. F143 predicts it red on any tip without L35mf | an invariant error or skip: architect |
| 4 (optional) | after run 2 (the member image exists) | `"FullyQualifiedName~LinuxPoolDockerTests"` | green | architect |

`AuthStackDockerTests` (KG-W7-6) stays on the wave 7 Docker run (D-ML74). F143 also blocked its names step, so it can pass only on a tip with L35mf.

## 8. Ledger edits for the orchestrator

| Where | Edit |
| --- | --- |
| README findings | Add F143 to F147 (section 2, one line each, linking this note). |
| README decisions | Add D-ML168 to D-ML174 (section 3). Amend D-ML161: "closes on `LabDbDockerTests` 4/4 on a tip with L35mf (D-ML169)". |
| README known gaps | Add KG-W7-9 and KG-W7-10 (section 4). KG-W7-1 stays open. |
| README packet table | Add rows: L35mf (unit 1, `ml-l35m-fix`, `ml/l35m-fix-oid`, Ready) and L37f1 to L37f3 (unit 2, `ml-l37-live`, `ml/l37-live`, Ready, security-review). L35m row: "merged; Docker gate red in run 15 (F143); fix L35mf". L37 row: "merged; live gate is `CliSpecE2ETests` (D-ML174), owed after L37f". |
| work-packets.md | Under L35m and L37, add one line each: "Amended by [docker-run-15 ruling](worknotes/docker-run-15-ruling.md)". The packet specs live in this note, section 5. |
| RESUME.md | Tip 2efc332f. Next free F148, D-ML175, KG-W5-24, KG-W7-11. Next action 1 becomes: ops creates both worktrees, then dispatch L35mf beside L37f1 → L37f2 → L37f3, then the reviews, the merges in D-ML174 order, and the four Docker runs in section 7, each after a new user yes. Add `ml-l35m-fix` and `ml-l37-live` to cleanup after merge. Add a watch-out: "S-L6 is predicted red until L35mf (F143)". |
| docs/PROJECT-STATE.md (if it lists the lab's db_query, the CLI spec or S-L6) | Say "code exists", not "confirmed working", until runs 1 to 3 pass. |
