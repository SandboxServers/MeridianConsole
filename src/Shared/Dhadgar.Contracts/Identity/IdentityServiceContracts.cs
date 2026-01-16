namespace Dhadgar.Contracts.Identity;

/// <summary>
/// Service client interface for Identity service communication.
/// Used by other microservices to interact with Identity.
/// Implementations should use HTTP client to call Identity's /internal/* endpoints.
/// </summary>
public interface IIdentityServiceClient
{
    /// <summary>
    /// Get user information by ID.
    /// </summary>
    Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Get multiple users by their IDs (batch lookup).
    /// </summary>
    Task<Dictionary<Guid, UserInfo>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default);

    /// <summary>
    /// Get organization information by ID.
    /// </summary>
    Task<OrganizationInfo?> GetOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Check if an organization exists.
    /// </summary>
    Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Get all active members of an organization.
    /// </summary>
    Task<IReadOnlyCollection<OrganizationMemberInfo>> GetOrganizationMembersAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a user has a specific permission in an organization.
    /// </summary>
    Task<bool> UserHasPermissionAsync(
        Guid userId,
        Guid organizationId,
        string permission,
        CancellationToken ct = default);

    /// <summary>
    /// Get all permissions for a user in an organization.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get a user's membership info in an organization.
    /// </summary>
    Task<MembershipInfo?> GetMembershipAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);
}

/// <summary>
/// Basic user information for service-to-service communication.
/// </summary>
public sealed record UserInfo(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive);

/// <summary>
/// Basic organization information for service-to-service communication.
/// </summary>
public sealed record OrganizationInfo(
    Guid Id,
    string Name,
    string Slug,
    Guid OwnerId,
    bool IsActive);

/// <summary>
/// Organization member information.
/// </summary>
public sealed record OrganizationMemberInfo(
    Guid UserId,
    string Role,
    bool IsActive);

/// <summary>
/// User membership information in an organization.
/// </summary>
public sealed record MembershipInfo(
    Guid UserId,
    Guid OrganizationId,
    string? Role,
    bool IsActive,
    DateTime JoinedAt);
