using Dhadgar.Console.Data;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Contracts.Console;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ContractsOutputType = Dhadgar.Contracts.Console.ConsoleOutputType;
using EntityOutputType = Dhadgar.Console.Data.Entities.ConsoleOutputType;

namespace Dhadgar.Console.Services;

public sealed class ConsoleHistoryService : IConsoleHistoryService
{
    private readonly ConsoleDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ConsoleOptions _options;
    private readonly TimeSpan _hotStorageTtl;
    private const int MaxHotStorageLines = 500;

    public ConsoleHistoryService(
        ConsoleDbContext db,
        IDistributedCache cache,
        IOptions<ConsoleOptions> options)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
        _hotStorageTtl = TimeSpan.FromMinutes(_options.HotStorageTtlMinutes);
    }

    public async Task AddOutputAsync(Guid serverId, Guid organizationId, ContractsOutputType outputType, string content, long sequenceNumber, Guid? sessionId = null, CancellationToken ct = default)
    {
        // Add to Redis (hot storage)
        var key = GetHotStorageKey(serverId);
        var line = new ConsoleLine(content, outputType, DateTime.UtcNow, sequenceNumber);
        var lineJson = JsonSerializer.Serialize(line);

        // Use a list in Redis, pushing to the left (newest first)
        // This is a simplified implementation - in production you'd use Redis LPUSH directly
        var existing = await GetHotStorageLinesAsync(serverId, ct);
        var lines = existing.Prepend(line).Take(MaxHotStorageLines).ToList();

        var linesJson = JsonSerializer.Serialize(lines);
        await _cache.SetStringAsync(key, linesJson,
            new DistributedCacheEntryOptions { SlidingExpiration = _hotStorageTtl }, ct);
    }

    public async Task<IReadOnlyList<ConsoleLine>> GetRecentHistoryAsync(Guid serverId, int lineCount = 100, CancellationToken ct = default)
    {
        var lines = await GetHotStorageLinesAsync(serverId, ct);
        return lines.Take(lineCount).ToList();
    }

    public async Task<ConsoleHistorySearchResult> SearchHistoryAsync(SearchConsoleHistoryRequest request, CancellationToken ct = default)
    {
        var query = _db.ConsoleHistory
            .Where(h => h.ServerId == request.ServerId);

        if (request.StartTime.HasValue)
        {
            query = query.Where(h => h.Timestamp >= request.StartTime.Value);
        }

        if (request.EndTime.HasValue)
        {
            query = query.Where(h => h.Timestamp <= request.EndTime.Value);
        }

        if (request.OutputType.HasValue)
        {
            var dbType = (EntityOutputType)(int)request.OutputType.Value;
            query = query.Where(h => h.OutputType == dbType);
        }

        if (!string.IsNullOrEmpty(request.Query))
        {
            query = query.Where(h => h.Content.Contains(request.Query));
        }

        var totalCount = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(h => h.Timestamp)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(h => new ConsoleLine(
                h.Content,
                (ContractsOutputType)(int)h.OutputType,
                h.Timestamp,
                h.SequenceNumber))
            .ToListAsync(ct);

        return new ConsoleHistorySearchResult(
            entries,
            totalCount,
            request.Page,
            request.PageSize,
            totalCount > request.Page * request.PageSize);
    }

    public async Task ArchiveOldEntriesAsync(Guid serverId, CancellationToken ct = default)
    {
        var lines = await GetHotStorageLinesAsync(serverId, ct);
        if (lines.Count == 0) return;

        // Get the oldest entries that should be archived
        var entriesToArchive = lines.Skip(MaxHotStorageLines / 2).ToList();
        if (entriesToArchive.Count == 0) return;

        // Get organization ID from the first entry we have
        var existingEntry = await _db.ConsoleHistory
            .Where(h => h.ServerId == serverId)
            .Select(h => h.OrganizationId)
            .FirstOrDefaultAsync(ct);

        // Insert into PostgreSQL
        foreach (var line in entriesToArchive)
        {
            _db.ConsoleHistory.Add(new ConsoleHistoryEntry
            {
                ServerId = serverId,
                OrganizationId = existingEntry,
                OutputType = (EntityOutputType)(int)line.OutputType,
                Content = line.Content,
                Timestamp = line.Timestamp,
                SequenceNumber = line.SequenceNumber
            });
        }

        await _db.SaveChangesAsync(ct);

        // Keep only recent entries in Redis
        var recentLines = lines.Take(MaxHotStorageLines / 2).ToList();
        var key = GetHotStorageKey(serverId);
        var linesJson = JsonSerializer.Serialize(recentLines);
        await _cache.SetStringAsync(key, linesJson,
            new DistributedCacheEntryOptions { SlidingExpiration = _hotStorageTtl }, ct);
    }

    public async Task PurgeOldEntriesAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await _db.ConsoleHistory
            .Where(h => h.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<List<ConsoleLine>> GetHotStorageLinesAsync(Guid serverId, CancellationToken ct)
    {
        var key = GetHotStorageKey(serverId);
        var data = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(data))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ConsoleLine>>(data) ?? [];
    }

    private static string GetHotStorageKey(Guid serverId) => $"console:history:{serverId}";
}
