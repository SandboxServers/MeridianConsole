namespace Dhadgar.Identity.Options;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenLifetimeSeconds { get; set; } = 900;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public string SigningKeyPath { get; set; } = string.Empty;
    public string SigningKeyPem { get; set; } = string.Empty;
    public string SigningKeyKid { get; set; } = string.Empty;
    public KeyVaultOptions KeyVault { get; set; } = new();
}

public sealed class ExchangeTokenOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string PublicKeyPath { get; set; } = string.Empty;
}

public sealed class KeyVaultOptions
{
    public string VaultUri { get; set; } = string.Empty;
    public string SigningKeyName { get; set; } = string.Empty;
    public string EncryptionCertName { get; set; } = string.Empty;
}

public sealed class WebhookOptions
{
    /// <summary>
    /// Key Vault secret name for the Better Auth webhook shared secret.
    /// The secret is retrieved from the vault specified in Auth:KeyVault:VaultUri.
    /// </summary>
    public string BetterAuthSecretName { get; set; } = "better-auth-webhook-secret";

    /// <summary>
    /// Header name containing the webhook signature.
    /// </summary>
    public string SignatureHeader { get; set; } = "X-Webhook-Signature";

    /// <summary>
    /// Maximum allowed clock skew for timestamp validation (in seconds).
    /// </summary>
    public int MaxTimestampAgeSeconds { get; set; } = 300;

    /// <summary>
    /// Cache duration for the webhook secret in minutes. Default: 60 minutes.
    /// </summary>
    public int SecretCacheMinutes { get; set; } = 60;
}
