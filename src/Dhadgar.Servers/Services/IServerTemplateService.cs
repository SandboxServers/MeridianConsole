using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;

namespace Dhadgar.Servers.Services;

public interface IServerTemplateService
{
    /// <summary>
    /// Gets a paginated list of templates.
    /// </summary>
    Task<PagedResponse<ServerTemplateListItem>> GetTemplatesAsync(
        Guid? organizationId,
        bool includePublic,
        string? gameType,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    Task<ServiceResult<ServerTemplateDetail>> GetTemplateAsync(
        Guid templateId,
        Guid? organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new template.
    /// </summary>
    Task<ServiceResult<ServerTemplateDetail>> CreateTemplateAsync(
        Guid organizationId,
        CreateServerTemplateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a template.
    /// </summary>
    Task<ServiceResult<ServerTemplateDetail>> UpdateTemplateAsync(
        Guid organizationId,
        Guid templateId,
        UpdateServerTemplateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a template (soft delete).
    /// </summary>
    Task<ServiceResult<bool>> DeleteTemplateAsync(
        Guid organizationId,
        Guid templateId,
        CancellationToken ct = default);
}
