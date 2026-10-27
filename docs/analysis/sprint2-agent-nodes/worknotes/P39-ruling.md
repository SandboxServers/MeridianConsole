# P39 ruling: campaign security review pass 1, notes N1 and N2 (D-S246, D-S247)

Architect ruling, 2026-10-27. Input: `campaign-security-1` (a local report in the campaign worktree, not committed) (verdict `fix`, notes N1 and N2) and the fix coder's `blocked spec` report `P39-fix-1.md` (worktree `sprint2-agent-nodes-p39`, base `c0a641e`, nothing committed). This note makes decisions **D-S246** and **D-S247**, records findings **F30** and **F31**, and re-specifies fix packet **P39** so that a fresh packet-coder can apply both notes in one pass. No owner decision blocks.

## 1. Rulings

| Note | Ruling | Decision |
| --- | --- | --- |
| N1 | **Apply now, by a different mechanism than the note's preferred one.** Make `EnrollmentToken.UsedAt` an EF concurrency token, catch `DbUpdateConcurrencyException` around enrollment's one `SaveChangesAsync`, clear the change tracker, audit and return `invalid_token`. No `ExecuteUpdateAsync`, no migration, no Testcontainers. The race test is deterministic and runs on SQLite in memory, which Nodes.Tests already uses. | D-S246 |
| N2 | **Apply now, as the note says.** Compare `NotBefore` and `NotAfter` as `DateTimeOffset` instants. | D-S247 |
| Pass-1 residuals (events before save, `AgentHub` missing-row, `Hardware` control characters, unzeroable token string) | **Not in P39.** They stay on the P15 follow-up and owner lists (§ 6). | - |

### Why not the note's `ExecuteUpdateAsync`

- The EF InMemory provider does not support `ExecuteUpdateAsync`. Enrollment's success path runs on InMemory in `EnrollmentServiceTests` (13 tests) and in every host-level seam that enrolls: `NodesWebApplicationFactory` (`EnrollmentApiIntegrationTests`, `AgentEnrollmentContractTests`, `AgentSeamHarness`), `Dhadgar.AgentSeam.Tests` (`GatewayNodesHarness`, S2 and S4) and the UAT `StackScenario`. Moving them all to a relational provider is a campaign of its own, and it would put the seam tests at risk just before the PR merges.
- It claims the token before the certificate is issued. A later `invalid_public_key` or CA failure then burns a one-time token unless a compensating release is added. The concurrency token keeps today's behaviour: a failed enrollment leaves the token usable.

### Why the concurrency token is sound

- With `UsedAt` as a concurrency token, EF writes the claim as `UPDATE enrollment_tokens SET "UsedAt" = @now, "UsedByNodeId" = @node WHERE "Id" = @id AND "UsedAt" IS NULL`. This is the conditional write the note asks for. It runs in the same implicit transaction as the `Node`, hardware, capacity and `AgentCertificate` inserts.
- On PostgreSQL (READ COMMITTED), the second writer blocks on the row lock. When the first commits, the second re-evaluates `"UsedAt" IS NULL`, matches 0 rows, and EF throws `DbUpdateConcurrencyException`. The loser's transaction rolls back, so no `Node`, no `AgentCertificate` row and no token change survive.
- The loser's CA did sign a certificate, but it is never returned or stored. Without a row it is only reachable by someone who holds the certificate, and nobody does. The InMemory provider also checks concurrency tokens, so the InMemory tests keep working.
- `NodesModelSnapshotTests` (`HasPendingModelChanges`) guards the model. A concurrency token on an existing column is not a schema change, so no migration is needed. The snapshot gets the matching `.IsConcurrencyToken()` line anyway, so that a later `migrations add` does not pick it up as noise.

### Why `ChangeTracker.Clear()` is part of the fix

`AuditService` saves through the **same scoped `NodesDbContext`** (`AuditService.cs:70-71`). Without the clear, the loser's `EnrollmentFailed` audit calls `SaveChangesAsync`, which re-sends the tracked `Node`, hardware, capacity, certificate and token update, throws the concurrency exception again, and `AuditService` rethrows it as a 500. The race test uses the real `AuditService` for this reason.

### Why no Testcontainers

