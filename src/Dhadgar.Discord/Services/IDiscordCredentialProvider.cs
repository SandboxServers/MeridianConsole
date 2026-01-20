namespace Dhadgar.Discord.Services;

/// <summary>
/// Provides Discord credentials from the Secrets service.
/// </summary>
public interface IDiscordCredentialProvider
{
    /// <summary>
    /// Gets the Discord bot token.
    /// </summary>
    Task<string> GetBotTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the Discord OAuth client ID.
    /// </summary>
    Task<string> GetClientIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the Discord OAuth client secret.
    /// </summary>
    Task<string> GetClientSecretAsync(CancellationToken ct = default);
}
