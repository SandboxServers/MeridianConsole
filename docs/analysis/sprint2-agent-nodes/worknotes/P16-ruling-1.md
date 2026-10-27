# P16 ruling 1: pre-existing MassTransit outbox drift blocks the GraceUntil migration

Input: `P16-coder.md` (blocked, `spec`), worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p16`, branch `s2/p16-renewal-grace`, base w1 `fbed046`. Ruling date 2026-10-27.

## Ruling

**Re-spec P16 step 3 into two migrations and two commits on the same branch, in the same worktree. Keep the coder's uncommitted work.** First a drift-fix migration that creates only the MassTransit tables, with a guard test. Then the `GraceUntil` migration on a clean model. This is not an owner decision.

## Findings

| # | Finding | Evidence |
| --- | --- | --- |
| F1 | The drift predates the campaign. `AddInboxStateEntity`, `AddOutboxStateEntity` and `AddOutboxMessageEntity` were added to the model in January without a migration. | `NodesDbContext.cs:39-41`; added in `d29add5` ("add transactional outbox"), which comes after the last generated migration `20260123085708_AddAuditLogging` (`d76ebe3`). The snapshot at `fbed046` has no `OutboxState`. |
| F2 | The service depends on those tables. | `Program.cs:113-123`: `AddEntityFrameworkOutbox<NodesDbContext>(o => o.UsePostgres() ...)`. On a migrated database, the outbox delivery service polls tables that do not exist. |
| F3 | Development auto-migrate cannot succeed today. Since EF Core 9, `Migrate` throws on `PendingModelChangesWarning` by default, and Nodes does not suppress it (only Identity does, `Identity/Program.cs:101`). | `DatabaseMigrationExtensions.AutoMigrateDatabaseAsync` calls `MigrateAsync` and rethrows. Inferred from the EF Core 9 breaking change; not run here. |
| F4 | Tests never caught it, because every Nodes test builds its schema with `EnsureCreated`, which reads the model rather than the migrations. | `NodesWebApplicationFactory.cs:157,198,225,235` and the integration tests. |
| F5 | P09's hand-written `xmin` change is consistent with the snapshot: the generated diff did not touch `agent_certificates.xmin`. Only the three MassTransit tables and `GraceUntil` appeared. | Parked file `...\scratchpad\20261027084255_AddAgentCertificateGraceUntil.cs`. |
| F6 | EF generated the ID `20261027084255`, which sorts **before** P09's `20261027120000_AddAgentCertificateRowVersion`. | `P16-coder.md` ordering note. |

## Options considered

| Option | Verdict |
| --- | --- |
| A. Hand-write a migration with only `GraceUntil` and leave the snapshot without the MassTransit tables | Rejected. The drift stays, so the packet's `has-pending-model-changes` check still fails, Development auto-migrate still throws, and the next person to run `migrations add` hits the same problem. |
| B. Accept both in one migration | Rejected. It mixes an infrastructure fix into a security-reviewed certificate change, and rolling back `GraceUntil` would also drop the outbox tables. |
| C. A separate fix packet, with its own worktree and merge, before P16 | Correct but costly: one more worktree, merge and rebase for about 3 generated files. |
| **D. Two commits in P16: the drift migration first, then `GraceUntil`** | **Chosen.** Each migration does one thing. The first commit can be cherry-picked or reviewed on its own, and adds the guard test that would have caught F1. |

Migration IDs are fixed so that they sort after P09: `20261027130000_AddMassTransitOutboxEntities` and `20261027130100_AddAgentCertificateGraceUntil`. This overrides the packet's "keep the generated name".

## Steps for packet-coder (same worktree, keep its uncommitted state)

`<wt>` = `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p16`. Run all commands from `<wt>`. Rules for this work: R-NULL (build with `-p:TreatWarningsAsErrors=true`), R-CPM (no `Version=` in `.csproj`; no new packages), R-USING (`using var` on the `DbContext`), R-XUNIT (`[Fact]`, `Assert`, a sealed test class in the folder-matching namespace). Do **not** copy the parked scratchpad migration files; regenerate.

1. **Check the start state.** `git -C <wt> status --short` must show exactly the 8 modified files and 2 untracked files listed in `P16-coder.md` ("Changes in the worktree"), with nothing under `src/Dhadgar.Nodes/Data/Migrations/`. `git -C <wt> rev-parse HEAD` must be `fbed046…`. If either check fails, stop with `blocked spec`.
2. **Set the P16 work aside.**
   ```powershell
   git -C <wt> stash push --include-untracked -m "P16 wip (P16-ruling-1)" -- src/Dhadgar.Nodes src/Shared/Dhadgar.Contracts
   ```
   `git status --short` must now be empty for `src/`.
3. **Generate the drift migration** on the base model:
   ```powershell
   dotnet ef migrations add AddMassTransitOutboxEntities --project src\Dhadgar.Nodes\Dhadgar.Nodes.csproj --startup-project src\Dhadgar.Nodes\Dhadgar.Nodes.csproj --output-dir Data\Migrations
   ```
   `Up` must contain only: `CreateTable` for `InboxState`, `OutboxState` and `OutboxMessage` (with the `AK_InboxState_MessageId_ConsumerId` key and the 2 `OutboxMessage` FKs), and the 6 indexes `IX_InboxState_Delivered`, `IX_OutboxMessage_EnqueueTime`, `IX_OutboxMessage_ExpirationTime`, `IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber`, `IX_OutboxMessage_OutboxId_SequenceNumber` and `IX_OutboxState_Created`. `Down` must drop the 3 tables. If any operation touches another table (for example `agent_certificates`, `nodes` or `xmin`), stop with `blocked spec` and paste the operation.
4. **Rename it so that it sorts after P09.** Rename `<generated-id>_AddMassTransitOutboxEntities.cs` and `.Designer.cs` to `20261027130000_AddMassTransitOutboxEntities.cs` and `.Designer.cs`. In the Designer, change the attribute to `[Migration("20261027130000_AddMassTransitOutboxEntities")]`. In the migration class, replace `/// <inheritdoc />` above the class with:
   ```csharp
   /// <summary>
   /// Creates the MassTransit EF outbox and inbox tables. NodesDbContext has registered them since d29add5,
   /// but no migration created them (P16-ruling-1).
   /// </summary>
   ```
   Keep the snapshot change as generated.