`Directory.Packages.props` has no Testcontainers entry, and no test project in the repo uses it. The coder's report says `Dhadgar.Identity.Tests` does, but it does not. Docker runs on this machine, but CI Docker availability for a new PostgreSQL fixture is untested. Adding it now means a new package, a new fixture and a new CI dependency on the merge path. The SQLite test runs the real relational SQL (`WHERE "UsedAt" IS NULL`, row count 0, rollback) and fails when the fix is reverted. What it cannot show is PostgreSQL's lock-wait re-check, which is documented engine behaviour. A Nodes PostgreSQL integration fixture becomes follow-up **FU-P39-1** (§ 6).

### N2: the note's direction is reversed; the fix stands

`X509Certificate2.NotAfter` and `NotBefore` are `DateTimeKind.Local`, and `now` is UTC, so `DateTime` comparison compares wall-clock numbers. On a host at UTC+2, a certificate that expired at 12:00Z reads `NotAfter = 14:00` (local). At 13:00Z, `14:00 < 13:00` is false, so the expired certificate is **accepted** for up to the offset (fail-open). Hosts west of UTC reject valid certificates early and accept not-yet-valid ones. The note has the east/west direction swapped, but the defect and the fix are right. The claim path (`MtlsMiddleware.cs:149`, `new DateTimeOffset(notAfter)`) already converts correctly and does not change.

## 2. Decisions

| ID | Decision | Reason |
| --- | --- | --- |
| D-S246 | **The one-time enrollment token is consumed by a conditional write at enrollment's save.** `EnrollmentTokenConfiguration` marks `UsedAt` `.IsConcurrencyToken()`, so the token UPDATE carries `"UsedAt" IS NULL`. `EnrollmentService.EnrollAsync` catches `DbUpdateConcurrencyException` around its `SaveChangesAsync`, calls `ChangeTracker.Clear()` (the audit service shares the context), audits `EnrollmentFailed` / `invalid_token` and returns `invalid_token`. No migration (snapshot line only), no `ExecuteUpdateAsync`, no new test infrastructure. The race test is deterministic: SQLite in memory, two contexts, a gated CA and the real `AuditService`. | Fixes F30 without breaking the InMemory unit tests and seams, which cannot run `ExecuteUpdate`. A failed certificate issuance does not burn the token. |
| D-S247 | **Nodes compares certificate validity as instants.** `CertificateValidationService` compares `new DateTimeOffset(certificate.NotBefore/NotAfter)` with `_timeProvider.GetUtcNow()`. The tests pin the window at ±30 minutes. They are host-time-zone dependent by nature: on a UTC host they pass either way, and on this machine (Central, UTC-5/-6) the revert fails two of them. | Fixes F31. It matches the agent's `AgentCertificateIdentity.IsWithinValidity`. |

## 3. Fix packet P39 (re-specified)

- **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p39`. **Branch** `s2/p39-campaign-fix-1` (exists, at `c0a641e`, clean). Do not create another worktree.
- **Security review** yes (enrollment, certificate validation). The orchestrator runs campaign-security pass 2 on the result. Pass 2 also covers the areas pass 1 left open.
- **Commit subject** `fix(nodes): consume the enrollment token atomically and compare certificate validity as instants (P39, D-S246, D-S247)`
- **Files** (edit only these):
  1. `src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs`
  2. `src/Dhadgar.Nodes/Data/Migrations/NodesDbContextModelSnapshot.cs` (one line)
  3. `src/Dhadgar.Nodes/Services/EnrollmentService.cs`
  4. `src/Dhadgar.Nodes/Auth/CertificateValidationService.cs`
  5. `tests/Dhadgar.Nodes.Tests/EnrollmentTokenRaceTests.cs` (new)
  6. `tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs` (class `CertificateValidationServiceTests` only)
  7. `src/Dhadgar.Nodes/README.md` (one sentence)

### Repo rules for this packet

- Validation, IO and network failures return a failure result, never throw. Here that is `ServiceResult.Fail<LocalEnrollNodeResponse>("invalid_token")`, the existing shape.
- Never return `Success(null)`. Not touched here.
- Put `using` on every `IDisposable`: `SqliteConnection`, each `NodesDbContext`, each `X509Certificate2`, each `RSA`.
- No package versions in `.csproj`. This packet adds no package (`Microsoft.EntityFrameworkCore.Sqlite` and `NSubstitute` are already referenced by Nodes.Tests).
- Nullable is enabled and warnings are errors in CI. Build with `-c Release`.
- xUnit v2 conventions of Nodes.Tests: `[Fact]`, `Arrange/Act/Assert` comments, `FakeTimeProvider`, `NullLogger<T>.Instance`, sealed test classes.
- Do not add Testcontainers, a migration, `ExecuteUpdateAsync`/`ExecuteDeleteAsync` on enrollment, or a provider check (`IsRelational`, `IsInMemory`) in product code.

### Step 1: concurrency token (`EnrollmentTokenConfiguration.cs`)

After the `CreatedByUserId` property block and before the index comments, add:

```csharp
        // D-S246: the one-time claim. Enrollment's UPDATE carries WHERE "UsedAt" IS NULL (the value it read),
        // so of two requests that validated the same token, only the first to save consumes it.
        builder.Property(t => t.UsedAt)
            .IsConcurrencyToken();
