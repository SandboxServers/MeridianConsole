using System.Security.Claims;
using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Dhadgar.Secrets.Tests.Authorization;

public class SecretsAuthorizationServiceTests
{
    private readonly SecretsAuthorizationService _service;
    private readonly SecretsOptions _options;

    public SecretsAuthorizationServiceTests()
    {
        _options = new SecretsOptions
        {
            KeyVaultUri = "https://test.vault.azure.net/",
            AllowedSecrets = new AllowedSecretsOptions
            {
                OAuth = new List<string> { "oauth-steam-api-key", "oauth-discord-client-secret" },
                BetterAuth = new List<string> { "betterauth-jwt-secret" },
                Infrastructure = new List<string> { "infra-db-password", "infra-redis-password" }
            }
        };

        _service = new SecretsAuthorizationService(
            OptionsFactory.Create(_options),
            new InMemoryBreakGlassNonceTracker(),
            NullLogger<SecretsAuthorizationService>.Instance);
    }

    #region Unauthenticated Access

    [Fact]
    public void Authorize_UnauthenticatedUser_ReturnsDenied()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("not authenticated", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizeCategory_UnauthenticatedUser_ReturnsDenied()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = _service.AuthorizeCategory(user, "oauth", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("not authenticated", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Full Admin (secrets:*)

    [Fact]
    public void Authorize_WithFullAdminPermission_Succeeds()
    {
        var user = CreateUser("user-1", "secrets:*");

        var result = _service.Authorize(user, "any-secret-name", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.Equal("user-1", result.UserId);
    }

    [Fact]
    public void Authorize_WithFullAdmin_SucceedsForAllActions()
    {
        var user = CreateUser("admin-1", "secrets:*");

        foreach (var action in Enum.GetValues<SecretAction>())
        {
            var result = _service.Authorize(user, "any-secret", action);
            Assert.True(result.IsAuthorized, $"Expected full admin to have {action} access");
        }
    }

    [Fact]
    public void AuthorizeCategory_WithFullAdmin_Succeeds()
    {
        var user = CreateUser("admin-1", "secrets:*");

        var result = _service.AuthorizeCategory(user, "oauth", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    #endregion

    #region Action Wildcard (secrets:{action}:*)

    [Fact]
    public void Authorize_WithReadWildcard_SucceedsForRead()
    {
        var user = CreateUser("user-1", "secrets:read:*");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithReadWildcard_FailsForWrite()
    {
        var user = CreateUser("user-1", "secrets:read:*");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Write);

        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithWriteWildcard_SucceedsForWrite()
    {
        var user = CreateUser("user-1", "secrets:write:*");

        var result = _service.Authorize(user, "any-secret", SecretAction.Write);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithRotateWildcard_SucceedsForRotate()
    {
        var user = CreateUser("user-1", "secrets:rotate:*");

        var result = _service.Authorize(user, "any-secret", SecretAction.Rotate);

        Assert.True(result.IsAuthorized);
    }

    #endregion

    #region Category Permissions (secrets:{action}:{category})

    [Fact]
    public void Authorize_WithOAuthCategoryPermission_SucceedsForOAuthSecrets()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithOAuthCategoryPermission_FailsForInfrastructureSecrets()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        var result = _service.Authorize(user, "infra-db-password", SecretAction.Read);

        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithInfrastructureCategoryPermission_SucceedsForInfraSecrets()
    {
        var user = CreateUser("user-1", "secrets:read:infrastructure");

        var result = _service.Authorize(user, "infra-db-password", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void AuthorizeCategory_WithMatchingPermission_Succeeds()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        var result = _service.AuthorizeCategory(user, "oauth", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void AuthorizeCategory_WithMismatchedPermission_Fails()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        var result = _service.AuthorizeCategory(user, "infrastructure", SecretAction.Read);

        Assert.False(result.IsAuthorized);
    }

    #endregion

    #region Specific Secret Permissions (secrets:{action}:{secretName})

    [Fact]
    public void Authorize_WithSpecificSecretPermission_Succeeds()
    {
        var user = CreateUser("user-1", "secrets:read:oauth-steam-api-key");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithSpecificSecretPermission_FailsForOtherSecrets()
    {
        var user = CreateUser("user-1", "secrets:read:oauth-steam-api-key");

        var result = _service.Authorize(user, "oauth-discord-client-secret", SecretAction.Read);

        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_WithSpecificSecretPermission_FailsForOtherActions()
    {
        var user = CreateUser("user-1", "secrets:read:oauth-steam-api-key");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Write);

        Assert.False(result.IsAuthorized);
    }

    #endregion

    #region Break-Glass Access

    [Fact]
    public void Authorize_WithBreakGlass_Succeeds()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_reason", "Critical production incident"),
            new("break_glass_exp", exp),
            new("break_glass_nonce", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "sensitive-secret", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.True(result.IsBreakGlass);
    }

    [Fact]
    public void Authorize_WithBreakGlass_DeniedWhenExpired()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_exp", exp),
            new("break_glass_nonce", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "any-secret", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("expired", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorize_WithBreakGlass_DeniedWhenMissingExpiration()
    {
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_nonce", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "any-secret", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("expiration", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorize_WithBreakGlass_DeniedWhenMissingNonce()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_exp", exp)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "any-secret", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("nonce", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorize_WithBreakGlass_DeniedOnNonceReplay()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_exp", exp),
            new("break_glass_nonce", nonce)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        // First use succeeds
        var result1 = _service.Authorize(user, "any-secret", SecretAction.Read);
        Assert.True(result1.IsAuthorized);

        // Replay denied
        var result2 = _service.Authorize(user, "any-secret", SecretAction.Read);
        Assert.False(result2.IsAuthorized);
        Assert.Contains("already been used", result2.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorize_WithBreakGlass_DeniedWhenTtlExceedsMax()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_exp", exp),
            new("break_glass_nonce", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "any-secret", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("exceeds maximum", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizeCategory_WithBreakGlass_Succeeds()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString();
        var claims = new List<Claim>
        {
            new("sub", "emergency-user"),
            new("break_glass", "true"),
            new("break_glass_exp", exp),
            new("break_glass_nonce", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.AuthorizeCategory(user, "oauth", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.True(result.IsBreakGlass);
    }

    #endregion

    #region Service Account Detection

    [Fact]
    public void Authorize_WithServiceAccount_DetectsServiceAccountType()
    {
        var claims = new List<Claim>
        {
            new("sub", "svc-betterauth"),
            new("principal_type", "service"),
            new("permission", "secrets:read:oauth")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.True(result.IsServiceAccount);
        Assert.Equal("service", result.PrincipalType);
    }

    [Fact]
    public void Authorize_WithUserPrincipal_DetectsUserType()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.False(result.IsServiceAccount);
        Assert.Equal("user", result.PrincipalType);
    }

    [Fact]
    public void Authorize_WithNoPrincipalType_DefaultsToUser()
    {
        var claims = new List<Claim>
        {
            new("sub", "user-1"),
            new("permission", "secrets:read:oauth")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
        Assert.False(result.IsServiceAccount);
        Assert.Equal("user", result.PrincipalType);
    }

    #endregion

    #region Category Inference from Naming Convention

    [Fact]
    public void Authorize_InfersOAuthCategoryFromPrefix()
    {
        var user = CreateUser("user-1", "secrets:read:oauth");

        // Not in AllowedSecrets.OAuth list, but starts with "oauth-"
        var result = _service.Authorize(user, "oauth-new-provider-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_InfersBetterAuthCategoryFromPrefix()
    {
        var user = CreateUser("user-1", "secrets:read:betterauth");

        var result = _service.Authorize(user, "betterauth-session-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_UnknownSecret_DefaultsToCustomCategory()
    {
        var user = CreateUser("user-1", "secrets:read:custom");

        var result = _service.Authorize(user, "my-unknown-secret", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    #endregion

    #region Multiple Permissions

    [Fact]
    public void Authorize_WithMultiplePermissions_SucceedsIfAnyMatches()
    {
        var claims = new List<Claim>
        {
            new("sub", "user-1"),
            new("principal_type", "user"),
            new("permission", "secrets:read:oauth"),
            new("permission", "secrets:write:oauth")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var readResult = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);
        var writeResult = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Write);

        Assert.True(readResult.IsAuthorized);
        Assert.True(writeResult.IsAuthorized);
    }

    [Fact]
    public void Authorize_CaseInsensitivePermissions()
    {
        var claims = new List<Claim>
        {
            new("sub", "user-1"),
            new("principal_type", "user"),
            new("PERMISSION", "SECRETS:READ:OAUTH")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.True(result.IsAuthorized);
    }

    #endregion

    #region Denial Information

    [Fact]
    public void Authorize_WhenDenied_IncludesUserId()
    {
        var user = CreateUser("denied-user", "unrelated:permission");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Equal("denied-user", result.UserId);
    }

    [Fact]
    public void Authorize_WhenDenied_IncludesActionInReason()
    {
        var user = CreateUser("user-1", "secrets:write:oauth");

        var result = _service.Authorize(user, "oauth-steam-api-key", SecretAction.Read);

        Assert.False(result.IsAuthorized);
        Assert.Contains("Read", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private static ClaimsPrincipal CreateUser(string userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("principal_type", "user")
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}
