using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Dhadgar.Nodes.Data;

/// <summary>
/// Retries transient database failures (after a Postgres blip, an idle pooled connection is dead). On a
/// client-side connection failure it clears the Npgsql pool before the retry, so the retry opens a fresh connection.
/// </summary>
public sealed class NodesRetryingExecutionStrategy : NpgsqlRetryingExecutionStrategy
{
    /// <summary>Retries after the first failed attempt.</summary>
    public const int MaxRetries = 3;

    /// <summary>Upper bound on the delay between attempts.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    private bool _clearPoolBeforeRetry;

    public NodesRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, MaxRetries, MaxDelay, errorCodesToAdd: null)
    {
    }

    /// <summary>A client-side connection failure (I/O, socket, timeout), not a server error code.</summary>
    public static bool IsConnectionFailure(Exception? exception) =>
        exception is NpgsqlException { IsTransient: true } and not PostgresException;

    protected override bool ShouldRetryOn(Exception? exception)
    {
        bool retry = base.ShouldRetryOn(exception);
        if (retry && IsConnectionFailure(exception))
        {
            _clearPoolBeforeRetry = true;
        }

        return retry;
    }

    protected override void OnRetry()
    {
        if (_clearPoolBeforeRetry
            && Dependencies.CurrentContext.Context.Database.GetDbConnection() is NpgsqlConnection connection)
        {
            // Every idle pooled connection may be dead after a blip; the retry must not take the next one.
            NpgsqlConnection.ClearPool(connection);
        }

        _clearPoolBeforeRetry = false;
        base.OnRetry();
    }
}
