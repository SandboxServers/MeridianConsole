namespace Dhadgar.Contracts.Mods;

// Mod service MassTransit events

/// <summary>
/// Published when a new mod is created.
/// </summary>
public record ModCreated(
    Guid ModId,
    Guid? OrganizationId,
    string Name,
    string Slug,
    string GameType,
    bool IsPublic,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Published when a mod is deleted (soft delete).
/// </summary>
public record ModDeleted(
    Guid ModId,
    Guid? OrganizationId,
    string Name,
    DateTimeOffset DeletedAtUtc);

/// <summary>
/// Published when a mod is archived.
/// </summary>
public record ModArchived(
    Guid ModId,
    Guid? OrganizationId,
    string Name,
    DateTimeOffset ArchivedAtUtc);

/// <summary>
/// Published when a mod is unarchived.
/// </summary>
public record ModUnarchived(
    Guid ModId,
    Guid? OrganizationId,
    string Name,
    DateTimeOffset UnarchivedAtUtc);

/// <summary>
/// Published when a new mod version is published.
/// </summary>
public record ModVersionPublished(
    Guid ModId,
    Guid ModVersionId,
    Guid? OrganizationId,
    string ModName,
    string Version,
    bool IsPrerelease,
    bool IsLatest,
    DateTimeOffset PublishedAtUtc);

/// <summary>
/// Published when a mod version is deprecated.
/// </summary>
public record ModVersionDeprecated(
    Guid ModId,
    Guid ModVersionId,
    string ModName,
    string Version,
    string Reason,
    Guid? RecommendedVersionId,
    DateTimeOffset DeprecatedAtUtc);

/// <summary>
/// Published when an incompatibility is reported between mods.
/// </summary>
public record IncompatibilityReported(
    Guid ModVersionId,
    Guid IncompatibleWithModId,
    string ModName,
    string ModVersion,
    string IncompatibleModName,
    string? Reason,
    bool IsUserReported,
    DateTimeOffset ReportedAtUtc);

/// <summary>
/// Published when a mod is downloaded.
/// </summary>
public record ModDownloaded(
    Guid ModId,
    Guid ModVersionId,
    Guid? OrganizationId,
    Guid? NodeId,
    Guid? ServerId,
    string ModName,
    string Version,
    DateTimeOffset DownloadedAtUtc);

/// <summary>
/// Published when a mod's visibility changes (public/private).
/// </summary>
public record ModVisibilityChanged(
    Guid ModId,
    Guid? OrganizationId,
    string ModName,
    bool IsPublic,
    DateTimeOffset ChangedAtUtc);

/// <summary>
/// Published when a mod's download count crosses a threshold (for notifications).
/// </summary>
public record ModDownloadMilestone(
    Guid ModId,
    string ModName,
    long TotalDownloads,
    long Milestone,
    DateTimeOffset ReachedAtUtc);