```

### Step 2: snapshot (`NodesDbContextModelSnapshot.cs`)

In the `Dhadgar.Nodes.Data.Entities.EnrollmentToken` entity, change only the `UsedAt` property (around lines 188-189) to:

```csharp
                    b.Property<DateTime?>("UsedAt")
                        .IsConcurrencyToken()
                        .HasColumnType("timestamp with time zone");
```

Change nothing else in the snapshot. Do not touch any `*.Designer.cs`.

### Step 3: the conditional save (`EnrollmentService.cs`)

Replace the single line `await _dbContext.SaveChangesAsync(ct);` (line 221) with the block below. Change nothing else in `EnrollAsync`: the two `Publish` calls stay before it (see residual R-P39-2), and `MarkTokenUsed` stays.

```csharp
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // D-S246: another request consumed this one-time token after ValidateTokenAsync read it. The token
            // UPDATE carries "UsedAt" IS NULL, so this whole save rolled back: no Node, no AgentCertificate row,
            // and the certificate issued above is never returned. The audit service saves through this same
            // DbContext, so drop the rejected entities first or the audit would re-send them.
            _dbContext.ChangeTracker.Clear();

            _logger.LogWarning(
                "Enrollment failed: token {TokenId} was consumed by a concurrent enrollment",
                token.Id);
            NodesMetrics.RecordEnrollmentFailure(platform, "invalid_token");

            await _auditService.LogAsync(
                AuditActions.EnrollmentFailed,
                ResourceTypes.Node,
                null,
                AuditOutcome.Failure,
                new { Platform = platform, Hostname = request.Hardware.Hostname, TokenId = token.Id },
                organizationId: token.OrganizationId,
                failureReason: "invalid_token",
                ct: ct);

            return ServiceResult.Fail<LocalEnrollNodeResponse>("invalid_token");
        }
```

`Microsoft.EntityFrameworkCore` is already imported, and `DbUpdateConcurrencyException` lives there.

### Step 4: instants (`CertificateValidationService.cs`)

Replace lines 149-153 (from `var now = ...` to `var notYetValid = ...`) with:

```csharp
        var now = _timeProvider.GetUtcNow();

        // D-S247: X509Certificate2.NotBefore/NotAfter are local times (DateTimeKind.Local). Compare instants,
        // not wall-clock numbers, or the window shifts by the host's UTC offset (fail-open east of UTC).
        var isExpired = new DateTimeOffset(certificate.NotAfter) < now;
        var notYetValid = new DateTimeOffset(certificate.NotBefore) > now;
