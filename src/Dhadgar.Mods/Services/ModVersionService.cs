using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.Mods.Data.Entities;
using Dhadgar.Shared.Results;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Mods.Services;

public sealed class ModVersionService : IModVersionService
{
    private readonly ModsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModVersionService> _logger;
    private readonly TimeProvider _timeProvider;

    public ModVersionService(
        ModsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ModVersionService> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ModVersionDetail>> GetVersionAsync(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        CancellationToken ct = default)
    {
        // Validate mod belongs to organization
        var modExists = await _db.Mods.AnyAsync(
            m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (!modExists)
        {
            return Result<ModVersionDetail>.Failure("mod_not_found");
        }

        var version = await _db.ModVersions
            .Include(v => v.Dependencies).ThenInclude(d => d.DependsOnMod)
            .Include(v => v.Incompatibilities).ThenInclude(i => i.IncompatibleWithMod)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ModId == modId, ct);

        if (version is null)
        {
            return Result<ModVersionDetail>.Failure("version_not_found");
        }

        return Result<ModVersionDetail>.Success(MapToDetail(version));
    }

    public async Task<Result<ModVersionDetail>> GetLatestVersionAsync(
        Guid organizationId,
        Guid modId,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        // Validate mod belongs to organization
        var modExists = await _db.Mods.AnyAsync(
            m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (!modExists)
        {
            return Result<ModVersionDetail>.Failure("mod_not_found");
        }

        var query = _db.ModVersions
            .Include(v => v.Dependencies).ThenInclude(d => d.DependsOnMod)
            .Include(v => v.Incompatibilities).ThenInclude(i => i.IncompatibleWithMod)
            .Where(v => v.ModId == modId && !v.IsDeprecated);

        if (!includePrerelease)
        {
            query = query.Where(v => !v.IsPrerelease);
        }

        // Fetch all versions and sort in memory using proper SemVer comparison
        var versions = await query.ToListAsync(ct);

        if (versions.Count == 0)
        {
            return Result<ModVersionDetail>.Failure("no_versions_found");
        }

        // Sort using SemanticVersion for proper prerelease ordering
        var latest = versions
            .Select(v => (Version: v, SemVer: SemanticVersion.TryParse(v.Version, out var sv) ? sv : null))
            .Where(x => x.SemVer is not null)
            .OrderByDescending(x => x.SemVer)
            .Select(x => x.Version)
            .FirstOrDefault();

        if (latest is null)
        {
            // Fallback to database ordering if all versions fail to parse
            latest = versions
                .OrderByDescending(v => v.Major)
                .ThenByDescending(v => v.Minor)
                .ThenByDescending(v => v.Patch)
                .First();
        }

        return Result<ModVersionDetail>.Success(MapToDetail(latest));
    }

    public async Task<Result<ModVersionDetail>> PublishVersionAsync(
        Guid organizationId,
        Guid modId,
        PublishVersionRequest request,
        CancellationToken ct = default)
    {
        var mod = await _db.Mods
            .FirstOrDefaultAsync(m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (mod is null)
        {
            return Result<ModVersionDetail>.Failure("mod_not_found");
        }

        // Parse version
        if (!SemanticVersion.TryParse(request.Version, out var semver))
        {
            return Result<ModVersionDetail>.Failure("invalid_version_format");
        }

        // Check for duplicate version
        var exists = await _db.ModVersions.AnyAsync(
            v => v.ModId == modId && v.Version == request.Version, ct);

        if (exists)
        {
            return Result<ModVersionDetail>.Failure("version_already_exists");
        }

        // Get current latest to compare versions
        var currentLatest = await _db.ModVersions
            .Where(v => v.ModId == modId && v.IsLatest && !v.IsPrerelease)
            .FirstOrDefaultAsync(ct);

        // Determine if this version should be marked as latest
        // Only stable releases can be latest, and only if they're newer than current latest
        var shouldBeLatest = !request.IsPrerelease;
        if (shouldBeLatest && currentLatest != null)
        {
            // Parse current latest version and compare
            if (SemanticVersion.TryParse(currentLatest.Version, out var currentLatestSemver))
            {
                // Only mark as latest if new version is greater than current
                shouldBeLatest = semver > currentLatestSemver;
            }
        }

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
            IsLatest = shouldBeLatest,
            PublishedAt = _timeProvider.GetUtcNow().UtcDateTime
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

        // Update latest flag - only demote current latest if new version is newer
        if (version.IsLatest && currentLatest != null)
        {
            currentLatest.IsLatest = false;
        }

        _db.ModVersions.Add(version);

        // Publish event before save so the outbox captures it in the same transaction
        await _publishEndpoint.Publish(new ModVersionPublished(
            modId,
            version.Id,
            organizationId,
            mod.Name,
            version.Version,
            version.IsPrerelease,
            version.IsLatest,
            _timeProvider.GetUtcNow()), ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Published version {Version} for mod {ModId}",
            version.Version, modId);

        // Reload with navigation properties
        var result = await GetVersionAsync(organizationId, modId, version.Id, ct);
        return result;
    }

    public async Task<Result<bool>> DeprecateVersionAsync(
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
            return Result<bool>.Failure("mod_not_found");
        }

        var version = await _db.ModVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ModId == modId, ct);

        if (version is null)
        {
            return Result<bool>.Failure("version_not_found");
        }

        version.IsDeprecated = true;
        version.DeprecationReason = request.Reason;

        // If this was the latest, find new latest using proper SemVer comparison
        if (version.IsLatest)
        {
            version.IsLatest = false;

            var candidates = await _db.ModVersions
                .Where(v => v.ModId == modId && !v.IsDeprecated && !v.IsPrerelease && v.Id != versionId)
                .ToListAsync(ct);

            var newLatest = candidates
                .Select(v => (Version: v, SemVer: SemanticVersion.TryParse(v.Version, out var sv) ? sv : null))
                .Where(x => x.SemVer is not null)
                .OrderByDescending(x => x.SemVer)
                .Select(x => x.Version)
                .FirstOrDefault();

            if (newLatest != null)
            {
                newLatest.IsLatest = true;
            }
        }

        // Publish event before save so the outbox captures it in the same transaction
        await _publishEndpoint.Publish(new ModVersionDeprecated(
            modId,
            versionId,
            mod.Name,
            version.Version,
            request.Reason,
            request.RecommendedVersionId,
            _timeProvider.GetUtcNow()), ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deprecated version {Version} for mod {ModId}: {Reason}",
            version.Version, modId, request.Reason);

        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<ModVersionSummary>>> FindVersionsMatchingAsync(
        Guid organizationId,
        Guid modId,
        string? constraint,
        CancellationToken ct = default)
    {
        // Validate mod belongs to organization
        var modExists = await _db.Mods.AnyAsync(
            m => m.Id == modId && m.OrganizationId == organizationId, ct);

        if (!modExists)
        {
            return Result<IReadOnlyList<ModVersionSummary>>.Failure("mod_not_found");
        }

        var versions = await _db.ModVersions
            .Where(v => v.ModId == modId && !v.IsDeprecated)
            .OrderByDescending(v => v.Major)
            .ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.Patch)
            .ToListAsync(ct);

        if (string.IsNullOrEmpty(constraint))
        {
            var allVersions = versions.Select(v => new ModVersionSummary(
                v.Id, v.Version, v.IsPrerelease, v.DownloadCount, v.PublishedAt)).ToList();
            return Result<IReadOnlyList<ModVersionSummary>>.Success(allVersions);
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

        return Result<IReadOnlyList<ModVersionSummary>>.Success(matching);
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
