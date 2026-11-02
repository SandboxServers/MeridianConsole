# Nodes EF retry after a Postgres blip: packets NR-1 and L19n

Architect, 2026-11-01. Owner decision D-ML128 (option A): a separate product PR that adds EF connection retry to `Dhadgar.Nodes`. Plan ruling: D-ML132. Inputs: F114 ([run 9 ruling](wave5-run9-ruling.md) section 3), `origin/main` at `37560f0d`, and the lab tip `4fe0b32a`.

## 1. What is wrong, and what this changes

After a Postgres cut that Nodes did not see (its pool was idle), Nodes' pooled connections are dead. The first request that uses one gets `NpgsqlException` "Exception while reading from stream" (`EndOfStreamException`), EF logs a "transient failure", and the request ends in "Unhandled exception (fallback handler)" with a 500. Run 9 recorded this at 21:44:03.19 in `logs/nodes.jsonl`. The cause is that `UseNpgsql` has no retry (`src/Dhadgar.Nodes/Program.cs`, the `AddDbContext<NodesDbContext>` call). This is not KI-F67, which covers requests made *during* a cut.

NR-1 makes three changes:

1. A retrying execution strategy, with 3 retries and a delay of at most 1 s. On a client-side connection failure, it also clears the Npgsql pool, so the retry opens a fresh connection instead of taking the next dead one.
2. The one user-initiated transaction (`CapacityReservationService.ReserveAsync`, `BeginTransactionAsync(IsolationLevel.Serializable)`) runs inside `CreateExecutionStrategy().ExecuteAsync`. With a retrying strategy, EF refuses a user transaction outside the strategy (`InvalidOperationException`: "does not support user-initiated transactions"). A grep of `origin/main` finds no other `BeginTransaction`, `TransactionScope` or `CreateExecutionStrategy` in `src/Dhadgar.Nodes`.
3. A retried reservation attempt builds the same reservation and publishes the same event. Nodes has no `UseBusOutbox()`, so `Publish` goes straight to the broker and is not part of the transaction (F117, D-ML133). The ids are therefore fixed once, before the strategy runs.

Out of scope: D-ML120 (Nodes exits when the database is unreachable at startup). NR-1 does not change that behaviour, but `MigrateAsync` (Development only) now runs under the strategy, so a startup failure arrives after the retry budget instead of at once. That is at most 3 extra connect timeouts plus about 3 s of delay. F117's `UseBusOutbox()` is also out of scope (D-ML133).

## 2. Shape and order

| Packet | Branch / worktree | Base | Ships as | Security |
| --- | --- | --- | --- | --- |
| NR-1 | `fix/nodes-ef-retry` / `nodes-ef-retry` | `origin/main` (**not** `campaign/meridian-lab`) | its own PR to `main`, separate from the lab PR | security-advisor pass (section 3) |
| L19n | `ml/l19n` / `ml-l19n` | `campaign/meridian-lab` | local merge into the campaign; ships in the one campaign PR (D-ML131) | no (test only) |

Order:

