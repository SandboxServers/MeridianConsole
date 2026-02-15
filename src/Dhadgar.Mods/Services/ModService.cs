using Dhadgar.Contracts;
using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.Mods.Data.Entities;
using Dhadgar.Shared.Data;
using Dhadgar.Shared.Results;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Mods.Services;

public sealed class ModService : IModService
{
    private readonly ModsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModService> _logger;
    private readonly TimeProvider _timeProvider;

    public ModService(
        ModsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ModService> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<FilteredPagedResponse<ModListItem>> GetModsAsync(
        Guid? organizationId,
        ModSearchQuery query,
        CancellationToken ct = default)
    {
        // Clamp pagination to valid ranges
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var queryable = _db.Mods.Where(m => !m.IsArchived && m.DeletedAt == null);

        // Filter by organization or public
        if (organizationId.HasValue)
        {
            if (query.IsPublic == true)
            {
                queryable = queryable.Where(m => m.IsPublic);
            }
            else
            {
                queryable = queryable.Where(m => m.OrganizationId == organizationId.Value || m.IsPublic);
            }
        }
        else
        {
            queryable = queryable.Where(m => m.IsPublic);
        }

        // Apply filters
        if (!string.IsNullOrEmpty(query.GameType))
        {
            queryable = queryable.Where(m => m.GameType == query.GameType);
        }

        if (query.CategoryId.HasValue)
        {
            queryable = queryable.Where(m => m.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrEmpty(query.Query))
        {
            var escapedQuery = DatabaseHelpers.EscapeLikePattern(query.Query);
            var searchPattern = $"%{escapedQuery}%";
            queryable = queryable.Where(m =>
                EF.Functions.ILike(m.Name, searchPattern) ||
                (m.Description != null && EF.Functions.ILike(m.Description, searchPattern)) ||
                (m.Author != null && EF.Functions.ILike(m.Author, searchPattern)));
        }

        if (!string.IsNullOrEmpty(query.Tags))
        {
            var tags = query.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tag in tags)
            {
                queryable = queryable.Where(m => m.Tags.Contains(tag));
            }
        }

        var totalCount = await queryable.CountAsync(ct);

        // Apply sorting (handle null sortBy/sortOrder safely)
        var sortBy = query.SortBy?.ToLowerInvariant() ?? "downloads";
        var sortOrder = query.SortOrder?.ToLowerInvariant() ?? "desc";
        queryable = sortBy switch
        {
            "name" => sortOrder == "desc"
                ? queryable.OrderByDescending(m => m.Name)
                : queryable.OrderBy(m => m.Name),
            "downloads" => sortOrder == "asc"
                ? queryable.OrderBy(m => m.TotalDownloads)
                : queryable.OrderByDescending(m => m.TotalDownloads),
            "createdat" => sortOrder == "desc"
                ? queryable.OrderByDescending(m => m.CreatedAt)
                : queryable.OrderBy(m => m.CreatedAt),
            "updatedat" => sortOrder == "asc"
                ? queryable.OrderBy(m => m.UpdatedAt)
                : queryable.OrderByDescending(m => m.UpdatedAt),
            _ => queryable.OrderByDescending(m => m.TotalDownloads)
        };

        // Apply pagination
        var mods = await queryable
            .Include(m => m.Category)
            .Include(m => m.Versions.Where(v => v.IsLatest))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ModListItem(
                m.Id,
                m.Name,
                m.Slug,
                m.Description,
                m.Author,
                m.GameType,
                m.Category != null ? m.Category.Name : null,
                m.TotalDownloads,
                m.IsPublic,
                m.IconUrl,
                m.Tags,
                m.Versions.Where(v => v.IsLatest).Select(v => new ModVersionSummary(
                    v.Id,
                    v.Version,
                    v.IsPrerelease,
                    v.DownloadCount,
                    v.PublishedAt)).FirstOrDefault(),
                m.CreatedAt,
                m.UpdatedAt))
            .ToListAsync(ct);

        return FilteredPagedResponse<ModListItem>.Create(
            mods,
            totalCount,
            page,
            pageSize);
    }

    public async Task<Result<ModDetail>> GetModAsync(
        Guid modId,
        Guid? requestingOrgId,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .Include(m => m.Category)
            .Include(m => m.Versions.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.Patch).Take(10))
            .FirstOrDefaultAsync(m => m.Id == modId && m.DeletedAt == null, ct);

        if (mod is null)
        {
            return Result<ModDetail>.Failure("mod_not_found");
        }

        // Check access
        if (!mod.IsPublic && mod.OrganizationId != requestingOrgId)
        {
            return Result<ModDetail>.Failure("mod_not_found");
        }

