using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Configuration;

public sealed class CliConfig
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dhadgar");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("gateway_url")]
    public string? GatewayUrl { get; set; }

    [JsonPropertyName("identity_url")]
    public string? IdentityUrl { get; set; }

    [JsonPropertyName("secrets_url")]
    public string? SecretsUrl { get; set; }

    [JsonPropertyName("notifications_url")]
    public string? NotificationsUrl { get; set; }

    [JsonPropertyName("discord_url")]
    public string? DiscordUrl { get; set; }

    [JsonPropertyName("admin_api_key")]
    public string? AdminApiKey { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("current_org_id")]
    public string? CurrentOrgId { get; set; }

    [JsonPropertyName("token_expires_at")]
    public DateTime? TokenExpiresAt { get; set; }

    // Computed properties for service URLs (all behind gateway)
    [JsonIgnore]
    public string EffectiveIdentityUrl => IdentityUrl ?? $"{(GatewayUrl ?? "http://localhost:5000").TrimEnd('/')}/api/v1/identity";

    [JsonIgnore]
    public string EffectiveSecretsUrl => SecretsUrl ?? $"{(GatewayUrl ?? "http://localhost:5000").TrimEnd('/')}/api/v1/secrets";

    [JsonIgnore]
    public string EffectiveGatewayUrl => (GatewayUrl ?? "http://localhost:5000").TrimEnd('/');

    [JsonIgnore]
    public string EffectiveNotificationsUrl => NotificationsUrl ?? "http://localhost:5008";

    [JsonIgnore]
    public string EffectiveDiscordUrl => DiscordUrl ?? "http://localhost:5009";

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var config = new CliConfig
            {
                GatewayUrl = "http://localhost:5000",
                IdentityUrl = "http://localhost:5001",
                SecretsUrl = "http://localhost:5002"
            };

            // Create the config file with defaults on first run
            config.Save();
            return config;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        }
        catch
        {
            return new CliConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);

        var json = JsonSerializer.Serialize(this, SaveOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    public bool IsAuthenticated()
    {
        return !string.IsNullOrWhiteSpace(AccessToken) &&
               TokenExpiresAt.HasValue &&
               TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(1);
    }
}
