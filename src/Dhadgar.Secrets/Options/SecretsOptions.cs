namespace Dhadgar.Secrets.Options;

#pragma warning disable CA1002 // Do not expose generic lists - Options classes require List<T> for configuration binding

public sealed class SecretsOptions
{
    public string KeyVaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Azure subscription ID for Key Vault management operations.
    /// Can also be set via AZURE_SUBSCRIPTION_ID environment variable.
    /// </summary>
    public string? AzureSubscriptionId { get; set; }

    /// <summary>
    /// Permission names required to access secrets.
    /// </summary>
    public SecretsPermissionsOptions Permissions { get; set; } = new();

    /// <summary>
    /// Secret names that are allowed to be dispensed by this service.
    /// Identity service has its own direct Key Vault access for core secrets.
    /// This service dispenses OAuth and other application secrets.
    /// </summary>
    public AllowedSecretsOptions AllowedSecrets { get; set; } = new();
}

public sealed class SecretsPermissionsOptions
{
    public string OAuthRead { get; set; } = "secrets:read:oauth";
    public string BetterAuthRead { get; set; } = "secrets:read:betterauth";
    public string InfrastructureRead { get; set; } = "secrets:read:infrastructure";
}

public sealed class AllowedSecretsOptions
{
    /// <summary>
    /// OAuth provider secrets that can be dispensed.
    /// </summary>
    public List<string> OAuth { get; set; } =
    [
        // Discord bot token for internal admin notifications
        "discord-bot-token",
        // Better Auth supported providers
        "oauth-facebook-app-id",
        "oauth-facebook-app-secret",
        "oauth-google-client-id",
        "oauth-google-client-secret",
        "oauth-discord-client-id",
        "oauth-discord-client-secret",
        "oauth-twitch-client-id",
        "oauth-twitch-client-secret",
        "oauth-github-client-id",
        "oauth-github-client-secret",
        "oauth-apple-client-id",
        "oauth-apple-client-secret",
        "oauth-amazon-client-id",
        "oauth-amazon-client-secret",
        // Microsoft OAuth (personal + work)
        "oauth-microsoft-personal-client-id",
        "oauth-microsoft-personal-client-secret",
        "oauth-microsoft-work-client-id",
        "oauth-microsoft-work-client-secret",
        // Gaming platforms (ASP.NET Identity)
        "oauth-steam-api-key",
        "oauth-battlenet-client-id",
        "oauth-battlenet-client-secret",
        "oauth-epic-client-id",
        "oauth-epic-client-secret",
        "oauth-xbox-client-id",
        "oauth-xbox-client-secret"
    ];

    /// <summary>
    /// BetterAuth-specific secrets that can be dispensed.
    /// </summary>
    public List<string> BetterAuth { get; set; } =
    [
        "betterauth-secret",
        "betterauth-exchange-private-key",
        "better-auth-webhook-secret"
    ];

    /// <summary>
    /// Infrastructure secrets that can be dispensed.
    /// </summary>
    public List<string> Infrastructure { get; set; } =
    [
        "postgres-password",
        "rabbitmq-password",
        "redis-password"
    ];
}

#pragma warning restore CA1002
