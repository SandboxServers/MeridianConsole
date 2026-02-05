// CA1054: URI parameters should be Uri, not string.
// Suppressed for DTOs because JSON serialization works better with strings,
// and API contracts commonly use string URLs for interoperability.
#pragma warning disable CA1054

namespace Dhadgar.Contracts.Mods;

// Mods service contracts

// List/Summary DTOs

public record ModListItem(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? Author,
    string GameType,
    string? CategoryName,
    long TotalDownloads,
    bool IsPublic,
    string? IconUrl,
    IReadOnlyList<string> Tags,
    ModVersionSummary? LatestVersion,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ModVersionSummary(
    Guid Id,
    string Version,
    bool IsPrerelease,
    long DownloadCount,
    DateTime? PublishedAt);

// Detail DTOs

public record ModDetail(
    Guid Id,
    Guid? OrganizationId,
    string Name,
    string Slug,
    string? Description,
    string? Author,
    Guid? CategoryId,
    string? CategoryName,
    string GameType,
    long TotalDownloads,
    bool IsPublic,
    bool IsArchived,
    string? ProjectUrl,
    string? IconUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ModVersionSummary> Versions,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ModVersionDetail(
    Guid Id,
    Guid ModId,
    string Version,
    int Major,
    int Minor,
    int Patch,
    string? Prerelease,
    string? BuildMetadata,
    string? ReleaseNotes,
    string? FileHash,
    long FileSizeBytes,
    string? MinGameVersion,
    string? MaxGameVersion,
    bool IsLatest,
    bool IsPrerelease,
    bool IsDeprecated,
    string? DeprecationReason,
    long DownloadCount,
    DateTime? PublishedAt,
    IReadOnlyList<ModDependencyDto> Dependencies,
    IReadOnlyList<ModIncompatibilityDto> Incompatibilities,
    DateTime CreatedAt);

public record ModDependencyDto(
    Guid ModId,
    string ModName,
    string ModSlug,
    string? MinVersion,
    string? MaxVersion,
    bool IsOptional);

public record ModIncompatibilityDto(
    Guid ModId,
    string ModName,
    string ModSlug,
    string? MinVersion,
    string? MaxVersion,
    string? Reason);

// Request DTOs

public record CreateModRequest(
    string Name,
    string Slug,
    string? Description,
    string? Author,
    Guid? CategoryId,
    string GameType,
    bool IsPublic,
    string? ProjectUrl,
    string? IconUrl,
    IReadOnlyList<string>? Tags);

public record UpdateModRequest(
    string? Name,
    string? Description,
    string? Author,
    Guid? CategoryId,
    bool? IsPublic,
    bool? IsArchived,
    string? ProjectUrl,
    string? IconUrl,
    IReadOnlyList<string>? Tags);

public record PublishVersionRequest(
    string Version,
    string? ReleaseNotes,
    string? FileHash,
    long FileSizeBytes,
    string? FilePath,
    string? MinGameVersion,
    string? MaxGameVersion,
    bool IsPrerelease,
    IReadOnlyList<AddDependencyRequest>? Dependencies);

public record AddDependencyRequest(
    Guid ModId,
    string? MinVersion,
    string? MaxVersion,
    bool IsOptional);

public record ReportIncompatibilityRequest(
    Guid ModVersionId,
    Guid IncompatibleWithModId,
    string? MinVersion,
    string? MaxVersion,
    string? Reason);

public record DeprecateVersionRequest(
    string Reason,
    Guid? RecommendedVersionId);

public record ModSearchQuery(
    string? Query = null,
    string? GameType = null,
    Guid? CategoryId = null,
    string? Tags = null,
    bool? IsPublic = null,
    string SortBy = "downloads",
    string SortOrder = "desc",
    int Page = 1,
    int PageSize = 20);

public record DependencyResolutionRequest(
    IReadOnlyList<ModVersionReference> RequestedMods,
    bool IncludeOptional = false);

public record ModVersionReference(
    Guid ModId,
    string? VersionConstraint = null);

// Resolution Response

public record DependencyResolutionResult(
    bool Success,
    IReadOnlyList<ResolvedMod>? ResolvedMods,
    IReadOnlyList<DependencyConflict>? Conflicts,
    IReadOnlyList<string>? Warnings);

public record ResolvedMod(
    Guid ModId,
    string ModName,
    Guid VersionId,
    string Version,
    bool IsOptional);

public record DependencyConflict(
    string Description,
    Guid ModId,
    string ModName,
    string? RequestedVersion,
    string? ConflictingVersion);

// Download

public record ModDownloadUrl(
    Guid ModVersionId,
    string Url,
    DateTime ExpiresAt,
    string? FileHash,
    long FileSizeBytes);

// Category

public record ModCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? Icon,
    int SortOrder,
    Guid? ParentId,
    IReadOnlyList<ModCategoryDto>? Children);
