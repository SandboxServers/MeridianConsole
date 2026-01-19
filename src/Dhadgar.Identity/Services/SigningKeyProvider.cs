using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Identity.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Provides a shared ECDSA signing key for JWT token generation.
/// This ensures both JwtService and OpenIddict use the same key.
/// </summary>
public interface ISigningKeyProvider : IDisposable
{
    ECDsaSecurityKey GetSigningKey();
    SigningCredentials GetSigningCredentials();
}

public sealed class SigningKeyProvider : ISigningKeyProvider
{
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;

    public SigningKeyProvider(AuthOptions options, IHostEnvironment environment, ILogger<SigningKeyProvider> logger)
    {
        _ecdsa = LoadEcdsaKey(options, environment, logger);
        _securityKey = new ECDsaSecurityKey(_ecdsa)
        {
            KeyId = string.IsNullOrWhiteSpace(options.SigningKeyKid) ? "jwt-signing-key" : options.SigningKeyKid
        };
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.EcdsaSha256);
    }

    public ECDsaSecurityKey GetSigningKey() => _securityKey;
    public SigningCredentials GetSigningCredentials() => _signingCredentials;

    private static ECDsa LoadEcdsaKey(AuthOptions options, IHostEnvironment environment, ILogger logger)
    {
        // Try Key Vault first
        if (!string.IsNullOrWhiteSpace(options.KeyVault?.VaultUri) &&
            !string.IsNullOrWhiteSpace(options.KeyVault.JwtSigningKeyName))
        {
            try
            {
                var credential = new DefaultAzureCredential();
                var secretClient = new SecretClient(new Uri(options.KeyVault!.VaultUri), credential);
                var secret = secretClient.GetSecret(options.KeyVault.JwtSigningKeyName);
                var pem = secret.Value.Value;
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                logger.LogInformation("Loaded JWT signing key from Key Vault");
                return ecdsa;
            }
            catch (Exception ex)
            {
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException("Key Vault signing key is required in production.", ex);
                }
                logger.LogWarning(ex, "Failed to load Key Vault signing key, falling back to local key");
            }
        }

        // Try local PEM
        var pem2 = options.SigningKeyPem;
        if (string.IsNullOrWhiteSpace(pem2) && !string.IsNullOrWhiteSpace(options.SigningKeyPath))
        {
            if (File.Exists(options.SigningKeyPath))
            {
                pem2 = File.ReadAllText(options.SigningKeyPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(pem2))
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem2);
            logger.LogInformation("Loaded JWT signing key from local PEM");
            return ecdsa;
        }

        // Generate ephemeral key for development
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("JWT signing key is required in production.");
        }

        logger.LogWarning("Using ephemeral JWT signing key - tokens will not survive restarts");
        return ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    public void Dispose()
    {
        _ecdsa.Dispose();
    }
}
