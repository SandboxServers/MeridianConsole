using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Identity Service API calls
/// </summary>
public interface IIdentityApi
{
    [Post("/connect/token")]
    [Headers("Content-Type: application/x-www-form-urlencoded")]
    Task<TokenResponse> GetTokenAsync(
        [Body(BodySerializationMethod.UrlEncoded)] Dictionary<string, string> request,
        CancellationToken ct = default);

    [Get("/organizations")]
    Task<List<OrganizationResponse>> GetOrganizationsAsync(CancellationToken ct = default);

    [Get("/organizations/{orgId}")]
    Task<OrganizationDetailResponse> GetOrganizationAsync(string orgId, CancellationToken ct = default);

    [Post("/organizations")]
    Task<OrganizationDetailResponse> CreateOrganizationAsync([Body] CreateOrganizationRequest request, CancellationToken ct = default);

    [Patch("/organizations/{orgId}")]
    Task<OrganizationDetailResponse> UpdateOrganizationAsync(string orgId, [Body] UpdateOrganizationRequest request, CancellationToken ct = default);

    [Delete("/organizations/{orgId}")]
    Task DeleteOrganizationAsync(string orgId, CancellationToken ct = default);

    [Post("/organizations/{orgId}/switch")]
    Task<SwitchOrganizationResponse> SwitchOrganizationAsync(string orgId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/members")]
    Task<List<MemberResponse>> GetMembersAsync(string orgId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/users")]
    Task<List<UserSummaryResponse>> GetUsersAsync(string orgId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/users/{userId}")]
    Task<UserDetailResponse> GetUserAsync(string orgId, string userId, CancellationToken ct = default);

    [Post("/organizations/{orgId}/users")]
    Task<UserDetailResponse> CreateUserAsync(string orgId, [Body] CreateUserRequest request, CancellationToken ct = default);

    [Patch("/organizations/{orgId}/users/{userId}")]
    Task<UserDetailResponse> UpdateUserAsync(string orgId, string userId, [Body] UpdateUserRequest request, CancellationToken ct = default);

    [Delete("/organizations/{orgId}/users/{userId}")]
    Task DeleteUserAsync(string orgId, string userId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/users/search")]
    Task<List<UserSummaryResponse>> SearchUsersAsync(
        string orgId,
        [AliasAs("query")] string query,
        CancellationToken ct = default);

    [Get("/organizations/search")]
    Task<List<OrganizationResponse>> SearchOrganizationsAsync(
        [AliasAs("query")] string query,
        CancellationToken ct = default);

    [Get("/organizations/{orgId}/roles")]
    Task<List<RoleSummaryResponse>> GetRolesAsync(string orgId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/roles/{roleId}")]
    Task<RoleSummaryResponse> GetRoleAsync(string orgId, string roleId, CancellationToken ct = default);

    [Post("/organizations/{orgId}/roles")]
    Task<RoleSummaryResponse> CreateRoleAsync(string orgId, [Body] CreateRoleRequest request, CancellationToken ct = default);

    [Post("/organizations/{orgId}/roles/{roleId}/assign")]
    Task<RoleAssignmentResponse> AssignRoleAsync(string orgId, string roleId, [Body] RoleAssignmentRequest request, CancellationToken ct = default);

    [Post("/organizations/{orgId}/roles/{roleId}/revoke")]
    Task<RoleAssignmentResponse> RevokeRoleAsync(string orgId, string roleId, [Body] RoleAssignmentRequest request, CancellationToken ct = default);

    [Get("/organizations/{orgId}/roles/search")]
    Task<List<RoleSummaryResponse>> SearchRolesAsync(
        string orgId,
        [AliasAs("query")] string query,
        CancellationToken ct = default);

    // Role update/delete/members endpoints
    [Patch("/organizations/{orgId}/roles/{roleId}")]
    Task<RoleSummaryResponse> UpdateRoleAsync(string orgId, string roleId, [Body] UpdateRoleRequest request, CancellationToken ct = default);

    [Delete("/organizations/{orgId}/roles/{roleId}")]
    Task DeleteRoleAsync(string orgId, string roleId, CancellationToken ct = default);

    [Get("/organizations/{orgId}/roles/{roleId}/members")]
    Task<List<RoleMemberResponse>> GetRoleMembersAsync(string orgId, string roleId, CancellationToken ct = default);

    // /me self-service endpoints
    [Get("/me")]
    Task<MeProfileResponse> GetMyProfileAsync(CancellationToken ct = default);

    [Patch("/me")]
    Task<MeProfileResponse> UpdateMyProfileAsync([Body] UpdateProfileRequest request, CancellationToken ct = default);

    [Get("/me/organizations")]
    Task<MyOrganizationsResponse> GetMyOrganizationsAsync(CancellationToken ct = default);

    [Get("/me/linked-accounts")]
    Task<MyLinkedAccountsResponse> GetMyLinkedAccountsAsync(CancellationToken ct = default);

    [Get("/me/permissions")]
    Task<MyPermissionsResponse> GetMyPermissionsAsync(CancellationToken ct = default);

    // Session management endpoints
    [Get("/me/sessions")]
    Task<List<SessionResponse>> GetMySessionsAsync(CancellationToken ct = default);

    [Delete("/me/sessions/{sessionId}")]
    Task RevokeSessionAsync(string sessionId, CancellationToken ct = default);

    [Post("/me/sessions/revoke-all")]
    Task<RevokeAllSessionsResponse> RevokeAllSessionsAsync(CancellationToken ct = default);

    [Post("/logout")]
    Task LogoutAsync(CancellationToken ct = default);
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";
}

public class OrganizationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; set; }
}

public class MemberResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("joinedAt")]
    public DateTime? JoinedAt { get; set; }
}

public class CreateOrganizationRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

public class UpdateOrganizationRequest
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Slug { get; set; }

    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrganizationSettingsUpdateRequest? Settings { get; set; }
}

