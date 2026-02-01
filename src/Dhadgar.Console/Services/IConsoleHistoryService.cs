using Dhadgar.Contracts.Console;

namespace Dhadgar.Console.Services;

public interface IConsoleHistoryService
{
    /// <summary>
    /// Adds a console output line to history.
    /// </summary>
    Task AddOutputAsync(Guid serverId, Guid organizationId, ConsoleOutputType outputType, string content, long sequenceNumber, Guid? sessionId = null, CancellationToken ct = default);

    /// <summary>
    /// Gets recent console history from hot storage (Redis).
    /// </summary>
    Task<IReadOnlyList<ConsoleLine>> GetRecentHistoryAsync(Guid serverId, int lineCount = 100, CancellationToken ct = default);

    /// <summary>
    /// Searches console history in cold storage (PostgreSQL).
    /// </summary>
    Task<ConsoleHistorySearchResult> SearchHistoryAsync(SearchConsoleHistoryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Archives old entries from Redis to PostgreSQL.
    /// </summary>
    Task ArchiveOldEntriesAsync(Guid serverId, CancellationToken ct = default);

    /// <summary>
    /// Purges old entries from PostgreSQL.
    /// </summary>
    Task PurgeOldEntriesAsync(int retentionDays, CancellationToken ct = default);
}
