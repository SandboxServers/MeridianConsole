using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Npgsql;

namespace Dhadgar.Lab.Data;

/// <summary>
/// The result of <c>db_query</c>: <c>{columns, rows, row_count, truncated}</c>.
/// </summary>
public sealed record DbQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, JsonElement>> Rows, int RowCount, bool Truncated);

/// <summary>
/// Read-only SQL against the lab service databases (C4 <c>db_query</c>). Connects to the direct host port (R-OBSERVE), never
/// through Toxiproxy, inside a <c>READ ONLY</c> transaction with a 5 s statement timeout. Parameters are bound, never interpolated.
/// </summary>
public sealed class LabDb
{
    /// <summary>The statement timeout applied to every query.</summary>
    public const int StatementTimeoutMs = 5000;

    /// <summary>The largest SQL text accepted.</summary>
    public const int MaxSqlLength = 16 * 1024;

    /// <summary>The largest <c>max_rows</c> a caller may ask for.</summary>
    public const int MaxRowsCap = 500;

    private static readonly HashSet<string> AllowedDatabases = new(StringComparer.Ordinal)
    {
        "nodes", "identity", "notifications", "discord", "betterauth",
    };

    private static readonly Regex ParameterName = new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private const string ReadOnlyError = "[Lab.ReadOnly] the statement would write; db_query is read-only";

    private readonly LabConnectionStrings _connections;

    /// <summary>
    /// Creates the query runner over the connection-string builder.
    /// </summary>
    public LabDb(LabConnectionStrings connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    /// <summary>
    /// Runs one read-only statement. Returns <c>[Lab.ReadOnly]</c> when Postgres refuses a write (SQLSTATE 25006 or 42501).
    /// </summary>
    public async Task<Result<DbQueryResult>> QueryAsync(
        string database,
        string sql,
        IReadOnlyDictionary<string, JsonElement> parameters,
        int maxRows,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!AllowedDatabases.Contains(database))
        {
            return Result<DbQueryResult>.Failure("[Lab.InvalidArgument] database must be one of nodes, identity, notifications, discord, betterauth");
        }

        if (maxRows < 1 || maxRows > MaxRowsCap)
        {
            return Result<DbQueryResult>.Failure("[Lab.InvalidArgument] max_rows must be between 1 and 500");
        }

        if (sql.Trim().Length == 0 || sql.Length > MaxSqlLength)
        {
            return Result<DbQueryResult>.Failure("[Lab.InvalidArgument] sql must be non-empty and at most 16384 characters");
        }

        Result scan = SqlStatementScanner.Check(sql);
        if (scan.IsFailure)
        {
            return Result<DbQueryResult>.Failure(scan.Error);
        }

        Result<List<NpgsqlParameter>> bound = BindParameters(parameters);
        if (bound.IsFailure)
        {
            return Result<DbQueryResult>.Failure(bound.Error);
        }

        Result<string> connection = _connections.Reader(database);
        if (connection.IsFailure)
        {
            return Result<DbQueryResult>.Failure(connection.Error);
        }

        try
        {
            await using var conn = new NpgsqlConnection(connection.Value);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using (var begin = new NpgsqlCommand("BEGIN TRANSACTION READ ONLY", conn))
            {
                _ = await begin.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // The timeout text is a constant that matches StatementTimeoutMs.
            await using (var timeout = new NpgsqlCommand("SET LOCAL statement_timeout = 5000", conn))
            {
                _ = await timeout.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            try
            {
#pragma warning disable CA2100 // db_query runs the caller's SQL by design: one statement (SqlStatementScanner), READ ONLY, parameters bound.
                await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
                cmd.Parameters.AddRange(bound.Value.ToArray());
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return Result<DbQueryResult>.Success(await ReadRowsAsync(reader, maxRows, ct).ConfigureAwait(false));
            }
            finally
            {
                // Nothing is committed: the transaction only ever reads. A failed rollback is harmless; the connection closes.
                await using var rollback = new NpgsqlCommand("ROLLBACK", conn);
                try
                {
                    _ = await rollback.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (NpgsqlException)
                {
                    // The connection is disposed right after; nothing else to undo.
                }
            }
        }
        catch (PostgresException ex) when (ex.SqlState is "25006" or "42501")
        {
            return Result<DbQueryResult>.Failure(ReadOnlyError);
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            return Result<DbQueryResult>.Failure("[Lab.Timeout] statement exceeded 5 s");
        }
        catch (PostgresException ex)
        {
            // The server message can echo literal values, so only the SQLSTATE is returned.
            return Result<DbQueryResult>.Failure($"[Lab.InvalidArgument] Postgres rejected the statement (SQLSTATE {ex.SqlState})");
        }
        catch (NpgsqlException)
        {
            return Result<DbQueryResult>.Failure("[Lab.Unavailable] the database is not reachable on the lab host port");
        }
    }

    private static Result<List<NpgsqlParameter>> BindParameters(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var bound = new List<NpgsqlParameter>(parameters.Count);
        foreach (KeyValuePair<string, JsonElement> entry in parameters)
        {
            if (!ParameterName.IsMatch(entry.Key))
            {
                return Result<List<NpgsqlParameter>>.Failure("[Lab.InvalidArgument] parameter names are letters, digits and underscores");
            }

            JsonElement value = entry.Value;
            object? typed;
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    typed = value.GetString();
                    break;
                case JsonValueKind.True:
                    typed = true;
                    break;
                case JsonValueKind.False:
                    typed = false;
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    typed = null;
                    break;
                case JsonValueKind.Number when value.TryGetInt64(out long whole):
                    typed = whole;
                    break;
                case JsonValueKind.Number when value.TryGetDecimal(out decimal fraction):
                    typed = fraction;
                    break;
                default:
                    return Result<List<NpgsqlParameter>>.Failure("[Lab.InvalidArgument] parameters must be strings, numbers, booleans or null");
            }

            bound.Add(new NpgsqlParameter(entry.Key, typed ?? DBNull.Value));
        }

        return Result<List<NpgsqlParameter>>.Success(bound);
    }

    private static async Task<DbQueryResult> ReadRowsAsync(NpgsqlDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new List<string>(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<Dictionary<string, JsonElement>>();
        bool truncated = false;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // One row past the limit tells the caller that the result was cut.
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            var row = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            for (int i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = ToJson(reader, i);
            }

            rows.Add(row);
        }

        return new DbQueryResult(columns, rows, rows.Count, truncated);
    }

    private static JsonElement ToJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return JsonSerializer.SerializeToElement<string?>(null, LabJson.Options);
        }

        object value = reader.GetValue(ordinal);
        object json = value switch
        {
            string or bool or short or int or long or decimal => value,
            double d when double.IsFinite(d) => d,
            float f when float.IsFinite(f) => f,
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        return JsonSerializer.SerializeToElement(json, LabJson.Options);
    }
}
