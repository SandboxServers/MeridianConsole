using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.Mods.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Mods.Services;

public sealed class ModVersionService : IModVersionService
{
    private readonly ModsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModVersionService> _logger;

    public ModVersionService(
        ModsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ModVersionService> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<ServiceResult<ModVersionDetail>> GetVersionAsync(
        Guid modId,
        Guid versionId,
        CancellationToken ct = default)
    {
        var version = await _db.ModVersions
            .Include(v => v.Dependencies).ThenInclude(d => d.DependsOnMod)
            .Include(v => v.Incompatibilities).ThenInclude(i => i.IncompatibleWithMod)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ModId == modId, ct);

        if (version is null)
        {
            return ServiceResult.Fail<ModVersionDetail>("version_not_found");
        }

        return ServiceResult.Ok(MapToDetail(version));
    }

    public async Task<ServiceResult<ModVersionDetail>> GetLatestVersionAsync(
        Guid modId,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        var query = _db.ModVersions
            .Include(v => v.Dependencies).ThenInclude(d => d.DependsOnMod)
            .Include(v => v.Incompatibilities).ThenInclude(i => i.IncompatibleWithMod)
            .Where(v => v.ModId == modId && !v.IsDeprecated);

        if (!includePrerelease)
        {
            query = query.Where(v => !v.IsPrerelease);
        }

        var version = await query
            .OrderByDescending(v => v.Major)
            .ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.Patch)
            .ThenBy(v => v.Prerelease) // Stable before prerelease
            .FirstOrDefaultAsync(ct);

        if (version is null)
        {
            return ServiceResult.Fail<ModVersionDetail>("no_versions_found");
        }

