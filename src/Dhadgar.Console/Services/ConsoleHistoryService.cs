using Dhadgar.Console.Data;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Contracts.Console;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;
using ContractsOutputType = Dhadgar.Contracts.Console.ConsoleOutputType;
using EntityOutputType = Dhadgar.Console.Data.Entities.ConsoleOutputType;

namespace Dhadgar.Console.Services;

/// <summary>
/// Hot storage data structure that includes metadata for proper archival.
/// </summary>
internal sealed record HotStorageData(Guid OrganizationId, List<ConsoleLine> Lines);

public sealed class ConsoleHistoryService : IConsoleHistoryService
{
    private readonly ConsoleDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ConsoleOptions _options;
    private readonly TimeSpan _hotStorageTtl;
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _lockExpiry = TimeSpan.FromSeconds(30);
    private const int MaxHotStorageLines = 500;

    // Lua script for atomic compare-and-delete (release lock only if we own it)
    private const string ReleaseLockScript = """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    private readonly TimeProvider _timeProvider;

    public ConsoleHistoryService(
        ConsoleDbContext db,
        IDistributedCache cache,
        IConnectionMultiplexer redis,
        IOptions<ConsoleOptions> options,
        TimeProvider timeProvider)
    {
        _db = db;
        _cache = cache;
        _redis = redis;
        _options = options.Value;
        _hotStorageTtl = TimeSpan.FromMinutes(_options.HotStorageTtlMinutes);
        _timeProvider = timeProvider;
    }

    public async Task AddOutputAsync(Guid serverId, Guid organizationId, ContractsOutputType outputType, string content, long sequenceNumber, CancellationToken ct = default)
    {
        var key = GetHotStorageKey(serverId);
        var lockKey = GetLockKey(serverId);
        var line = new ConsoleLine(content, outputType, _timeProvider.GetUtcNow().UtcDateTime, sequenceNumber);

        // Acquire distributed lock to prevent race conditions during read-modify-write
        var lockValue = await TryAcquireLockAsync(lockKey, ct);
        if (lockValue is null)
        {
            // Lock acquisition failed - log and continue (best effort)
            return;
        }

        try
        {
            var data = await GetHotStorageDataAsync(serverId, ct);
            var lines = data?.Lines ?? [];
            var orgId = data?.OrganizationId ?? organizationId;

            // Prepend new line and truncate to max size
            var updatedLines = lines.Prepend(line).Take(MaxHotStorageLines).ToList();
            var updatedData = new HotStorageData(orgId, updatedLines);

            var json = JsonSerializer.Serialize(updatedData);
            await _cache.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { SlidingExpiration = _hotStorageTtl }, ct);
        }
        finally
        {
            await ReleaseLockAsync(lockKey, lockValue);
        }
    }

    public async Task<IReadOnlyList<ConsoleLine>> GetRecentHistoryAsync(Guid serverId, int lineCount = 100, CancellationToken ct = default)
    {
        var data = await GetHotStorageDataAsync(serverId, ct);
        return data?.Lines.Take(lineCount).ToList() ?? [];
    }

    public async Task<ConsoleHistorySearchResult> SearchHistoryAsync(Guid organizationId, SearchConsoleHistoryRequest request, CancellationToken ct = default)
    {
        var query = _db.ConsoleHistory
            .Where(h => h.ServerId == request.ServerId && h.OrganizationId == organizationId);

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

    public async Task ArchiveOldEntriesAsync(Guid serverId, Guid organizationId, CancellationToken ct = default)
    {
        var lockKey = GetLockKey(serverId);

        // Acquire lock to ensure consistency during archival
        var lockValue = await TryAcquireLockAsync(lockKey, ct);
        if (lockValue is null)
        {
            return; // Another archival may be in progress
        }

        try
        {
            var data = await GetHotStorageDataAsync(serverId, ct);
            if (data is null || data.Lines.Count == 0) return;

            // Use organizationId from hot storage (authoritative) or fall back to parameter
            var actualOrgId = data.OrganizationId;

            // Get the oldest entries that should be archived
            var entriesToArchive = data.Lines.Skip(MaxHotStorageLines / 2).ToList();
            if (entriesToArchive.Count == 0) return;

            // Insert into PostgreSQL
            foreach (var line in entriesToArchive)
            {
                _db.ConsoleHistory.Add(new ConsoleHistoryEntry
                {
                    ServerId = serverId,
                    OrganizationId = actualOrgId,
                    OutputType = (EntityOutputType)(int)line.OutputType,
                    Content = line.Content,
                    Timestamp = line.Timestamp,
                    SequenceNumber = line.SequenceNumber
                });
            }

            await _db.SaveChangesAsync(ct);

            // Keep only recent entries in Redis
            var recentLines = data.Lines.Take(MaxHotStorageLines / 2).ToList();
            var key = GetHotStorageKey(serverId);
            var updatedData = new HotStorageData(actualOrgId, recentLines);
            var json = JsonSerializer.Serialize(updatedData);
            await _cache.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { SlidingExpiration = _hotStorageTtl }, ct);
        }
        finally
        {
            await ReleaseLockAsync(lockKey, lockValue);
        }
    }

    public async Task PurgeOldEntriesAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        await _db.ConsoleHistory
            .Where(h => h.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<HotStorageData?> GetHotStorageDataAsync(Guid serverId, CancellationToken ct)
    {
        var key = GetHotStorageKey(serverId);
        var json = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        // Support both old format (List<ConsoleLine>) and new format (HotStorageData)
        // for backward compatibility during migration
        try
        {
            return JsonSerializer.Deserialize<HotStorageData>(json);
        }
        catch (JsonException)
        {
            // Try old format
            var lines = JsonSerializer.Deserialize<List<ConsoleLine>>(json) ?? [];
            return lines.Count > 0 ? new HotStorageData(Guid.Empty, lines) : null;
        }
    }

    private async Task<string?> TryAcquireLockAsync(string lockKey, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var lockValue = Guid.NewGuid().ToString();
        var deadline = _timeProvider.GetUtcNow().UtcDateTime.Add(_lockTimeout);

        while (_timeProvider.GetUtcNow().UtcDateTime < deadline)
        {
            // Atomic SET NX with expiry — only succeeds if key doesn't exist
            var acquired = await db.StringSetAsync(lockKey, lockValue, _lockExpiry, When.NotExists);
            if (acquired)
            {
                return lockValue;
            }

            await Task.Delay(50, ct);
        }

        return null;
    }

    private async Task ReleaseLockAsync(string lockKey, string lockValue)
    {
        var db = _redis.GetDatabase();
        // Atomic compare-and-delete: only release if we still own the lock
        await db.ScriptEvaluateAsync(ReleaseLockScript,
            [(RedisKey)lockKey],
            [(RedisValue)lockValue]);
    }

    private static string GetHotStorageKey(Guid serverId) => $"console:history:{serverId}";
    private static string GetLockKey(Guid serverId) => $"console:history:lock:{serverId}";
}