1. **NR-1 code and review can start now.** It needs no Docker, and it does not touch the lab or the boundary.
2. **L19n runs after the boundary confirmation run (D-ML130)**, on a fresh daemon, so it shares no daemon with boundary runs (D-ML125's hard stop: L19n is a new seam test, not a fix wave). It writes S-L15 and records it **red** on the campaign tip.
3. **NR-1 seam check (section 4.6).** Before NR-1 is reported ready to merge, S-L15 runs **green** in a scratch worktree: the campaign tip with NR-1's `src/Dhadgar.Nodes` diff applied. Not done: NR-1 merged (#256) first; L19n's tip green replaces it (D-ML144).
4. **The owner merges NR-1** (D-41: only the owner merges, one yes per PR). If another Nodes `Program.cs` PR lands on `main` first, ops merges `main` into `fix/nodes-ef-retry` (merge, never rebase, D-38).
5. The next `origin/main` merge into `campaign/meridian-lab` brings NR-1 in. The campaign review (D-ML74) reruns S-L15, and it must be green. Until then, S-L15 red is the conditional gap KG-W5-17.

## 3. Security-review list check (CLAUDE.md)

NR-1 touches none of these: `IControlPlaneClient`, `CommandEnvelope`, `WindowsProcessManager` or `LinuxProcessManager`, or the agent projects (Core, Windows, Linux). The `security-review` label is therefore **not required**.

D-ML132 still asks for **one `security-advisor` pass**, beside the packet-reviewer, because the retry changes the enrollment write path. The advisor checks one thing: a one-time enrollment token stays single-use under a retried `SaveChanges`. `EnrollmentService.EnrollAsync` saves the node, certificate and the token's `UsedAt` in one `SaveChangesAsync`, and `UsedAt` is a concurrency token (`EnrollmentTokenConfiguration.cs`, D-S246). Suppose a commit succeeds but its acknowledgement is lost. The retry then hits either the concurrency check (the existing `catch (DbUpdateConcurrencyException)` path, a 401) or a duplicate-key error on the node insert (a 500). Neither path enrolls twice. The advisor confirms this by reading the code. No test is required for the lost-ack case.

L19n is test-only lab code, so it gets no security pass.

## 4. NR-1: retrying strategy, reservation inside the strategy

- **Commit subject:** `fix(nodes): retry transient database failures and reconnect after a Postgres blip`
- **Size:** small, about 30k tokens.
- **Files:** `src/Dhadgar.Nodes/Data/NodesRetryingExecutionStrategy.cs` (new), `src/Dhadgar.Nodes/Data/NodesDatabase.cs` (new), `src/Dhadgar.Nodes/Program.cs`, `src/Dhadgar.Nodes/Services/CapacityReservationService.cs`, `src/Dhadgar.Nodes/README.md`, `tests/Dhadgar.Nodes.Tests/NodesWebApplicationFactory.cs`, `tests/Dhadgar.Nodes.Tests/CapacityReservationServiceTests.cs`, `tests/Dhadgar.Nodes.Tests/Data/NodesDatabaseTests.cs` (new).
- **Contract:** none. No `Dhadgar.Contracts` type, route, status code or error shape changes. The only cross-side behaviour is Nodes and Postgres, and S-L15 covers it (section 5).

### Rules (packet-coder does not read CLAUDE.md)

- R-CPM: no package versions in any `.csproj`, and no new `PackageReference`. `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 is already referenced by Nodes, and `Microsoft.EntityFrameworkCore.Sqlite` by Nodes.Tests. If a `packages.lock.json` changes, stop and report it; do not commit lock churn.
- R-RESTORE: restore without a RID. Never run `publish -r` or `restore -r`, which dirty the lock files.
- R-USING: `using` / `await using` on every `IDisposable` (`SqliteConnection`, the contexts, the factory, the transaction).
- R-RESULT: keep `ServiceResult` returns for validation outcomes, as today. Do not add exceptions for validation.
- Nullable is on, and CI builds with warnings as errors.
- Tests use xUnit `[Fact]`, with `Method_Case_Result` names, and `FakeTimeProvider` as the target class already does. Tests that use `NodesWebApplicationFactory` join `[Collection("Nodes Integration")]`.
- Branch from `origin/main`. Never rebase. Never `git worktree prune`.

### Steps

1. **`NodesRetryingExecutionStrategy`** (namespace `Dhadgar.Nodes.Data`). It is public, and it derives from `Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy`. That type is not sealed, and `OnRetry` is protected virtual on `ExecutionStrategy` (checked on 10.0.3).

   ```csharp
   public sealed class NodesRetryingExecutionStrategy : NpgsqlRetryingExecutionStrategy
   {
       public const int MaxRetries = 3;
       public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);
       private bool _clearPoolBeforeRetry;

       public NodesRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
           : base(dependencies, MaxRetries, MaxDelay, errorCodesToAdd: null) { }

       /// <summary>A client-side connection failure (I/O, socket, timeout), not a server error code.</summary>
       public static bool IsConnectionFailure(Exception exception) =>
           exception is NpgsqlException { IsTransient: true } and not PostgresException;

       protected override bool ShouldRetryOn(Exception exception)
       {
           bool retry = base.ShouldRetryOn(exception);
           if (retry && IsConnectionFailure(exception)) { _clearPoolBeforeRetry = true; }
           return retry;
       }

       protected override void OnRetry()
       {
           if (_clearPoolBeforeRetry
               && Dependencies.CurrentContext.Context.Database.GetDbConnection() is NpgsqlConnection connection)
           {
               // Every idle pooled connection may be dead after a blip; the retry must not take the next one.
               NpgsqlConnection.ClearPool(connection);
           }
           _clearPoolBeforeRetry = false;
           base.OnRetry();
       }
   }
   ```

   Server-side transient errors, such as `40001` serialization failure or `40P01` deadlock, are retried but do not clear the pool.

2. **`NodesDatabase`** (static, namespace `Dhadgar.Nodes.Data`):

   ```csharp
   public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
       options.UseNpgsql(connectionString, npgsql =>
           npgsql.ExecutionStrategy(dependencies => new NodesRetryingExecutionStrategy(dependencies)));
   ```

   In `Program.cs`, change the `AddDbContext<NodesDbContext>` call to `options => NodesDatabase.Configure(options, builder.Configuration.GetConnectionString("Postgres"))`. Change nothing else in `Program.cs`.

3. **`CapacityReservationService.ReserveAsync`.** Keep the signature. Fix the ids and the time before the strategy, so that every attempt builds the same reservation and publishes the same `CapacityReserved`:

   ```csharp
   var reservationId = Guid.NewGuid();
   var reservationToken = Guid.NewGuid();
   var now = _timeProvider.GetUtcNow().UtcDateTime;
   var attempt = 0;
   var strategy = _dbContext.Database.CreateExecutionStrategy();
   return await strategy.ExecuteAsync(async cancellationToken =>
   {
       if (attempt++ > 0)
       {
           // A failed attempt left its reservation (and the node it read) tracked; start the retry clean.
           _dbContext.ChangeTracker.Clear();
       }

       await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
       // existing body: every `ct` becomes `cancellationToken`; the reservation uses reservationId, reservationToken and now
   }, ct);
   ```

   - Keep the early-return `RollbackAsync` calls on the validation paths; their connection is healthy.
   - **Delete** the outer `catch (Exception) { await transaction.RollbackAsync(ct); throw; }`. On a broken connection that rollback can throw a non-transient error that hides the transient one, so the strategy would not retry. `await using` already rolls back an uncommitted transaction on dispose.
   - Replace the comment above `Publish` ("MassTransit's outbox stores the message in the same transaction…"). Nodes has no `UseBusOutbox()`, so the comment is false. Use one line: `// Not transactional: Nodes has no UseBusOutbox (F117); fixed ids make a retried publish identical.`