        return ServiceResult.Ok(MapToDetail(version));
    }

    public async Task<ServiceResult<ModVersionDetail>> PublishVersionAsync(
        Guid organizationId,
        Guid modId,
        PublishVersionRequest request,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return ServiceResult.Fail<ModVersionDetail>("mod_not_found");
        }

        // Parse version
        if (!SemanticVersion.TryParse(request.Version, out var semver))
        {
            return ServiceResult.Fail<ModVersionDetail>("invalid_version_format");
        }

        // Check for duplicate version
        var exists = await _db.ModVersions.AnyAsync(
            v => v.ModId == modId && v.Version == request.Version, ct);

        if (exists)
        {
            return ServiceResult.Fail<ModVersionDetail>("version_already_exists");
        }

        // Unmark current latest
        var currentLatest = await _db.ModVersions
            .Where(v => v.ModId == modId && v.IsLatest && !v.IsPrerelease)
            .FirstOrDefaultAsync(ct);

        var version = new ModVersion
        {
            ModId = modId,
            Version = request.Version,
            Major = semver!.Major,
            Minor = semver.Minor,
            Patch = semver.Patch,
            Prerelease = semver.Prerelease,
            BuildMetadata = semver.BuildMetadata,
            ReleaseNotes = request.ReleaseNotes,
            FileHash = request.FileHash,
            FileSizeBytes = request.FileSizeBytes,
            FilePath = request.FilePath,
            MinGameVersion = request.MinGameVersion,
            MaxGameVersion = request.MaxGameVersion,
            IsPrerelease = request.IsPrerelease,
            IsLatest = !request.IsPrerelease, // Only mark stable releases as latest
            PublishedAt = DateTime.UtcNow
        };

        // Add dependencies
        if (request.Dependencies != null)
        {
            foreach (var dep in request.Dependencies)
            {
                version.Dependencies.Add(new ModDependency
                {
                    ModVersionId = version.Id,
                    DependsOnModId = dep.ModId,
                    MinVersion = dep.MinVersion,
                    MaxVersion = dep.MaxVersion,
                    IsOptional = dep.IsOptional
                });
            }
        }

        // Update latest flag
        if (version.IsLatest && currentLatest != null)
        {
            currentLatest.IsLatest = false;
        }

        _db.ModVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ModVersionPublished(
            modId,
            version.Id,
            organizationId,
            mod.Name,
            version.Version,
            version.IsPrerelease,
            version.IsLatest,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Published version {Version} for mod {ModId}",
            version.Version, modId);

        // Reload with navigation properties
        var result = await GetVersionAsync(modId, version.Id, ct);
        return result;
    }

    public async Task<ServiceResult<bool>> DeprecateVersionAsync(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        DeprecateVersionRequest request,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return ServiceResult.Fail<bool>("mod_not_found");
        }

        var version = await _db.ModVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ModId == modId, ct);

        if (version is null)
        {
            return ServiceResult.Fail<bool>("version_not_found");
        }

        version.IsDeprecated = true;
        version.DeprecationReason = request.Reason;

        // If this was the latest, find new latest
        if (version.IsLatest)
        {
            version.IsLatest = false;

            var newLatest = await _db.ModVersions
                .Where(v => v.ModId == modId && !v.IsDeprecated && !v.IsPrerelease && v.Id != versionId)
                .OrderByDescending(v => v.Major)
                .ThenByDescending(v => v.Minor)
                .ThenByDescending(v => v.Patch)
                .FirstOrDefaultAsync(ct);

            if (newLatest != null)
            {
                newLatest.IsLatest = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ModVersionDeprecated(
            modId,
            versionId,
            mod.Name,
            version.Version,
            request.Reason,
            request.RecommendedVersionId,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Deprecated version {Version} for mod {ModId}: {Reason}",
            version.Version, modId, request.Reason);

        return ServiceResult.Ok(true);
    }

    public async Task<IReadOnlyList<ModVersionSummary>> FindVersionsMatchingAsync(
        Guid modId,
        string? constraint,
        CancellationToken ct = default)
    {
        var versions = await _db.ModVersions
            .Where(v => v.ModId == modId && !v.IsDeprecated)
            .OrderByDescending(v => v.Major)
            .ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.Patch)
            .ToListAsync(ct);

        if (string.IsNullOrEmpty(constraint))
        {
            return versions.Select(v => new ModVersionSummary(
                v.Id, v.Version, v.IsPrerelease, v.DownloadCount, v.PublishedAt)).ToList();
        }

        var matching = versions
            .Where(v =>
            {
                if (!SemanticVersion.TryParse(v.Version, out var semver))
                    return false;
                return semver!.Satisfies(constraint);
            })
            .Select(v => new ModVersionSummary(
                v.Id, v.Version, v.IsPrerelease, v.DownloadCount, v.PublishedAt))
            .ToList();

        return matching;
    }

    private static ModVersionDetail MapToDetail(ModVersion version)
    {
        var dependencies = version.Dependencies
            .Select(d => new ModDependencyDto(
                d.DependsOnModId,
                d.DependsOnMod?.Name ?? "",
                d.DependsOnMod?.Slug ?? "",
                d.MinVersion,
                d.MaxVersion,
                d.IsOptional))
            .ToList();

        var incompatibilities = version.Incompatibilities
            .Select(i => new ModIncompatibilityDto(
                i.IncompatibleWithModId,
                i.IncompatibleWithMod?.Name ?? "",
                i.IncompatibleWithMod?.Slug ?? "",
                i.MinVersion,
                i.MaxVersion,
                i.Reason))
            .ToList();

        return new ModVersionDetail(
            version.Id,
            version.ModId,
            version.Version,
            version.Major,
            version.Minor,
            version.Patch,
            version.Prerelease,
            version.BuildMetadata,
            version.ReleaseNotes,
            version.FileHash,
            version.FileSizeBytes,
            version.MinGameVersion,
            version.MaxGameVersion,
            version.IsLatest,
            version.IsPrerelease,
            version.IsDeprecated,
            version.DeprecationReason,
            version.DownloadCount,
            version.PublishedAt,
            dependencies,
            incompatibilities,
            version.CreatedAt);
    }
}
