using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Data;

/// <summary>
/// <c>db_query</c> arguments (C4). Snake-case on the wire through <see cref="LabJson"/>; <c>params</c> are bound, never interpolated.
/// </summary>
public sealed record DbQueryArgs(string Database, string Sql, Dictionary<string, JsonElement>? Params = null, int? MaxRows = null);

/// <summary>
/// <c>db_query</c> (Open): one read-only statement against a lab service database, through the direct host port.
/// </summary>
public sealed class DbQueryTool : LabTool<DbQueryArgs>
{
    /// <summary>
    /// Rows returned when <c>max_rows</c> is absent.
    /// </summary>
    public const int DefaultMaxRows = 100;

    private readonly LabDb _db;

    /// <summary>
    /// Creates the tool over the read-only query runner.
    /// </summary>
    public DbQueryTool(LabDb db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public override string Name => "db_query";

    /// <inheritdoc />
    public override string Description => "Run one read-only SQL statement against a lab service database (nodes, identity, notifications, discord, betterauth). Bind values with @name parameters; max_rows up to 500.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(DbQueryArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        int maxRows = args.MaxRows ?? DefaultMaxRows;
        IReadOnlyDictionary<string, JsonElement> parameters = args.Params ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        Result<DbQueryResult> result = await _db.QueryAsync(args.Database, args.Sql, parameters, maxRows, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value);
    }
}
