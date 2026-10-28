using System.Text.Json;
using Dhadgar.Lab.Coverage;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Invariants;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// L52: the Nodes transition snapshotter has three outcomes (ok, not applicable, error) and records a changed value.
/// </summary>
public sealed class TransitionSnapshotterTests
{
    private const string NodeId = "11111111-1111-1111-1111-111111111111";
    private const string CertId = "22222222-2222-2222-2222-222222222222";
    private const string TokenId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Snapshotter_ApplicableChangingRows_RecordsTransition()
    {
        // Anti-always-skip: the gate is applicable and the node moves Online (1) to Offline (3).
        var rows = new Queue<int>([1, 3]);
        var snapshotter = new TransitionSnapshotter(Applicable(), (database, sql, max, ct) =>
        {
            if (sql.Contains("from nodes", StringComparison.Ordinal))
            {
                return Task.FromResult(Ok(Rows("{\"id\":\"" + NodeId + "\",\"status\":" + rows.Dequeue() + "}")));
            }

            return Task.FromResult(Ok(Rows()));
        });

        TransitionSnapshot before = (await snapshotter.SnapshotAsync(CancellationToken.None)).Value!;
        TransitionSnapshot after = (await snapshotter.SnapshotAsync(CancellationToken.None)).Value!;

        Assert.True(after.Applicable);
        Assert.Equal(["node:Online->Offline"], TransitionSnapshotter.Diff(before, after));
    }

    [Theory]
    [InlineData(DbApplicability.StackNotUp)]
    [InlineData(DbApplicability.SchemaNotReady)]
    public async Task Snapshotter_NotReady_NotApplicable(DbApplicability state)
    {
        int queries = 0;
        var snapshotter = new TransitionSnapshotter(Gate(state), (database, sql, max, ct) =>
        {
            queries++;
            return Task.FromResult(Ok(Rows()));
        });

        Result<TransitionSnapshot> result = await snapshotter.SnapshotAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applicable);
        Assert.Equal(0, queries);
        Assert.Empty(TransitionSnapshotter.Diff(result.Value, result.Value));
    }

    [Fact]
    public async Task Snapshotter_QueryError_IsError()
    {
        var snapshotter = new TransitionSnapshotter(Applicable(), (database, sql, max, ct) =>
            Task.FromResult(Result<DbQueryResult>.Failure("[Lab.Db] boom")));

        Result<TransitionSnapshot> result = await snapshotter.SnapshotAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Snapshotter_UndefinedTableAfterApplicable_IsErrorNeverASkip()
    {
        // 42P01 after the gate said applicable is an error (C11), never a not-applicable outcome.
        var snapshotter = new TransitionSnapshotter(Applicable(), (database, sql, max, ct) =>
            Task.FromResult(Result<DbQueryResult>.Failure("[Lab.Db] 42P01 relation does not exist")));

        Result<TransitionSnapshot> result = await snapshotter.SnapshotAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Snapshotter_GateFailure_IsError()
    {
        InvariantDbGate gate = (database, ct) => Task.FromResult(Result<DbApplicability>.Failure("[Lab.Db] probe failed"));
        var snapshotter = new TransitionSnapshotter(gate, (database, sql, max, ct) => Task.FromResult(Ok(Rows())));

        Assert.True((await snapshotter.SnapshotAsync(CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task Snapshotter_RevokedCertificateAndUsedToken_RecordTransitions()
    {
        var certs = new Queue<string>(["false", "true"]);
        var tokens = new Queue<string>(["false", "false"]);
        var usedAt = new Queue<string>(["false", "true"]);
        var snapshotter = new TransitionSnapshotter(Applicable(), (database, sql, max, ct) =>
        {
            if (sql.Contains("agent_certificates", StringComparison.Ordinal))
            {
                return Task.FromResult(Ok(Rows("{\"id\":\"" + CertId + "\",\"is_revoked\":" + certs.Dequeue() + "}")));
            }

            if (sql.Contains("enrollment_tokens", StringComparison.Ordinal))
            {
                return Task.FromResult(Ok(Rows("{\"id\":\"" + TokenId + "\",\"used_at_set\":" + usedAt.Dequeue() + ",\"is_revoked\":" + tokens.Dequeue() + "}")));
            }

            return Task.FromResult(Ok(Rows()));
        });

        TransitionSnapshot before = (await snapshotter.SnapshotAsync(CancellationToken.None)).Value!;
        TransitionSnapshot after = (await snapshotter.SnapshotAsync(CancellationToken.None)).Value!;

        Assert.Equal(
            ["agent_certificate:active->revoked", "enrollment_token:unused->used"],
            TransitionSnapshotter.Diff(before, after));
    }

    private static InvariantDbGate Applicable() => Gate(DbApplicability.Applicable);

    private static InvariantDbGate Gate(DbApplicability state) =>
        (database, ct) => Task.FromResult(Result<DbApplicability>.Success(state));

    private static Result<DbQueryResult> Ok(List<Dictionary<string, JsonElement>> rows) =>
        Result<DbQueryResult>.Success(new DbQueryResult(["row"], rows, rows.Count, Truncated: false));

    private static List<Dictionary<string, JsonElement>> Rows(params string[] json) =>
        json.Select(text => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text)!).ToList();
}
