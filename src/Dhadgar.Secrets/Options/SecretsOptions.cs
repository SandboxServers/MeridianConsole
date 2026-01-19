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
    /// Workload Identity Federation configuration for authenticating to Azure.
    /// When configured, uses WIF instead of client secrets for Key Vault access.
    /// </summary>
    public WifOptions? Wif { get; set; }

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

/// <summary>
/// Configuration for Workload Identity Federation (WIF) authentication to Azure.
/// </summary>
public sealed class WifOptions
{
    /// <summary>
    /// Azure AD tenant ID for the Secrets service app registration.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD client ID for the Secrets service app registration.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Token endpoint of the Identity service (e.g., http://identity:8080/connect/token).
    /// </summary>
    public string IdentityTokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Client ID for authenticating to the Identity service.
    /// </summary>
    public string? ServiceClientId { get; set; }

    /// <summary>
    /// Client secret for authenticating to the Identity service.
    /// </summary>
    public string? ServiceClientSecret { get; set; }
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
        "redis-password",
        // Discord bot token for internal admin notifications
        "discord-bot-token"
    ];
}

#pragma warning restore CA1002
