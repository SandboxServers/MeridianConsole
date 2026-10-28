using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Lab.Data;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>db</c> source (C7): one read-only statement through <see cref="LabDb"/>. <c>@since</c> is bound automatically;
/// <c>params</c> are bound as parameters after variable substitution. The SQL text itself is never substituted.
/// Result shape: <c>{rows, row_count}</c>.
/// </summary>
public sealed class DbSource : IClauseSource
{
    private readonly LabDb _db;

    /// <summary>
    /// Creates the source over the read-only query runner.
    /// </summary>
    public DbSource(LabDb db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public string Name => "db";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<string> database = ClauseFields.Required(clause.SourceFields, "database");
        if (database.IsFailure)
        {
            return Result<JsonElement>.Failure(database.Error);
        }

        Result<string> sql = ClauseFields.Required(clause.SourceFields, "sql");
        if (sql.IsFailure)
        {
            return Result<JsonElement>.Failure(sql.Error);
        }

        Result<DateTimeOffset> since = window.SinceFor(clause.Since);
        if (since.IsFailure)
        {
            return Result<JsonElement>.Failure(since.Error);
        }

        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (clause.SourceFields.TryGetValue("params", out JsonElement raw))
        {
            if (raw.ValueKind != JsonValueKind.Object)
            {
                return Result<JsonElement>.Failure("[Lab.InvalidArgument] params must be a table");
            }

            foreach (JsonProperty property in raw.EnumerateObject())
            {
                parameters[property.Name] = property.Value;
            }
        }

        Result<Dictionary<string, JsonElement>> expanded = vars.ExpandFields(parameters);
        if (expanded.IsFailure)
        {
            return Result<JsonElement>.Failure(expanded.Error);
        }

        expanded.Value.Add("since", JsonSerializer.SerializeToElement(since.Value.ToString("O", CultureInfo.InvariantCulture)));

        Result<DbQueryResult> query = await _db.QueryAsync(database.Value, sql.Value, expanded.Value, LabDb.MaxRowsCap, ct);
        if (query.IsFailure)
        {
            return Result<JsonElement>.Failure(query.Error);
        }

        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new
        {
            rows = query.Value.Rows,
            row_count = query.Value.RowCount,
        }));
    }
}
