using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I2 over a fake reader: stuck message rows and undelivered state rows are violations; an empty outbox is clean.
/// </summary>
public sealed class I2OutboxInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0.AddMinutes(-5), T0, null, null);

    [Fact]
    public async Task I2_MessageRowsOlderThan60s_Violation()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, Fake(messageRows: 2, stateRows: 0), redactor);

        IReadOnlyList<InvariantViolation> violations = (await check.CheckAsync(Window, CancellationToken.None)).Value!;

        InvariantViolation violation = Assert.Single(violations);
        Assert.Equal("I2", violation.InvariantId);
        Assert.Equal("nodes", violation.Service);
        Assert.Equal("outbox-message-stuck", violation.CheckId);
        Assert.Equal(T0, violation.At);
        Assert.NotNull(violation.Evidence);
    }

    [Fact]
    public async Task I2_StateRowsUndelivered_Violation()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, Fake(messageRows: 0, stateRows: 1), redactor);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);

        Assert.Equal("outbox-state-undelivered", violation.CheckId);
    }

    [Fact]
    public async Task I2_EmptyOutbox_NoViolation()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, Fake(messageRows: 0, stateRows: 0), redactor);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task I2_QueryFails_ResultIsFailure()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        InvariantSqlQuery failing = (_, _, _) => Task.FromResult(Result<DbQueryResult>.Failure("[Lab.Db] unreachable"));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, failing, redactor);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task I2_StackNotUp_SkipsWithoutQuerying()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        int calls = 0;
        InvariantSqlQuery counting = (_, _, _) =>
        {
            calls++;
            return Task.FromResult(Result<DbQueryResult>.Success(new DbQueryResult([], [], 0, false)));
        };
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Success(DbApplicability.StackNotUp));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, counting, redactor, gate);

        Result<InvariantOutcome> result = await check.CheckWithSkipsAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Violations);
        Assert.Equal(0, calls);
        InvariantSkip skip = Assert.Single(result.Value.Skipped);
        Assert.Equal("db:nodes", skip.Source);
        Assert.Equal("I2", skip.InvariantId);
    }

    [Fact]
    public async Task I2_SchemaNotReady_Skips()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Success(DbApplicability.SchemaNotReady));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, Fake(messageRows: 0, stateRows: 0), redactor, gate);

        Result<InvariantOutcome> result = await check.CheckWithSkipsAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        InvariantSkip skip = Assert.Single(result.Value.Skipped);
        Assert.StartsWith("schema not created", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I2_Applicable_42P01_IsTablesMissingViolation()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string message = LabDb.RejectedStatementError("42P01");
        InvariantSqlQuery missing = (_, _, _) => Task.FromResult(Result<DbQueryResult>.Failure(message));
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Success(DbApplicability.Applicable));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, missing, redactor, gate);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        InvariantViolation violation = Assert.Single(result.Value!);
        Assert.Equal(I2OutboxInvariant.TablesMissingCheckId, violation.CheckId);
        Assert.Equal("nodes", violation.Service);
        Assert.Contains("42P01", violation.Detail, StringComparison.Ordinal);
        Assert.NotNull(violation.Evidence);
    }

    [Fact]
    public async Task I2_NoGate_42P01_IsStillAFailure()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string message = LabDb.RejectedStatementError("42P01");
        InvariantSqlQuery missing = (_, _, _) => Task.FromResult(Result<DbQueryResult>.Failure(message));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, missing, redactor);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("42P01", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I2_GateFails_IsFailure()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Failure("[Lab.Unavailable] down"));
        var check = new I2OutboxInvariant(I2OutboxInvariant.NodesTargets, Fake(messageRows: 0, stateRows: 0), redactor, gate);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task I2_NotificationsTargets_StuckRow_ViolationForNotifications()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var check = new I2OutboxInvariant(I2OutboxInvariant.AllTargets, Fake(messageRows: 1, stateRows: 0), redactor);

        IReadOnlyList<InvariantViolation> violations = (await check.CheckAsync(Window, CancellationToken.None)).Value!;

        Assert.Contains(violations, v => v.Service == "notifications" && v.CheckId == "outbox-message-stuck");
    }

    [Fact]
    public async Task I2_NodesViolation_AndNotificationsTablesMissing_BothReported()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string message = LabDb.RejectedStatementError("42P01");
        InvariantSqlQuery query = (database, sql, ct) => database == "notifications"
            ? Task.FromResult(Result<DbQueryResult>.Failure(message))
            : Fake(messageRows: 1, stateRows: 0)(database, sql, ct);
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Success(DbApplicability.Applicable));
        var check = new I2OutboxInvariant(I2OutboxInvariant.AllTargets, query, redactor, gate);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, v => v.Service == "nodes" && v.CheckId == "outbox-message-stuck");
        Assert.Contains(result.Value, v => v.Service == "notifications" && v.CheckId == I2OutboxInvariant.TablesMissingCheckId);
    }

    private static InvariantSqlQuery Fake(int messageRows, int stateRows) =>
        (_, sql, _) =>
        {
            int count = sql.Contains("\"OutboxMessage\"", StringComparison.Ordinal) ? messageRows : stateRows;
            var rows = new List<Dictionary<string, JsonElement>>();
            for (int i = 0; i < count; i++)
            {
                rows.Add(new Dictionary<string, JsonElement> { ["SequenceNumber"] = JsonSerializer.SerializeToElement(i) });
            }

            return Task.FromResult(Result<DbQueryResult>.Success(new DbQueryResult(["SequenceNumber"], rows, rows.Count, false)));
        };
}
