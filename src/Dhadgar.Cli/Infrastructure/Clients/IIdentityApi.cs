using Refit;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Identity Service API calls
/// </summary>
public interface IIdentityApi
{
    [Post("/connect/token")]
    Task<TokenResponse> GetTokenAsync([Body] TokenRequest request, CancellationToken ct = default);

    [Get("/organizations")]
    Task<List<OrganizationResponse>> GetOrganizationsAsync(CancellationToken ct = default);

    [Get("/organizations/{orgId}/members")]
    Task<List<MemberResponse>> GetMembersAsync(string orgId, CancellationToken ct = default);

    [Post("/organizations")]
    Task<OrganizationResponse> CreateOrganizationAsync([Body] CreateOrganizationRequest request, CancellationToken ct = default);
}

public class TokenRequest
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = "client_credentials";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "api://AzureADTokenExchange";
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
}

public class CreateOrganizationRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
