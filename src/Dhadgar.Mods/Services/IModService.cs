using Dhadgar.Contracts;
using Dhadgar.Contracts.Mods;

namespace Dhadgar.Mods.Services;

public interface IModService
{
    /// <summary>
    /// Gets a paginated list of mods with optional filtering.
    /// </summary>
    Task<FilteredPagedResponse<ModListItem>> GetModsAsync(
        Guid? organizationId,
        ModSearchQuery query,
        CancellationToken ct = default);

    /// <summary>
    /// Gets detailed information about a specific mod.
    /// </summary>
    Task<ServiceResult<ModDetail>> GetModAsync(
        Guid modId,
        Guid? requestingOrgId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new mod.
    /// </summary>
    Task<ServiceResult<ModDetail>> CreateModAsync(
        Guid organizationId,
        CreateModRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a mod.
    /// </summary>
    Task<ServiceResult<ModDetail>> UpdateModAsync(
        Guid organizationId,
        Guid modId,
        UpdateModRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a mod (soft delete).
    /// </summary>
    Task<ServiceResult<bool>> DeleteModAsync(
        Guid organizationId,
        Guid modId,
        CancellationToken ct = default);
}