4. **`NodesWebApplicationFactory`.** Add `public bool UseProductionDbContext { get; init; }`. When it is set, skip the `RemoveAll<DbContextOptions<NodesDbContext>>()`, the InMemory `AddDbContext` and the `DbInitStartupFilter` registration. Change nothing else. The default stays `false`, so every existing test is unchanged.

5. **Contingency.** EF 10 keeps the production `UseNpgsql` configuration action after `RemoveAll<DbContextOptions<…>>()`. If the full Nodes suite shows "does not support user-initiated transactions" in any InMemory test, also remove the leaked configuration in the default branch with `services.RemoveAll<IDbContextOptionsConfiguration<NodesDbContext>>()` (namespace `Microsoft.EntityFrameworkCore.Infrastructure`), before the InMemory `AddDbContext`. If the suite is green without it, do not add it.

6. **Seam check before ready-to-merge (after L19n's red record exists).**
   1. ops creates a scratch worktree `ml-nr1-seam` on a new local branch `ml/nr1-seam-scratch`, from the `campaign/meridian-lab` tip.
   2. A packet-coder applies only the Nodes source diff: `git diff origin/main...fix/nodes-ef-retry -- src/Dhadgar.Nodes | git apply --3way`. If it does not apply cleanly, return `blocked spec`; the green check then moves to step 5 of section 2.
   3. The packet-coder runs S-L15 and S-L7 on a fresh daemon, commits locally on the scratch branch (never pushed or merged), and copies the result into `worknotes/nr-1-seam.md`.
   4. ops removes the worktree and deletes the scratch branch.

### Tests (each fails when its step is reverted)

- `NodesDatabaseTests.Configure_UsesNodesRetryingStrategy` (unit). Build `DbContextOptionsBuilder<NodesDbContext>` with `NodesDatabase.Configure(builder, "Host=127.0.0.1;Port=1;Database=nr1;Username=nr1;Password=nr1")`. Create a `NodesDbContext`. Assert that `Database.CreateExecutionStrategy()` is `NodesRetryingExecutionStrategy` and that `RetriesOnFailure` is true. No connection is opened. Reverting step 2 gives `NonRetryingExecutionStrategy`.
- `NodesDatabaseTests.NodesHost_DbContext_RetriesOnFailure` (`[Collection("Nodes Integration")]`). Use `new NodesWebApplicationFactory { UseProductionDbContext = true }`, with `Settings["ConnectionStrings:Postgres"]` set to the string above. Resolve `NodesDbContext` from a scope and assert the same two facts. This is the Program wiring test: reverting the `Program.cs` line fails it even when `NodesDatabase` is kept. Accessing `factory.Services` builds the host but opens no database connection, because the `Testing` environment skips `AutoMigrateDatabaseAsync` and the factory removes the hosted services.
- `NodesDatabaseTests.IsConnectionFailure_IoFailure_True_ServerError_False`. Assert that `new NpgsqlException("x", new EndOfStreamException())` is true, and that `new PostgresException("serialization", "ERROR", "ERROR", "40001")` and `new InvalidOperationException()` are false. Reverting the predicate to `base.ShouldRetryOn` alone, or deleting it, fails this test.
- `CapacityReservationServiceTests.ReserveAsync_TransientFailureOnInsert_RetriesOnceAndCommitsOneReservation`. Use SQLite in memory on a shared open `SqliteConnection`, as `ConcurrentReservations_PreventOverProvisioning` does, with `UseSqlite(connection, o => o.ExecutionStrategy(d => new TestRetryingStrategy(d)))` and `.AddInterceptors(new FailFirstInsertInterceptor())`. Both are new private nested test types:
  - `TestRetryingStrategy : ExecutionStrategy` with `(d, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)`, where `ShouldRetryOn(e) => e is TransientTestException`.
  - `FailFirstInsertInterceptor : DbCommandInterceptor` throws `TransientTestException` once from `ReaderExecutingAsync` and `NonQueryExecutingAsync`, when `CommandText` starts with `INSERT INTO "capacity_reservations"` (ordinal). It counts its throws.

  Seed with `SeedNodeWithCapacityAsync`. Assert that `ReserveAsync` succeeds, the interceptor threw once, the table holds exactly one row, and every `CapacityReserved` in `publisher.GetMessages<CapacityReserved>()` carries that row's `ReservationToken`. Each revert fails it:
  - Without step 3's `ExecuteAsync` wrap, EF throws "does not support user-initiated transactions".
  - Without `ChangeTracker.Clear()`, EF throws a duplicate tracked key.
  - With the ids generated inside the lambda, the two published tokens differ.
- The existing `ConcurrentReservations_PreventOverProvisioning` and every other reservation test stay green, unchanged.

### Checks, in order

```powershell
dotnet build src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release --no-build
dotnet build tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release --no-build
```

`Dhadgar.AgentSeam.Tests` hosts Nodes in-process, so its seams must stay green. None of these checks need Docker.

### Docs owed

- `src/Dhadgar.Nodes/README.md`: add a short "Database resilience" subsection under "Database Schema". Cover three points: the retry policy (3 retries, at most 1 s delay, the pool cleared on a client-side connection failure); the rule that any new user-initiated transaction must run inside `CreateExecutionStrategy().ExecuteAsync`; and the fact that `Publish` is not transactional until D-ML133 is ruled.
- Check `docs/meta/doc-update-map.md` for a Nodes database row. Today it has none for this change.
- PR body: link F114, D-ML128 and this note. Cite the L19n red record and the section 4.6 green record. Name the security-advisor report.

## 5. L19n: seam S-L15 (Nodes pool ↔ Postgres cut through the lab)

- **Commit subject:** `test(lab): S-L15 first enroll after an idle Postgres cut`
- **Size:** small, about 25k tokens. Docker must be running, on a fresh daemon that no other run is using.
- **Files:** `tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs` only.
- **Why here:** the seam is Nodes' connection pool against a Postgres connection that the lab's Toxiproxy `nodes-postgres` proxy cuts and heals. It is the same fault that the hunt's `PgDown` sends (`fault_proxy_disable`). `main` has no lab and no Toxiproxy, so the test that runs both sides lives here. S-L7 cannot serve: it polls for 401 after the heal (`WaitUntilAsync … lastHeal == 401`), which hides a single 500 on the first request.

### Rules

- `[Collection(LabDockerGate.Name)]`, `[Trait("RequiresDocker", "true")]`, `[Trait("Seam", "S-L15")]`.
- Observe on direct ports only (R-OBSERVE). Assert `LabHosts.IsLabEndpoint` before every probe, using the existing `ProbeAsync`.
- Use the product DTO for the body (`UnknownTokenEnrollBody`, D-ML7), never a local copy.
- Teardown in `finally` is label-only (`meridian-lab-*`): `services.StopAsync("nodes")`, `stack.DownAsync(wipe: true)`, then delete the home directory.
- `using` on every `IDisposable`. Return `Result` from lab code paths, as the existing test does.
- Never run a bare `publish -r`. Restore RID-less.

### Steps

1. Extract the object graph that the S-L7 test builds (from `FindRepoRoot()` to the `HttpClient`, and its `home` folder) into a private nested `sealed class SeamRig : IDisposable`. It exposes `Home`, `Stack`, `Services`, `Toxi`, `Http` and `RepoRoot`, and is built by `private static SeamRig CreateRig(string prefix)`. Change S-L7 only to use it. Its assertions and order stay byte-for-byte the same.
2. Add `[Fact] PostgresCutWhileIdle_FirstEnrollAfterHeal_Answers401`:
   1. `stack.UpAsync(wipe: true)`. Build `nodes` only with `LabBuilder.BuildAsync`, then `services.StartAsync("nodes", buildId)`.
   2. Baseline: `WaitUntilAsync` until an unknown-token enroll on `NodesEnrollUri()` answers 401 (60 s window, 5 s poll).
   3. Warm the pool: 4 probes in parallel (`Task.WhenAll`), each 401. Then wait 1 s with no request.
   4. `toxi.SetEnabledAsync("nodes-postgres", false)`. Assert that `CanConnectAsync(ProxyPort("nodes-postgres"))` is false. Wait 3 s, and send **no** Nodes request during the cut.
   5. Superseded by D-ML149: the listener is probed in-network with `ProxyListenerProbe` (L19o). `toxi.SetEnabledAsync("nodes-postgres", true)`. `WaitUntilAsync(CanConnectAsync(...))` (10 s window, 1 s poll). This probes the proxy listener, never Nodes.
   6. Send **one** probe and assert `Status == 401`, with the message `$"First enroll after an idle Postgres cut answered {status}, not 401 (F114 product half, D-ML128)."`
   7. Send 5 more sequential probes, each 401. Write the timings with `_output.WriteLine("S-L15 timing …")`.
3. Update the class `<summary>` to name S-L15.

### Expected results

- Superseded by D-ML144 ([l19n ruling 1](l19n-ruling-1.md) section 4): the red is taken on a scratch branch with `src/Dhadgar.Nodes` at `37560f0d`, and the hard stop applies to that run only. **Red** on the campaign tip at dispatch: step 2.6 gets 500, as in run 9. Record the status, and the Nodes log lines around it from the container log follower (EF "transient failure", then "Unhandled exception (fallback handler)"), in `worknotes/l19n.md`. **Hard stop:** if the first probe answers 401 before NR-1, the seam does not reproduce F114. Report `blocked spec` with the timings. Do not tune the waits to force a red.
- **Green** in NR-1's scratch check (section 4.6), and on the campaign after `main` (with NR-1) is merged in.
- S-L7 still passes after the step 1 extraction. Run both Facts.

### Checks, in order

```powershell
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ToxiproxySeamTests"
```

The last check needs Docker. S-L15 is expected red on this branch until NR-1 is merged in, so the commit message says "S-L15 red as expected (F114)". The red is not a failed packet.

### Docs owed

None beyond the ledger. The README seam row S-L15 and KG-W5-17 already exist; the architect updates their status from the run records.
