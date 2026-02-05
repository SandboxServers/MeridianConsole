using Dhadgar.Contracts.Mods;

namespace Dhadgar.Mods.Services;

public interface IModVersionService
{
    /// <summary>
    /// Gets a specific version of a mod.
    /// </summary>
    Task<ServiceResult<ModVersionDetail>> GetVersionAsync(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the latest version of a mod.
    /// </summary>
    Task<ServiceResult<ModVersionDetail>> GetLatestVersionAsync(
        Guid organizationId,
        Guid modId,
        bool includePrerelease = false,
        CancellationToken ct = default);

    /// <summary>
    /// Publishes a new version of a mod.
    /// </summary>
    Task<ServiceResult<ModVersionDetail>> PublishVersionAsync(
        Guid organizationId,
        Guid modId,
        PublishVersionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deprecates a version.
    /// </summary>
    Task<ServiceResult<bool>> DeprecateVersionAsync(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        DeprecateVersionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Finds versions matching a constraint.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<ModVersionSummary>>> FindVersionsMatchingAsync(
        Guid organizationId,
        Guid modId,
        string? constraint,
        CancellationToken ct = default);
}