public class OrganizationSettingsUpdateRequest
{
    [JsonPropertyName("customSettings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? CustomSettings { get; set; }
}

public class OrganizationDetailResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; set; } = string.Empty;

    [JsonPropertyName("settings")]
    public OrganizationSettingsResponse? Settings { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

public class OrganizationSettingsResponse
{
    [JsonPropertyName("allowMemberInvites")]
    public bool AllowMemberInvites { get; set; } = true;

    [JsonPropertyName("requireEmailVerification")]
    public bool RequireEmailVerification { get; set; } = true;

    [JsonPropertyName("maxMembers")]
    public int MaxMembers { get; set; } = 10;

    [JsonPropertyName("customSettings")]
    public Dictionary<string, string> CustomSettings { get; set; } = new();
}

public class SwitchOrganizationResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("permissions")]
    public Collection<string>? Permissions { get; set; }
}

public class UserSummaryResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("joinedAt")]
    public DateTime JoinedAt { get; set; }

    [JsonPropertyName("linkedProviders")]
    public Collection<string>? LinkedProviders { get; set; }
}

public class UserDetailResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("hasPasskeysRegistered")]
    public bool HasPasskeysRegistered { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("lastAuthenticatedAt")]
    public DateTime? LastAuthenticatedAt { get; set; }

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("joinedAt")]
    public DateTime JoinedAt { get; set; }

    [JsonPropertyName("linkedAccounts")]
    public Collection<LinkedAccountResponse>? LinkedAccounts { get; set; }
}

public class LinkedAccountResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("providerAccountId")]
    public string ProviderAccountId { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public LinkedAccountMetadataResponse? Metadata { get; set; }

    [JsonPropertyName("linkedAt")]
    public DateTime LinkedAt { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }
}

public class LinkedAccountMetadataResponse
{
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("extraData")]
    public Dictionary<string, string>? ExtraData { get; set; }
}

public class CreateUserRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

public class UpdateUserRequest
{
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

public class RoleSummaryResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isSystem")]
    public bool IsSystem { get; set; }

    [JsonPropertyName("permissions")]
    public Collection<string>? Permissions { get; set; }
}

public class CreateRoleRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("permissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Collection<string>? Permissions { get; set; }
}

public class RoleAssignmentRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
}

public class RoleAssignmentResponse
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

public class UpdateRoleRequest
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("permissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Collection<string>? Permissions { get; set; }
}

public class RoleMemberResponse
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("joinedAt")]
    public DateTime JoinedAt { get; set; }
}

public class MeProfileResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("preferredOrganizationId")]
    public string? PreferredOrganizationId { get; set; }

    [JsonPropertyName("hasPasskeysRegistered")]
    public bool HasPasskeysRegistered { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastAuthenticatedAt")]
    public DateTime? LastAuthenticatedAt { get; set; }
}

public class UpdateProfileRequest
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonPropertyName("preferredOrganizationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreferredOrganizationId { get; set; }
}

public class MyOrganizationsResponse
{
    [JsonPropertyName("organizations")]
    public Collection<MyOrganizationEntry> Organizations { get; set; } = [];
}

public class MyOrganizationEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("joinedAt")]
    public DateTime JoinedAt { get; set; }

    [JsonPropertyName("isPreferred")]
    public bool IsPreferred { get; set; }
}

public class MyLinkedAccountsResponse
{
    [JsonPropertyName("linkedAccounts")]
    public Collection<MyLinkedAccountEntry> LinkedAccounts { get; set; } = [];
}

public class MyLinkedAccountEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("providerDisplayName")]
    public string? ProviderDisplayName { get; set; }

    [JsonPropertyName("linkedAt")]
    public DateTime LinkedAt { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }
}

public class MyPermissionsResponse
{
    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("permissions")]
    public Collection<string> Permissions { get; set; } = [];
}

public class SessionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("deviceInfo")]
    public string? DeviceInfo { get; set; }

    [JsonPropertyName("issuedAt")]
    public DateTime IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; set; }
}

public class RevokeAllSessionsResponse
{
    [JsonPropertyName("revokedCount")]
    public int RevokedCount { get; set; }
}