```

`now` is used only in the two log calls (lines 159 and 171), which accept a `DateTimeOffset`. Leave every `notAfter: certificate.NotAfter` argument and the result's `NotAfter` unchanged. `MtlsMiddleware` already converts the result through `new DateTimeOffset(notAfter)`.

### Step 5: race test (new `tests/Dhadgar.Nodes.Tests/EnrollmentTokenRaceTests.cs`)

```csharp
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using EnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using HardwareInventoryDto = Dhadgar.Nodes.Models.HardwareInventoryDto;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// D-S246 (campaign-security-1 N1): two enrollments that validated the same one-time token must not both win.
/// Deterministic: each request stops inside certificate issuance (after ValidateTokenAsync, before its save)
/// until the test releases it, so B always validates before A saves. SQLite runs the real relational UPDATE.
/// </summary>
public sealed class EnrollmentTokenRaceTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task EnrollAsync_SameTokenValidatedByTwoRequests_OnlyTheFirstToSaveEnrolls()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodesDbContext>().UseSqlite(connection).Options;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var orgId = Guid.NewGuid();

        Guid tokenId;
        string plainToken;
        using (var setup = new NodesDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var (token, plain) = await CreateTokenService(setup, time).CreateTokenAsync(orgId, "admin-1", "race");
            tokenId = token.Id;
            plainToken = plain;
        }

        var ca = new TestCertificateAuthorityService(time);
        await ca.InitializeAsync();
        var gateA = new GatedCertificateAuthority(ca);
        var gateB = new GatedCertificateAuthority(ca);

        using var contextA = new NodesDbContext(options);
        using var contextB = new NodesDbContext(options);
        var serviceA = CreateEnrollmentService(contextA, gateA, time);
        var serviceB = CreateEnrollmentService(contextB, gateB, time);

        // Act
        var taskA = serviceA.EnrollAsync(Request(plainToken, "host-a"));
        await gateA.Entered.Task.WaitAsync(StepTimeout);   // A validated the token and holds before its save
        var taskB = serviceB.EnrollAsync(Request(plainToken, "host-b"));
        await gateB.Entered.Task.WaitAsync(StepTimeout);   // B validated the same, still unused, token

        gateA.Release.SetResult();
        var resultA = await taskA.WaitAsync(StepTimeout);
        gateB.Release.SetResult();
        var resultB = await taskB.WaitAsync(StepTimeout);

        // Assert
        Assert.True(resultA.Success);
        Assert.False(resultB.Success);
        Assert.Equal("invalid_token", resultB.Error);

        using var verify = new NodesDbContext(options);
        var node = Assert.Single(await verify.Nodes.AsNoTracking().ToListAsync());
        Assert.Equal(resultA.Value!.NodeId, node.Id);
        Assert.Equal(node.Id, Assert.Single(await verify.AgentCertificates.AsNoTracking().ToListAsync()).NodeId);
        Assert.Single(await verify.HardwareInventories.AsNoTracking().ToListAsync());
        Assert.Single(await verify.NodeCapacities.AsNoTracking().ToListAsync());

        var storedToken = await verify.EnrollmentTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        Assert.Equal(node.Id, storedToken.UsedByNodeId);

        // The loser's failure is audited (proves the change tracker was cleared before the audit save).
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a =>
            a.Action == AuditActions.EnrollmentFailed && a.FailureReason == "invalid_token"));
    }

    private static EnrollNodeRequest Request(string token, string hostname) => new(
        Token: token,
        Platform: "linux",
        PublicKey: TestAgentKeys.PublicKeyBase64,
        Hardware: new HardwareInventoryDto(
            Hostname: hostname,
            OsVersion: "Ubuntu 22.04",
            CpuCores: 8,
            MemoryBytes: 16L * 1024 * 1024 * 1024,
            DiskBytes: 500L * 1024 * 1024 * 1024,
            NetworkInterfaces: null));

    private static EnrollmentTokenService CreateTokenService(NodesDbContext context, TimeProvider time) =>
        new(context, CreateAuditService(context, time), time, Options.Create(new NodesOptions()),
            NullLogger<EnrollmentTokenService>.Instance);

    private static EnrollmentService CreateEnrollmentService(
        NodesDbContext context, ICertificateAuthorityService ca, TimeProvider time) =>
        new(context,
            CreateTokenService(context, time),
            ca,
            CreateAuditService(context, time),
            new TestNodesEventPublisher(),
            time,
            Options.Create(new NodesOptions()),
            NullLogger<EnrollmentService>.Instance);

    // The real AuditService: it saves through the same DbContext as the enrollment, which is what D-S246's
    // ChangeTracker.Clear() protects.
    private static AuditService CreateAuditService(NodesDbContext context, TimeProvider time)
    {
        var accessor = Substitute.For<IAuditContextAccessor>();
        accessor.GetActorId().Returns("race-test-agent");
        accessor.GetActorType().Returns(ActorType.Agent);
        return new AuditService(context, accessor, time, NullLogger<AuditService>.Instance);
    }

    private sealed class GatedCertificateAuthority(ICertificateAuthorityService inner) : ICertificateAuthorityService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken ct = default) => inner.InitializeAsync(ct);

        public Task<CertificateIssuanceResult> IssueCertificateAsync(Guid nodeId, CancellationToken ct = default) =>
            inner.IssueCertificateAsync(nodeId, ct);

        public async Task<CertificateIssuanceResult> IssueCertificateForPublicKeyAsync(
            Guid nodeId, string subjectPublicKeyInfoBase64, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(StepTimeout, ct);
            return await inner.IssueCertificateForPublicKeyAsync(nodeId, subjectPublicKeyInfoBase64, ct);
        }

        public Task<string> GetCaCertificatePemAsync(CancellationToken ct = default) =>
            inner.GetCaCertificatePemAsync(ct);

        public Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default) =>
            inner.ValidateCertificateAsync(certificatePem, ct);
    }
}
```

Adjust only what the compiler forces, and report each adjustment:

- an unused `using` (for example `Dhadgar.Nodes.Models`, if the aliases cover it);
- an `ICertificateAuthorityService` member this list misses (delegate it to `inner`);
- the `AuditLogs` / `HardwareInventories` / `NodeCapacities` DbSet names, if they differ (`AuditService` uses `AuditLogs`; `EnrollmentService` uses `HardwareInventories` and `NodeCapacities`).

Do not change the gating order or the assertions.

**Why it fails when reverted:**

- Without step 1 (no concurrency token), B's UPDATE has no `"UsedAt" IS NULL`, so B also saves. `resultB.Success` is true and the test fails, with two `Node` rows.
- Without step 3's catch, B throws `DbUpdateConcurrencyException`, `taskB` faults and the test fails.
- Without step 3's `ChangeTracker.Clear()`, the loser's audit save re-sends the rejected entities and throws again, so the test fails.

### Step 6: validity tests (`MtlsMiddlewareTests.cs`, class `CertificateValidationServiceTests`)

Add after `ValidateClientCertificateAsync_NotYetValidCertificate_ReturnsFailure`:

```csharp
    // D-S247 (campaign-security-1 N2): X509Certificate2 validity times are local. Compared with a UTC DateTime,
    // the window shifts by the host's UTC offset. On a UTC host these pass with or without the fix; on a host
    // at least 30 minutes off UTC, reverting the comparison fails at least one of them.
    [Fact]
    public async Task ValidateClientCertificateAsync_ExpiredThirtyMinutesAgo_IsExpiredInAnyTimeZone()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow();
        using var certificate = CreateCertificateWithValidity(now.AddDays(-1), now.AddMinutes(-30));
        var service = CreateService(allowExpired: false);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.Equal("certificate_expired", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_ValidForThirtyMoreMinutes_IsNotExpiredInAnyTimeZone()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow();
        using var certificate = CreateCertificateWithValidity(now.AddMinutes(-30), now.AddMinutes(30));
        var service = CreateService(allowExpired: false);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.NotEqual("certificate_expired", result.ErrorCode);
        Assert.NotEqual("certificate_not_yet_valid", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_ValidInThirtyMinutes_IsNotYetValidInAnyTimeZone()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow();
        using var certificate = CreateCertificateWithValidity(now.AddMinutes(30), now.AddDays(1));
        var service = CreateService(allowExpired: false);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.Equal("certificate_not_yet_valid", result.ErrorCode);
    }
```

Add this helper next to `CreateNotYetValidCertificate`:

```csharp
    private static X509Certificate2 CreateCertificateWithValidity(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ValidityWindow",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(notBefore, notAfter);
    }
```

### Step 7: docs (`src/Dhadgar.Nodes/README.md`)

In "Security" → "Agent Authentication", append this sentence to item 2 (line 527, after "receives mTLS certificate"):

> The token is consumed by a conditional write at the enrollment's save: of two requests with the same token, only the first to save enrolls; the other gets `invalid_token`, and no node or certificate row is kept for it (D-S246).

No other doc changes. `docs/PROJECT-STATE.md` is P15's.

## 4. Checks (in order; all in the p39 worktree)

```powershell
$wt = 'C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p39'
dotnet build "$wt\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj" -c Release
dotnet test "$wt\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj" -c Release
dotnet test "$wt\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj" -c Release
dotnet test "$wt\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj" -c Release
$env:CI = 'true'; dotnet build "$wt\Dhadgar.sln" -c Release; Remove-Item Env:CI
git -C $wt status --porcelain=v1 --untracked-files=all
```

- The full Nodes.Tests run must be green. It includes `EnrollmentServiceTests` (InMemory, unchanged), `NodesModelSnapshotTests` (snapshot still matches), the in-project enrollment seams and the three new validity tests.
- AgentSeam.Tests holds S2 and S4, which enroll through the Gateway.
- Uat.Tests compiles against `NodesDbContext`.
- `git status` must list only the seven files in § 3. Restore any dirty lock file with `git checkout -- <file>` and report it.

**Mutation checks (executed, then restored; report each result):**

1. Comment out step 1's `.IsConcurrencyToken()` and run `dotnet test ... --filter FullyQualifiedName~EnrollmentTokenRaceTests`. It must fail (`resultB.Success` true). Restore.
2. Comment out step 3's `_dbContext.ChangeTracker.Clear();` and run the same filter. It must fail (B throws). Restore.
3. First check `(Get-TimeZone).BaseUtcOffset`. If it is zero, skip this check and report "UTC host, N2 mutation not observable". Otherwise, revert step 4 to the old two lines (`var now = _timeProvider.GetUtcNow().UtcDateTime;`, `certificate.NotAfter < now`, `certificate.NotBefore > now`). Run `--filter FullyQualifiedName~InAnyTimeZone`. On this machine (UTC-5/-6), `ValidForThirtyMoreMinutes` and `ValidInThirtyMinutes` must fail. Restore.

After restoring, rerun the two filters green, then commit with explicit paths (no `git add -A`):

```powershell
git -C $wt add -- src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs src/Dhadgar.Nodes/Data/Migrations/NodesDbContextModelSnapshot.cs src/Dhadgar.Nodes/Services/EnrollmentService.cs src/Dhadgar.Nodes/Auth/CertificateValidationService.cs tests/Dhadgar.Nodes.Tests/EnrollmentTokenRaceTests.cs tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs src/Dhadgar.Nodes/README.md
```

Stop with `blocked spec` if:

- `NodesModelSnapshotTests` fails after step 2;
- the race test cannot create the schema on SQLite;
- any InMemory test fails with `DbUpdateConcurrencyException`.

Do not work around any of these.

## 5. Merge and review

P39 merges locally into `campaign/sprint2-agent-nodes-w1` like every packet (D-S215), then ops pushes w1 so that PR #235 picks it up. The campaign security loop continues with **pass 2** on `main..<new w1 tip>`. Pass 2 re-checks N1 and N2 and covers what pass 1 left open (`HeartbeatPayloadMapper` limits, `PortableStateFileSecurity`, options validation, `scripts/uat/*.ps1`, the final MSI table dump). The campaign packet-reviewer re-checks P39 against this note. No UAT rerun is needed: `src/` changes only in `Dhadgar.Nodes` server logic, which the Process, Service and Msi runs exercise only through a single enrollment. A single enrollment's behaviour does not change.

## 6. Residuals and follow-ups (owner-visible; add to the PR #235 approval list and P15's follow-ups)

| ID | Item | Why not now |
| --- | --- | --- |
| FU-P39-1 | Nodes has no PostgreSQL integration fixture. Add Testcontainers PostgreSQL (`Directory.Packages.props` entry, a Nodes fixture, a CI job with Docker) and run the D-S246 race test against it, plus the existing `ConcurrentReservations_PreventOverProvisioning`. | New package and CI dependency on the merge path. The SQLite test already fails when the fix is reverted. |
| R-P39-1 | `RevokeTokenAsync` racing a consume of the same token: revoke's UPDATE now also carries `"UsedAt" IS NULL` and throws `DbUpdateConcurrencyException` (500) instead of revoking a token that is already used. The window is one admin call against one enrollment. The token ends consumed either way. | Admin-only, millisecond window, fails closed. Follow-up: catch it in `RevokeTokenAsync` and return `false`. |
| R-P39-2 | The loser of a race has already published `NodeEnrolled` and `AgentCertificateIssued` (publish before save, no `UseBusOutbox`, the known pass-1 residual). No consumer of either event exists in `src/` today. | Fixed by the existing "notifications outbox" / `UseBusOutbox` follow-up. With the bus outbox, `ChangeTracker.Clear()` also drops the loser's outbox rows. |
| R-P39-3 | A token revoked between the loser's validate and its save is not caught by the concurrency token (only `UsedAt` is one). The outcome equals a revoke that lands just after the enrollment. | Same outcome as an ordinary race between revoke and use; not a new exposure. |
| R-P39-4 | The D-S247 tests cannot detect a revert on a UTC host (CI). They do detect it on any developer machine that is off UTC. | A real fix needs process-level `TZ` control. Not worth it for a two-line comparison. |
| (pass 1) | Events before save; `AgentHub` treats a missing certificate row as usable; `Hardware` strings unchecked for control characters; the enrollment token is an unzeroable managed string. | As pass 1 says: residuals, no action in this campaign. |
