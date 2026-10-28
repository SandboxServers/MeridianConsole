using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// Runs one read-only query against a named lab database. The caller supplies the reader role, so the check never holds a connection.
/// </summary>
public delegate Task<Result<DbQueryResult>> InvariantSqlQuery(string database, string sql, CancellationToken ct);

/// <summary>
/// One service's EF outbox: the database and the MassTransit message and state tables, with the time column each one is aged by.
/// </summary>
public sealed record OutboxTarget(
    string Service,
    string Database,
    string MessageTable,
    string MessageTimeColumn,
    string StateTable,
    string StateTimeColumn,
    string DeliveredColumn);

/// <summary>
/// I2: the outbox drains (C11). A message row older than 60 s, or a state row not delivered after 60 s, is a violation.
/// The Nodes target is the only one here; L34 and L36 add the others.
/// </summary>
public sealed class I2OutboxInvariant : ISkippableInvariant
{
    private const int EvidenceRows = 5;
    private const int MaxDetailLength = 512;

    /// <summary>The Nodes outbox: MassTransit tables <c>OutboxMessage</c> (aged by <c>EnqueueTime</c>) and <c>OutboxState</c> (aged by <c>Created</c>, delivered by <c>Delivered</c>).</summary>
    public static IReadOnlyList<OutboxTarget> NodesTargets { get; } =
    [
        new OutboxTarget("nodes", "nodes", "OutboxMessage", "EnqueueTime", "OutboxState", "Created", "Delivered"),
    ];

    private readonly IReadOnlyList<OutboxTarget> _targets;
    private readonly InvariantSqlQuery _query;
    private readonly Redactor _redactor;
    private readonly InvariantDbGate? _gate;

    /// <summary>
    /// Creates the check over the given outbox targets. The query delegate is the lab's reader-role <see cref="LabDb"/>.
    /// The optional gate answers whether a database can be checked yet (D-ML80); without it every database is checked.
    /// </summary>
    public I2OutboxInvariant(IEnumerable<OutboxTarget> targets, InvariantSqlQuery query, Redactor redactor, InvariantDbGate? gate = null)
    {
        _targets = targets.ToArray();
        _query = query;
        _redactor = redactor;
        _gate = gate;
    }

    /// <inheritdoc />
    public string Id => "I2";

    /// <summary>The skip reason written for a database that is not applicable yet (shared with I3).</summary>
    internal static string SkipReason(DbApplicability state) =>
        state == DbApplicability.StackNotUp ? "stack not up (no lab credentials)" : "schema not created by its service yet";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        Result<InvariantOutcome> outcome = await CheckWithSkipsAsync(window, ct).ConfigureAwait(false);
        return outcome.IsSuccess
            ? Result<IReadOnlyList<InvariantViolation>>.Success(outcome.Value.Violations)
            : Result<IReadOnlyList<InvariantViolation>>.Failure(outcome.Error);
    }

    /// <inheritdoc />
    public async Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct)
    {
        var violations = new List<InvariantViolation>();
        var skipped = new List<InvariantSkip>();
        var gated = new Dictionary<string, DbApplicability>(StringComparer.Ordinal);

        foreach (OutboxTarget target in _targets)
        {
            ct.ThrowIfCancellationRequested();
            if (_gate is not null)
            {
                if (!gated.TryGetValue(target.Database, out DbApplicability state))
                {
                    Result<DbApplicability> answer = await _gate(target.Database, ct).ConfigureAwait(false);
                    if (answer.IsFailure)
                    {
                        return Result<InvariantOutcome>.Failure($"{target.Service} applicability: {answer.Error}");
                    }

                    state = answer.Value;
                    gated[target.Database] = state;
                    if (state != DbApplicability.Applicable)
                    {
                        skipped.Add(new InvariantSkip(Id, "db:" + target.Database, SkipReason(state)));
                    }
                }

                if (state != DbApplicability.Applicable)
                {
                    continue;
                }
            }

            var probes = new[]
            {
                (CheckId: "outbox-message-stuck",
                 Table: target.MessageTable,
                 Sql: $"select \"SequenceNumber\" from \"{target.MessageTable}\" where \"{target.MessageTimeColumn}\" < now() - interval '60 seconds'"),
                (CheckId: "outbox-state-undelivered",
                 Table: target.StateTable,
                 Sql: $"select \"OutboxId\" from \"{target.StateTable}\" where \"{target.StateTimeColumn}\" < now() - interval '60 seconds' and \"{target.DeliveredColumn}\" is null"),
            };

            foreach (var probe in probes)
            {
                Result<DbQueryResult> query = await _query(target.Database, probe.Sql, ct).ConfigureAwait(false);
                if (query.IsFailure)
                {
                    return Result<InvariantOutcome>.Failure($"{target.Service} {probe.CheckId}: {query.Error}");
                }

                if (query.Value.RowCount == 0)
                {
                    continue;
                }

                string detail = $"{probe.Table}: {query.Value.RowCount} row(s) older than 60 s";
                JsonElement evidence = _redactor.Redact(JsonSerializer.SerializeToElement(new
                {
                    table = probe.Table,
                    rows = query.Value.Rows.Take(EvidenceRows).ToArray(),
                }));

                violations.Add(new InvariantViolation(
                    Id,
                    target.Service,
                    probe.CheckId,
                    detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail,
                    window.To,
                    evidence));
            }
        }

        return Result<InvariantOutcome>.Success(new InvariantOutcome(violations, skipped));
    }
}
