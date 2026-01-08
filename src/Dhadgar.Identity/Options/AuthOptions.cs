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
    /// Shared secret for HMAC-SHA256 signature validation of Better Auth webhooks.
    /// Must be configured in production; validation is skipped if empty in Development.
    /// </summary>
    public string BetterAuthSecret { get; set; } = string.Empty;

    /// <summary>
    /// Header name containing the webhook signature.
    /// </summary>
    public string SignatureHeader { get; set; } = "X-Webhook-Signature";

    /// <summary>
    /// Maximum allowed clock skew for timestamp validation (in seconds).
    /// </summary>
    public int MaxTimestampAgeSeconds { get; set; } = 300;
}
