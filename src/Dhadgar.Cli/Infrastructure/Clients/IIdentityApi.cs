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

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
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
