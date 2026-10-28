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
