using Dhadgar.Shared.Results;
using Npgsql;

namespace Dhadgar.Lab.Stack;

/// <summary>
/// One truncated lab database and the tables that were truncated in it.
/// </summary>
public sealed record TruncatedDatabase(string Database, IReadOnlyList<string> Tables);

/// <summary>
/// Empties the lab databases between scenarios. Each database is reached as its owner on the direct host port, lists its
/// public base tables except the EF migrations history, and truncates them in one statement with every name quoted.
/// </summary>
public sealed class LabDataReset
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private const string ListTablesSql =
        "SELECT table_name::text FROM information_schema.tables " +
        "WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory' " +
        "ORDER BY table_name";

    private readonly LabConnectionStrings _connections;

    /// <summary>
    /// Creates the reset over the lab connection strings.
    /// </summary>
    public LabDataReset(LabConnectionStrings connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    /// <summary>
    /// Truncates the named lab databases, in order. Any unknown name fails the call before anything is truncated.
    /// </summary>
    public async Task<Result<IReadOnlyList<TruncatedDatabase>>> ResetAsync(IReadOnlyCollection<string> databases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(databases);

        foreach (string database in databases)
        {
            if (!LabStack.LabDatabaseServices.Contains(database, StringComparer.Ordinal))
            {
                return Result<IReadOnlyList<TruncatedDatabase>>.Failure("[Lab.InvalidArgument] database must be a lab database");
            }
        }

        var results = new List<TruncatedDatabase>(databases.Count);
        foreach (string database in databases.Distinct(StringComparer.Ordinal))
        {
            Result<TruncatedDatabase> one = await ResetOneAsync(database, ct).ConfigureAwait(false);
            if (one.IsFailure)
            {
                return Result<IReadOnlyList<TruncatedDatabase>>.Failure(one.Error);
            }

            results.Add(one.Value);
        }

        return Result<IReadOnlyList<TruncatedDatabase>>.Success(results);
    }

    /// <summary>
    /// Builds the TRUNCATE statement for the given table names; every name is quoted, and the migrations history is never listed.
    /// </summary>
    internal static string BuildTruncate(IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var quoted = tables
            .Where(name => !string.Equals(name, MigrationsHistoryTable, StringComparison.Ordinal))
            .Select(QuoteIdentifier);
        return $"TRUNCATE TABLE {string.Join(", ", quoted)} RESTART IDENTITY CASCADE";
    }

    /// <summary>
    /// Double-quotes a Postgres identifier, doubling any embedded quote.
    /// </summary>
    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The only dynamic SQL is the TRUNCATE of table names read from information_schema, each quoted; no caller text reaches it.")]
    private async Task<Result<TruncatedDatabase>> ResetOneAsync(string database, CancellationToken ct)
    {
        Result<string> connection = _connections.OwnerDirect(database);
        if (connection.IsFailure)
        {
            return Result<TruncatedDatabase>.Failure(connection.Error);
        }

        try
        {
            await using var conn = new NpgsqlConnection(connection.Value);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            var tables = new List<string>();
            await using (var list = new NpgsqlCommand(ListTablesSql, conn))
            await using (var reader = await list.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            if (tables.Count == 0)
            {
                return Result<TruncatedDatabase>.Success(new TruncatedDatabase(database, tables));
            }

            await using (var truncate = new NpgsqlCommand(BuildTruncate(tables), conn))
            {
                _ = await truncate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return Result<TruncatedDatabase>.Success(new TruncatedDatabase(database, tables));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The message is not returned: a driver error can echo the connection string.
            return Result<TruncatedDatabase>.Failure($"[Lab.Failed] reset of a lab database failed: {ex.GetType().Name}");
        }
    }
}