        return Result<ModDetail>.Success(MapToDetail(mod));
    }

    public async Task<Result<ModDetail>> CreateModAsync(
        Guid organizationId,
        CreateModRequest request,
        CancellationToken ct = default)
    {
        // Check for duplicate slug
        var exists = await _db.Mods.AnyAsync(
            m => m.OrganizationId == organizationId && m.Slug == request.Slug, ct);

        if (exists)
        {
            return Result<ModDetail>.Failure("mod_slug_exists");
        }

        var mod = new Mod
        {
            OrganizationId = organizationId,
            Name = request.Name,
            Slug = request.Slug,
            Description = request.Description,
            Author = request.Author,
            CategoryId = request.CategoryId,
            GameType = request.GameType,
            IsPublic = request.IsPublic,
            ProjectUrl = request.ProjectUrl,
            IconUrl = request.IconUrl,
            Tags = request.Tags?.ToList() ?? []
        };

        _db.Mods.Add(mod);

        // Publish event before save so the outbox captures it in the same transaction
        await _publishEndpoint.Publish(new ModCreated(
            mod.Id,
            organizationId,
            mod.Name,
            mod.Slug,
            mod.GameType,
            mod.IsPublic,
            _timeProvider.GetUtcNow()), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseHelpers.IsUniqueConstraintViolation(ex))
        {
            return Result<ModDetail>.Failure("mod_slug_exists");
        }

        _logger.LogInformation("Created mod {ModId} '{ModName}' for org {OrgId}",
            mod.Id, mod.Name, organizationId);

        return Result<ModDetail>.Success(MapToDetail(mod));
    }

    public async Task<Result<ModDetail>> UpdateModAsync(
        Guid organizationId,
        Guid modId,
        UpdateModRequest request,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .Include(m => m.Category)
            .Include(m => m.Versions.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.Patch).Take(10))
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId && m.DeletedAt == null, ct);

        if (mod is null)
        {
            return Result<ModDetail>.Failure("mod_not_found");
        }

        var oldIsPublic = mod.IsPublic;

        if (request.Name != null) mod.Name = request.Name;
        if (request.Description != null) mod.Description = request.Description;
        if (request.Author != null) mod.Author = request.Author;
        if (request.CategoryId.HasValue) mod.CategoryId = request.CategoryId.Value;
        if (request.IsPublic.HasValue) mod.IsPublic = request.IsPublic.Value;
        if (request.IsArchived.HasValue) mod.IsArchived = request.IsArchived.Value;
        if (request.ProjectUrl != null) mod.ProjectUrl = request.ProjectUrl;
        if (request.IconUrl != null) mod.IconUrl = request.IconUrl;
        if (request.Tags != null) mod.Tags = request.Tags.ToList();

        // Publish visibility change event before save so the outbox captures it in the same transaction
        if (request.IsPublic.HasValue && request.IsPublic.Value != oldIsPublic)
        {
            await _publishEndpoint.Publish(new ModVisibilityChanged(
                mod.Id,
                organizationId,
                mod.Name,
                mod.IsPublic,
                _timeProvider.GetUtcNow()), ct);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated mod {ModId} for org {OrgId}", modId, organizationId);

        return Result<ModDetail>.Success(MapToDetail(mod));
    }

    public async Task<Result<bool>> DeleteModAsync(
        Guid organizationId,
        Guid modId,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return Result<bool>.Failure("mod_not_found");
        }

        if (mod.DeletedAt is not null)
        {
            return Result<bool>.Success(true); // already deleted, idempotent
        }

        mod.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;

        // Publish event before save so the outbox captures it in the same transaction
        await _publishEndpoint.Publish(new ModDeleted(
            mod.Id,
            organizationId,
            mod.Name,
            _timeProvider.GetUtcNow()), ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted mod {ModId} for org {OrgId}", modId, organizationId);

        return Result<bool>.Success(true);
    }

    private static ModDetail MapToDetail(Mod mod)
    {
        var versions = mod.Versions
            .Select(v => new ModVersionSummary(
                v.Id,
                v.Version,
                v.IsPrerelease,
                v.DownloadCount,
                v.PublishedAt))
            .ToList();

        return new ModDetail(
            mod.Id,
            mod.OrganizationId,
            mod.Name,
            mod.Slug,
            mod.Description,
            mod.Author,
            mod.CategoryId,
            mod.Category?.Name,
            mod.GameType,
            mod.TotalDownloads,
            mod.IsPublic,
            mod.IsArchived,
            mod.ProjectUrl,
            mod.IconUrl,
            mod.Tags,
            versions,
            mod.CreatedAt,
            mod.UpdatedAt);
    }
}
