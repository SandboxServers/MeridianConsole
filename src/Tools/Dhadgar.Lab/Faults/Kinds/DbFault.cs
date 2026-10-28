using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Arguments of the <c>db</c> fault kind: the mode (<c>terminate_backends</c>, <c>connection_limit</c>, <c>lock_table</c>,
/// <c>read_only</c>), the table for <c>lock_table</c>, and the limit (0..100) for <c>connection_limit</c>.
/// </summary>
public sealed record DbArgs(string? Mode = null, string? Table = null, int? Limit = null);

/// <summary>
/// Heal data of a database fault: the fault id, the database (a lab database), the mode, the table, and the prior state read in prepare.
/// </summary>
public sealed record DbHealData(string FaultId, string Database, string Mode, string? Table, int PriorConnectionLimit, bool PriorReadOnly);

/// <summary>
/// The <c>db</c> fault kind (<c>fault_db</c>): changes one lab database through the Postgres superuser on the host port (L20).
/// The database name is checked against <c>^meridian_lab_[a-z]+$</c> and the table name against the identifier rule,
/// and each must exist, before any SQL runs; both are then double-quoted. Only the fixed mode statements reach SQL text.
/// </summary>
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "The only dynamic SQL is ALTER DATABASE and LOCK TABLE over a database and table name that were validated and are double-quoted; no caller text reaches it.")]
public sealed class DbFault(
    LabConnectionStrings connections,
    ILabTargetRegistry registry,
    FaultTargetGuard guard,
    ILogger<DbFault> logger) : IFaultKind
{
    internal const string DatabasePrefix = "meridian_lab_";
    internal const string ModeTerminate = "terminate_backends";
    internal const string ModeConnectionLimit = "connection_limit";
    internal const string ModeLockTable = "lock_table";
    internal const string ModeReadOnly = "read_only";

    private const int MaxTableNameLength = 63;
    private const int MaxConnectionLimit = 100;

    private const string TerminateSql =
        "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()";

    private const string ConnectionLimitSql = "SELECT datconnlimit FROM pg_database WHERE datname = @db";

    private const string ReadOnlySettingSql =
        "SELECT coalesce(array_to_string(setconfig, ','), '') FROM pg_db_role_setting " +
        "WHERE setdatabase = (SELECT oid FROM pg_database WHERE datname = @db) AND setrole = 0";

    private const string TableExistsSql =
        "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table";

    private readonly LabConnectionStrings _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    private readonly ILabTargetRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly FaultTargetGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    private readonly ILogger<DbFault> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // The lock_table sessions, held until heal. A daemon crash drops them, which is why recovery heal is a no-op.
    private readonly ConcurrentDictionary<string, NpgsqlConnection> _locks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Kind => "db";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The name is checked before anything else: a refused name never reaches a connection or SQL.
        Result<string> database = DatabaseOf(target);
        if (database.IsFailure)
        {
            return Result<JsonElement>.Failure(database.Error);
        }

        Result<DbArgs> parsed = KindSupport.FromHealData<DbArgs>(args);
        if (parsed.IsFailure)
        {
            return Result<JsonElement>.Failure(parsed.Error);
        }

        Result checkedArgs = CheckArgs(parsed.Value);
        if (checkedArgs.IsFailure)
        {
            return Result<JsonElement>.Failure(checkedArgs.Error);
        }

        if (parsed.Value.Mode == ModeLockTable)
        {
            Result<bool> exists = await TableExistsAsync(database.Value, parsed.Value.Table!, ct).ConfigureAwait(false);
            if (exists.IsFailure)
            {
                return Result<JsonElement>.Failure(exists.Error);
            }

            if (!exists.Value)
            {
                return Result<JsonElement>.Failure("[Lab.InvalidArgument] table is not a table of that lab database");
            }
        }

        Result<(int Limit, bool ReadOnly)> prior = await ReadPriorStateAsync(database.Value, ct).ConfigureAwait(false);
        if (prior.IsFailure)
        {
            return Result<JsonElement>.Failure(prior.Error);
        }

        return KindSupport.ToHealData(new DbHealData(
            faultId, database.Value, parsed.Value.Mode!, parsed.Value.Table, prior.Value.Limit, prior.Value.ReadOnly));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<string> database = DatabaseOf(target);
        if (database.IsFailure)
        {
            return Result.Failure(database.Error);
        }

        Result<DbArgs> parsed = KindSupport.FromHealData<DbArgs>(args);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        Result checkedArgs = CheckArgs(parsed.Value);
        if (checkedArgs.IsFailure)
        {
            return checkedArgs;
        }

        switch (parsed.Value.Mode)
        {
            case ModeTerminate:
                return await TerminateBackendsAsync(database.Value, ct).ConfigureAwait(false);

            case ModeConnectionLimit:
                return await SetConnectionLimitAsync(database.Value, parsed.Value.Limit!.Value, ct).ConfigureAwait(false);

            case ModeReadOnly:
                return await SetReadOnlyAsync(database.Value, ct).ConfigureAwait(false);

            default:
                return await LockTableAsync(database.Value, parsed.Value.Table!, faultId, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<DbHealData> heal = KindSupport.FromHealData<DbHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        // The journal's name is checked again before SQL, the same as at prepare.
        Result<string> database = DatabaseOf(heal.Value.Database);
        if (database.IsFailure)
        {
            return Result.Failure(database.Error);
        }

        switch (heal.Value.Mode)
        {
            case ModeLockTable:
                // Disposing the session rolls back the transaction and releases the lock. A missing session is already healed.
                ReleaseLock(heal.Value.FaultId);
                return Result.Success();

            case ModeConnectionLimit:
                return await SetConnectionLimitAsync(database.Value, -1, ct).ConfigureAwait(false);

            case ModeReadOnly:
                return await ResetReadOnlyAsync(database.Value, ct).ConfigureAwait(false);

            default:
                // terminate_backends is one-shot: nothing to undo.
                return Result.Success();
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        int reset = 0;
        foreach (LabTarget target in _registry.All.Where(t => t.Kind == LabTargetKind.Database))
        {
            // Every candidate goes through the blast-radius guard first; a database that is not registered is never touched.
            Result<ResolvedTarget> resolved = await _guard.ResolveAsync(target.TargetId, Kind, ct).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                continue;
            }

            Result<string> database = DatabaseOf(resolved.Value);
            if (database.IsFailure)
            {
                continue;
            }

            Result limit = await SetConnectionLimitAsync(database.Value, -1, ct).ConfigureAwait(false);
            Result readOnly = await ResetReadOnlyAsync(database.Value, ct).ConfigureAwait(false);
            if (limit.IsSuccess && readOnly.IsSuccess)
            {
                reset++;
                _logger.LogInformation("Reconcile reset the lab database {Database}", database.Value);
            }
        }

        return Result<int>.Success(reset);
    }

    /// <summary>
    /// The database name of a target, if it is a lab database: <c>meridian_lab_</c> then lowercase letters only.
    /// </summary>
    internal static Result<string> DatabaseOf(ResolvedTarget target) => DatabaseOf(target.Name);

    internal static Result<string> DatabaseOf(string? name)
    {
        if (name is null || !name.StartsWith(DatabasePrefix, StringComparison.Ordinal) || name.Length == DatabasePrefix.Length)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] database must be a lab database");
        }

        for (int i = DatabasePrefix.Length; i < name.Length; i++)
        {
            if (name[i] is < 'a' or > 'z')
            {
                return Result<string>.Failure("[Lab.InvalidArgument] database must be a lab database");
            }
        }

        return Result<string>.Success(name);
    }

    /// <summary>
    /// A table name is an identifier: letters, digits and underscores, not starting with a digit, at most 63 characters.
    /// </summary>
    internal static bool IsTableName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxTableNameLength || char.IsDigit(name[0]))
        {
            return false;
        }

        return name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
    }

    /// <summary>
    /// Double-quotes an identifier that has already passed <see cref="IsTableName"/> or <see cref="DatabaseOf(string?)"/>.
    /// </summary>
    internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// The <c>ALTER DATABASE ... CONNECTION LIMIT</c> statement for a validated database and a limit (-1 for none).
    /// </summary>
    internal static string ConnectionLimitStatement(string database, int limit) =>
        "ALTER DATABASE " + Quote(database) + " CONNECTION LIMIT " + limit.ToString(CultureInfo.InvariantCulture);

    private static Result CheckArgs(DbArgs args)
    {
        switch (args.Mode)
        {
            case ModeTerminate:
            case ModeReadOnly:
                return Result.Success();

            case ModeConnectionLimit:
                return args.Limit is >= 0 and <= MaxConnectionLimit
                    ? Result.Success()
                    : Result.Failure("[Lab.InvalidArgument] limit must be 0..100");

            case ModeLockTable:
                return IsTableName(args.Table)
                    ? Result.Success()
                    : Result.Failure("[Lab.InvalidArgument] table must be a plain table name");

            default:
                return Result.Failure("[Lab.InvalidArgument] mode must be terminate_backends, connection_limit, lock_table or read_only");
        }
    }

    private static string WithDatabase(string admin, string database) =>
        new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;

    private async Task<Result> TerminateBackendsAsync(string database, CancellationToken ct)
    {
        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result.Failure(admin.Error);
        }

        Result<object?> run = await ScalarAsync(admin.Value, TerminateSql, database, ct).ConfigureAwait(false);
        return run.IsFailure ? Result.Failure(run.Error) : Result.Success();
    }

    private async Task<Result> SetConnectionLimitAsync(string database, int limit, CancellationToken ct)
    {
        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result.Failure(admin.Error);
        }

        string sql = ConnectionLimitStatement(database, limit);
        Result<object?> run = await ScalarAsync(admin.Value, sql, null, ct).ConfigureAwait(false);
        return run.IsFailure ? Result.Failure(run.Error) : Result.Success();
    }

    private async Task<Result> SetReadOnlyAsync(string database, CancellationToken ct)
    {
        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result.Failure(admin.Error);
        }

        string sql = "ALTER DATABASE " + Quote(database) + " SET default_transaction_read_only = on";
        Result<object?> run = await ScalarAsync(admin.Value, sql, null, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        // Pooled sessions keep the old setting, so the backends are ended for new sessions to pick it up.
        return await TerminateBackendsAsync(database, ct).ConfigureAwait(false);
    }

    private async Task<Result> ResetReadOnlyAsync(string database, CancellationToken ct)
    {
        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result.Failure(admin.Error);
        }

        string sql = "ALTER DATABASE " + Quote(database) + " RESET default_transaction_read_only";
        Result<object?> run = await ScalarAsync(admin.Value, sql, null, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        return await TerminateBackendsAsync(database, ct).ConfigureAwait(false);
    }

    private async Task<Result> LockTableAsync(string database, string table, string faultId, CancellationToken ct)
    {
        // The owner role holds the lock, the same as the daemon's own writes (L11).
        Result<string> owner = _connections.OwnerDirect(database[DatabasePrefix.Length..]);
        if (owner.IsFailure)
        {
            return Result.Failure(owner.Error);
        }

        if (_locks.ContainsKey(faultId))
        {
            return Result.Failure("[Lab.Conflict] the table is already locked by this fault");
        }

        var conn = new NpgsqlConnection(owner.Value);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using (var begin = new NpgsqlCommand("BEGIN", conn))
            {
                _ = await begin.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var lockCmd = new NpgsqlCommand("LOCK TABLE " + Quote(table) + " IN ACCESS EXCLUSIVE MODE", conn))
            {
                _ = await lockCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            conn.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            conn.Dispose();
            // The message is not returned: a driver error can echo the connection string.
            return Result.Failure($"[Lab.Failed] lock of a lab table failed: {ex.GetType().Name}");
        }

        if (!_locks.TryAdd(faultId, conn))
        {
            conn.Dispose();
            return Result.Failure("[Lab.Conflict] the table is already locked by this fault");
        }

        _logger.LogInformation("Fault {FaultId} holds an access exclusive lock on a lab table", faultId);
        return Result.Success();
    }

    private void ReleaseLock(string faultId)
    {
        if (_locks.TryRemove(faultId, out NpgsqlConnection? conn))
        {
            conn.Dispose();
        }
    }

    private async Task<Result<bool>> TableExistsAsync(string database, string table, CancellationToken ct)
    {
        if (!IsTableName(table))
        {
            return Result<bool>.Failure("[Lab.InvalidArgument] table must be a plain table name");
        }

        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result<bool>.Failure(admin.Error);
        }

        Result<object?> run = await ScalarAsync(WithDatabase(admin.Value, database), TableExistsSql, table, ct, paramName: nameof(table)).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<bool>.Failure(run.Error);
        }

        return Result<bool>.Success(Convert.ToInt64(run.Value, CultureInfo.InvariantCulture) > 0);
    }

    private async Task<Result<(int Limit, bool ReadOnly)>> ReadPriorStateAsync(string database, CancellationToken ct)
    {
        Result<string> admin = _connections.Admin();
        if (admin.IsFailure)
        {
            return Result<(int Limit, bool ReadOnly)>.Failure(admin.Error);
        }

        Result<object?> limit = await ScalarAsync(admin.Value, ConnectionLimitSql, database, ct).ConfigureAwait(false);
        if (limit.IsFailure)
        {
            return Result<(int Limit, bool ReadOnly)>.Failure(limit.Error);
        }

        if (limit.Value is null)
        {
            return Result<(int Limit, bool ReadOnly)>.Failure("[Lab.NotFound] the lab database does not exist");
        }

        Result<object?> settings = await ScalarAsync(admin.Value, ReadOnlySettingSql, database, ct).ConfigureAwait(false);
        if (settings.IsFailure)
        {
            return Result<(int Limit, bool ReadOnly)>.Failure(settings.Error);
        }

        int priorLimit = Convert.ToInt32(limit.Value, CultureInfo.InvariantCulture);
        bool priorReadOnly = Convert.ToString(settings.Value, CultureInfo.InvariantCulture)?
            .Contains("default_transaction_read_only=on", StringComparison.Ordinal) ?? false;
        return Result<(int Limit, bool ReadOnly)>.Success((priorLimit, priorReadOnly));
    }

    /// <summary>
    /// Runs one statement on a fresh connection and returns its first column. A driver error is a failure whose text
    /// names only the exception type, because the message can echo the connection string.
    /// </summary>
    private static async Task<Result<object?>> ScalarAsync(
        string connection, string sql, string? paramValue, CancellationToken ct, string paramName = "db")
    {
        try
        {
            await using var conn = new NpgsqlConnection(connection);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (paramValue is not null)
            {
                cmd.Parameters.AddWithValue(paramName, paramValue);
            }

            return Result<object?>.Success(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<object?>.Failure($"[Lab.Failed] database operation failed: {ex.GetType().Name}");
        }
    }
}
