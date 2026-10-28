using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Invariants;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Coverage;

/// <summary>
/// The Nodes entity states at one moment, keyed <c>entity:id</c>. An empty state map with <see cref="Applicable"/> false is
/// the not-applicable outcome (C11): nothing was read and nothing is recorded.
/// </summary>
/// <param name="Applicable">True when the rows were read; false when the gate said the schema or the stack is not ready.</param>
/// <param name="States">The state of each entity id.</param>
public sealed record TransitionSnapshot(bool Applicable, IReadOnlyDictionary<string, string> States);

/// <summary>
/// Reads the Nodes entity states at each row end and reports the transitions between two snapshots (L52, C18). Three
/// outcomes (C11, D-ML80): the rows are read (ok); the gate says <c>StackNotUp</c> or <c>SchemaNotReady</c> (not applicable,
/// no transition, no error); a gate failure or a query error (including 42P01 after the gate said applicable) is an error.
/// </summary>
public sealed class TransitionSnapshotter
{
    /// <summary>The lab database the Nodes tables live in.</summary>
    public const string Database = "nodes";

    private const int MaxRows = 10000;

    private const string NodesSql = "select id, status from nodes";
    private const string CertificatesSql = "select id, is_revoked from agent_certificates";
    private const string TokensSql = "select id, used_at is not null as used_at_set, is_revoked from enrollment_tokens";

    private readonly InvariantDbGate _gate;
    private readonly LabQuery _query;

    /// <summary>
    /// Creates the snapshotter.
    /// </summary>
    /// <param name="gate">The applicability gate (<c>InvariantWiring.DbGate</c> in production).</param>
    /// <param name="query">Runs one read-only statement against a lab database.</param>
    public TransitionSnapshotter(InvariantDbGate gate, LabQuery query)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>
    /// Creates the snapshotter over the lab's database service and its gate.
    /// </summary>
    /// <param name="db">The lab database service (direct 15432, reader role).</param>
    /// <param name="gate">The applicability gate.</param>
    public static TransitionSnapshotter ForLabDb(LabDb db, InvariantDbGate gate)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new TransitionSnapshotter(gate, (database, sql, maxRows, ct) =>
            db.QueryAsync(database, sql, new Dictionary<string, JsonElement>(StringComparer.Ordinal), maxRows, ct));
    }

    /// <summary>
    /// Reads the snapshot. A failure is a reported error; a not-applicable gate returns an empty, non-applicable snapshot.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<TransitionSnapshot>> SnapshotAsync(CancellationToken ct)
    {
        Result<DbApplicability> gate = await _gate(Database, ct).ConfigureAwait(false);
        if (gate.IsFailure)
        {
            return Result<TransitionSnapshot>.Failure("[Lab.Coverage.Snapshot] the database applicability check failed");
        }

        if (gate.Value != DbApplicability.Applicable)
        {
            return Result<TransitionSnapshot>.Success(new TransitionSnapshot(false, new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        var states = new Dictionary<string, string>(StringComparer.Ordinal);

        Result<IReadOnlyList<Dictionary<string, JsonElement>>> nodes = await ReadAsync(NodesSql, ct).ConfigureAwait(false);
        if (nodes.IsFailure)
        {
            return Result<TransitionSnapshot>.Failure(nodes.Error);
        }

        foreach (Dictionary<string, JsonElement> row in nodes.Value)
        {
            states["node:" + IdOf(row)] = StatusNameOf(row, "status");
        }

        Result<IReadOnlyList<Dictionary<string, JsonElement>>> certificates = await ReadAsync(CertificatesSql, ct).ConfigureAwait(false);
        if (certificates.IsFailure)
        {
            return Result<TransitionSnapshot>.Failure(certificates.Error);
        }

        foreach (Dictionary<string, JsonElement> row in certificates.Value)
        {
            states["agent_certificate:" + IdOf(row)] = BoolOf(row, "is_revoked") ? "revoked" : "active";
        }

        Result<IReadOnlyList<Dictionary<string, JsonElement>>> tokens = await ReadAsync(TokensSql, ct).ConfigureAwait(false);
        if (tokens.IsFailure)
        {
            return Result<TransitionSnapshot>.Failure(tokens.Error);
        }

        foreach (Dictionary<string, JsonElement> row in tokens.Value)
        {
            string state = BoolOf(row, "is_revoked") ? "revoked" : BoolOf(row, "used_at_set") ? "used" : "unused";
            states["enrollment_token:" + IdOf(row)] = state;
        }

        return Result<TransitionSnapshot>.Success(new TransitionSnapshot(true, states));
    }

    /// <summary>
    /// The transitions between two snapshots: each id whose state changed is <c>entity:from-&gt;to</c>, in key order. A
    /// snapshot that is not applicable on either side gives no transition.
    /// </summary>
    /// <param name="previous">The earlier snapshot.</param>
    /// <param name="current">The later snapshot.</param>
    public static IReadOnlyList<string> Diff(TransitionSnapshot previous, TransitionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (!previous.Applicable || !current.Applicable)
        {
            return [];
        }

        var transitions = new List<string>();
        foreach (string key in current.States.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!previous.States.TryGetValue(key, out string? from))
            {
                continue;
            }

            string to = current.States[key];
            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                int colon = key.IndexOf(':', StringComparison.Ordinal);
                transitions.Add(key[..colon] + ":" + from + "->" + to);
            }
        }

        return transitions;
    }

    private async Task<Result<IReadOnlyList<Dictionary<string, JsonElement>>>> ReadAsync(string sql, CancellationToken ct)
    {
        Result<DbQueryResult> read = await _query(Database, sql, MaxRows, ct).ConfigureAwait(false);
        if (read.IsFailure)
        {
            // The database error text is not returned: it can echo the statement or a value.
            return Result<IReadOnlyList<Dictionary<string, JsonElement>>>.Failure("[Lab.Coverage.Snapshot] the transition query failed");
        }

        if (read.Value.Truncated)
        {
            return Result<IReadOnlyList<Dictionary<string, JsonElement>>>.Failure("[Lab.Coverage.Snapshot] the transition snapshot was truncated");
        }

        return Result<IReadOnlyList<Dictionary<string, JsonElement>>>.Success(read.Value.Rows);
    }

    private static string IdOf(Dictionary<string, JsonElement> row) =>
        row.TryGetValue("id", out JsonElement id) ? (id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.GetRawText()) : string.Empty;

    private static bool BoolOf(Dictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string StatusNameOf(Dictionary<string, JsonElement> row, string column)
    {
        if (!row.TryGetValue(column, out JsonElement value))
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && Enum.IsDefined(typeof(NodeStatus), number))
        {
            return ((NodeStatus)number).ToString();
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
    }
}

/// <summary>
/// One read-only statement against a lab database: the database name, the SQL, the row limit.
/// </summary>
public delegate Task<Result<DbQueryResult>> LabQuery(string database, string sql, int maxRows, CancellationToken ct);
