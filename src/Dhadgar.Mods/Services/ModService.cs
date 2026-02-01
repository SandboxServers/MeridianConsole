using Dhadgar.Contracts;
using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.Mods.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Mods.Services;

public sealed class ModService : IModService
{
    private readonly ModsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModService> _logger;

    public ModService(
        ModsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ModService> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<FilteredPagedResponse<ModListItem>> GetModsAsync(
        Guid? organizationId,
        ModSearchQuery query,
        CancellationToken ct = default)
    {
        var queryable = _db.Mods.Where(m => !m.IsArchived);

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
            var search = query.Query.ToLowerInvariant();
            queryable = queryable.Where(m =>
                m.Name.ToLower().Contains(search) ||
                (m.Description != null && m.Description.ToLower().Contains(search)) ||
                (m.Author != null && m.Author.ToLower().Contains(search)));
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

        // Apply sorting
        queryable = query.SortBy.ToLowerInvariant() switch
        {
            "name" => query.SortOrder.ToLowerInvariant() == "desc"
                ? queryable.OrderByDescending(m => m.Name)
                : queryable.OrderBy(m => m.Name),
            "downloads" => query.SortOrder.ToLowerInvariant() == "asc"
                ? queryable.OrderBy(m => m.TotalDownloads)
                : queryable.OrderByDescending(m => m.TotalDownloads),
            "createdat" => query.SortOrder.ToLowerInvariant() == "desc"
                ? queryable.OrderByDescending(m => m.CreatedAt)
                : queryable.OrderBy(m => m.CreatedAt),
            "updatedat" => query.SortOrder.ToLowerInvariant() == "asc"
                ? queryable.OrderBy(m => m.UpdatedAt)
                : queryable.OrderByDescending(m => m.UpdatedAt),
            _ => queryable.OrderByDescending(m => m.TotalDownloads)
        };

        // Apply pagination
        var mods = await queryable
            .Include(m => m.Category)
            .Include(m => m.Versions.Where(v => v.IsLatest))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
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
            query.Page,
            query.PageSize);
    }

    public async Task<ServiceResult<ModDetail>> GetModAsync(
        Guid modId,
        Guid? requestingOrgId,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .Include(m => m.Category)
            .Include(m => m.Versions.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.Patch).Take(10))
            .FirstOrDefaultAsync(m => m.Id == modId, ct);

        if (mod is null)
        {
            return ServiceResult.Fail<ModDetail>("mod_not_found");
        }

        // Check access
        if (!mod.IsPublic && mod.OrganizationId != requestingOrgId)
        {
            return ServiceResult.Fail<ModDetail>("mod_not_found");
        }

        return ServiceResult.Ok(MapToDetail(mod));
    }

    public async Task<ServiceResult<ModDetail>> CreateModAsync(
        Guid organizationId,
        CreateModRequest request,
        CancellationToken ct = default)
    {
        // Check for duplicate slug
        var exists = await _db.Mods.AnyAsync(
            m => m.OrganizationId == organizationId && m.Slug == request.Slug, ct);

        if (exists)
        {
            return ServiceResult.Fail<ModDetail>("mod_slug_exists");
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
        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ModCreated(
            mod.Id,
            organizationId,
            mod.Name,
            mod.Slug,
            mod.GameType,
            mod.IsPublic,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Created mod {ModId} '{ModName}' for org {OrgId}",
            mod.Id, mod.Name, organizationId);

        return ServiceResult.Ok(MapToDetail(mod));
    }

    public async Task<ServiceResult<ModDetail>> UpdateModAsync(
        Guid organizationId,
        Guid modId,
        UpdateModRequest request,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .Include(m => m.Category)
            .Include(m => m.Versions.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.Patch).Take(10))
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return ServiceResult.Fail<ModDetail>("mod_not_found");
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

        await _db.SaveChangesAsync(ct);

        // Publish visibility change event if applicable
        if (request.IsPublic.HasValue && request.IsPublic.Value != oldIsPublic)
        {
            await _publishEndpoint.Publish(new ModVisibilityChanged(
                mod.Id,
                organizationId,
                mod.Name,
                mod.IsPublic,
                DateTimeOffset.UtcNow), ct);
        }

        _logger.LogInformation("Updated mod {ModId} for org {OrgId}", modId, organizationId);

        return ServiceResult.Ok(MapToDetail(mod));
    }

    public async Task<ServiceResult<bool>> DeleteModAsync(
        Guid organizationId,
        Guid modId,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return ServiceResult.Fail<bool>("mod_not_found");
        }

        mod.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ModDeleted(
            mod.Id,
            organizationId,
            mod.Name,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Deleted mod {ModId} for org {OrgId}", modId, organizationId);

        return ServiceResult.Ok(true);
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