5. **Add a guard test:** new file `tests/Dhadgar.Nodes.Tests/Data/NodesModelSnapshotTests.cs`.
   ```csharp
   using Dhadgar.Nodes.Data;
   using Microsoft.EntityFrameworkCore;

   namespace Dhadgar.Nodes.Tests.Data;

   public sealed class NodesModelSnapshotTests
   {
       // Tests build their schema with EnsureCreated, so nothing else notices a model change
       // that has no migration (P16-ruling-1). No database connection is opened.
       [Fact]
       public void Model_HasNoPendingChanges_AgainstMigrations()
       {
           using var db = new NodesDbContextFactory().CreateDbContext([]);

           Assert.False(
               db.Database.HasPendingModelChanges(),
               "NodesDbContext has model changes with no migration; run 'dotnet ef migrations add'.");
       }
   }
   ```
   Revert check: with step 3's migration removed (snapshot restored to `fbed046`), this test fails. Run that check once and restore.
6. **Run the checks for commit 1**, in order:
   ```powershell
   dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet ef migrations has-pending-model-changes --project <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj
   dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~NodesModelSnapshotTests"
   ```
   The second command must report no pending changes.
7. **Commit 1** (explicit paths: the 2 migration files, `NodesDbContextModelSnapshot.cs` and the test file). Subject: `fix(nodes): migrate the MassTransit outbox and inbox tables (P16-ruling-1)`. Body: "NodesDbContext has registered the outbox entities since d29add5 with no migration, so Migrate failed with pending model changes and the outbox polled missing tables. Adds the migration and a guard test."
8. **Restore the P16 work:** `git -C <wt> stash pop`. It must apply cleanly, because the stash holds no migration files. On a conflict, stop with `blocked spec` and leave the stash in place.
9. **Run P16 step 3 again, with this change:** generate `AddAgentCertificateGraceUntil` with the same command. `Up` must be exactly one nullable `timestamp with time zone` column `GraceUntil` on `agent_certificates`, and `Down` must drop it. Anything else means `blocked spec`. Rename to `20261027130100_AddAgentCertificateGraceUntil.cs` and `.Designer.cs`, and set the Designer attribute to `[Migration("20261027130100_AddAgentCertificateGraceUntil")]`. Keep the snapshot as generated.
10. **Do not redo** P16 change steps 1, 2 and 4 to 8 (already in the worktree per `P16-coder.md`). Check that each one is present, then re-run step 8a's grep and paste its output.
11. **Finish P16 as specified:** all of the packet's **Tests**, its **Checks (in order)** (the `has-pending-model-changes` line must now pass), and its **Docs owed**. In the Nodes README's database or migrations notes, also list `20261027130000_AddMassTransitOutboxEntities` (the MassTransit outbox and inbox tables). `NodesModelSnapshotTests` runs in the non-S3 test command.
12. **Commit 2** with the packet's subject, `feat(nodes): short revocation grace for the renewed agent certificate (D-S212)`. The branch then holds 2 commits on `fbed046`. Report both SHAs.

## Review notes for the orchestrator

- packet-reviewer covers both commits. Commit 1 is not a security-review area; security-advisor reviews commit 2 only, as P16 already requires.
- Out of campaign scope, for a follow-up issue: `src/Dhadgar.Notifications/Program.cs` configures `AddEntityFrameworkOutbox(... UseBusOutbox())`, but `NotificationsDbContext` registers no outbox entities and its snapshot has no outbox tables. Its runtime impact is unverified.

## Owner decisions

None.
