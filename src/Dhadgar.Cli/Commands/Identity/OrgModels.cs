using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Commands.Identity;

internal sealed class CreateOrganizationRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

internal sealed class UpdateOrganizationRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("settings")]
    public OrganizationSettingsResponse? Settings { get; set; }
}

internal sealed class OrganizationDetailResponse
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

internal sealed class OrganizationSettingsResponse
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
