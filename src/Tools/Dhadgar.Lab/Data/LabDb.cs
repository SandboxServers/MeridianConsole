using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Npgsql;
using Npgsql.Schema;
using NpgsqlTypes;

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

                RawRows raw;
                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    raw = await ReadRowsAsync(reader, maxRows, ct).ConfigureAwait(false);
                }

                // The reader is disposed (Npgsql allows one open reader per connection), so the catalog read runs now, still in the READ ONLY transaction.
                IReadOnlyDictionary<(uint TableOid, short Attnum), (string Table, string Column)> origins =
                    await ResolveOriginsAsync(conn, raw.Columns, ct).ConfigureAwait(false);

                return Result<DbQueryResult>.Success(BuildResult(database, raw, origins));
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
            return Result<DbQueryResult>.Failure(RejectedStatementError(ex.SqlState));
        }
        catch (NpgsqlException)
        {
            return Result<DbQueryResult>.Failure("[Lab.Unavailable] the database is not reachable on the lab host port");
        }
    }

    /// <summary>The error for a statement Postgres rejected; only the SQLSTATE is returned (the message can echo values).</summary>
    internal static string RejectedStatementError(string sqlState) =>
        $"[Lab.InvalidArgument] Postgres rejected the statement (SQLSTATE {sqlState})";

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

    /// <summary>One result column as Postgres reports it: its name, its base table column (zero for an expression) and its type.</summary>
    internal sealed record ColumnInfo(string Name, uint TableOid, short Attnum, string DataTypeName);

    /// <summary>The buffered cells of a reader, before the catalog lookup and the JSON conversion.</summary>
    internal sealed record RawRows(IReadOnlyList<ColumnInfo> Columns, List<object?[]> Rows, bool Truncated);

    private static async Task<RawRows> ReadRowsAsync(NpgsqlDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new List<ColumnInfo>(reader.FieldCount);
        foreach (NpgsqlDbColumn schema in reader.GetColumnSchema())
        {
            columns.Add(new ColumnInfo(
                schema.ColumnName,
                schema.TableOID,
                schema.ColumnAttributeNumber ?? 0,
                schema.DataTypeName));
        }

        var rows = new List<object?[]>();
        bool truncated = false;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // One row past the limit tells the caller that the result was cut.
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            var values = new object?[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(values);
        }

        return new RawRows(columns, rows, truncated);
    }

    /// <summary>
    /// Resolves the base table column of each result column with one bound catalog query. A column with no table OID or
    /// attribute number is an expression and gets no entry.
    /// </summary>
    private static async Task<IReadOnlyDictionary<(uint TableOid, short Attnum), (string Table, string Column)>> ResolveOriginsAsync(
        NpgsqlConnection conn,
        IReadOnlyList<ColumnInfo> columns,
        CancellationToken ct)
    {
        uint[] oids = columns
            .Where(c => c.TableOid != 0 && c.Attnum != 0)
            .Select(c => c.TableOid)
            .Distinct()
            .ToArray();

        var origins = new Dictionary<(uint TableOid, short Attnum), (string Table, string Column)>();
        if (oids.Length == 0)
        {
            return origins;
        }

        await using var catalog = new NpgsqlCommand(
            "SELECT c.oid, a.attnum, c.relname, a.attname FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid WHERE c.oid = ANY(@oids)",
            conn);
        _ = catalog.Parameters.Add(OidArrayParameter("oids", oids));

        await using NpgsqlDataReader catalogReader = await catalog.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await catalogReader.ReadAsync(ct).ConfigureAwait(false))
        {
            origins[(catalogReader.GetFieldValue<uint>(0), catalogReader.GetFieldValue<short>(1))] =
                (catalogReader.GetFieldValue<string>(2), catalogReader.GetFieldValue<string>(3));
        }

        return origins;
    }

    /// <summary>
    /// The catalog lookup's OID list. Npgsql has no default PostgreSQL type for <c>uint</c> (oid, xid or cid), so the array is typed as <c>oid[]</c> (F143).
    /// </summary>
    internal static NpgsqlParameter<uint[]> OidArrayParameter(string name, uint[] oids) =>
        new(name, oids) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Oid };

    /// <summary>
    /// Converts the buffered cells to JSON, replacing each masked, non-null cell with <see cref="ColumnMask.Masked"/>.
    /// Column names are kept.
    /// </summary>
    internal static DbQueryResult BuildResult(
        string database,
        RawRows raw,
        IReadOnlyDictionary<(uint TableOid, short Attnum), (string Table, string Column)> origins)
    {
        var columns = new List<string>(raw.Columns.Count);
        foreach (ColumnInfo column in raw.Columns)
        {
            columns.Add(column.Name);
        }

        var masks = new bool[raw.Columns.Count];
        for (int i = 0; i < raw.Columns.Count; i++)
        {
            ColumnInfo column = raw.Columns[i];
            (string Table, string Column)? origin = null;
            if (column.TableOid != 0 && column.Attnum != 0 && origins.TryGetValue((column.TableOid, column.Attnum), out (string Table, string Column) found))
            {
                origin = found;
            }

            masks[i] = ColumnMask.ShouldMask(database, origin, column.DataTypeName);
        }

        var rows = new List<Dictionary<string, JsonElement>>(raw.Rows.Count);
        foreach (object?[] values in raw.Rows)
        {
            var row = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            for (int i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = masks[i] && values[i] is not null
                    ? JsonSerializer.SerializeToElement(ColumnMask.Masked, LabJson.Options)
                    : ToJson(values[i]);
            }

            rows.Add(row);
        }

        return new DbQueryResult(columns, rows, rows.Count, raw.Truncated);
    }

    private static JsonElement ToJson(object? value)
    {
        if (value is null)
        {
            return JsonSerializer.SerializeToElement<string?>(null, LabJson.Options);
        }

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
