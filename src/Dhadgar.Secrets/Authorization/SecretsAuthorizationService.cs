using System.Security.Claims;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// Implementation of secrets authorization with permission hierarchy.
/// Permission format: secrets:{action}:{category-or-name}
/// Supports wildcards: secrets:*, secrets:read:*
/// </summary>
public sealed class SecretsAuthorizationService : ISecretsAuthorizationService
{
    private static readonly TimeSpan MaxBreakGlassTtl = TimeSpan.FromHours(1);

    private readonly SecretsOptions _options;
    private readonly IBreakGlassNonceTracker _nonceTracker;
    private readonly ILogger<SecretsAuthorizationService> _logger;

    public SecretsAuthorizationService(
        IOptions<SecretsOptions> options,
        IBreakGlassNonceTracker nonceTracker,
        ILogger<SecretsAuthorizationService> logger)
    {
        _options = options.Value;
        _nonceTracker = nonceTracker;
        _logger = logger;
    }

    public AuthorizationResult Authorize(ClaimsPrincipal user, string secretName, SecretAction action)
    {
        var userId = user.FindFirstValue("sub");
        var principalType = user.FindFirstValue("principal_type") ?? "user";
        var isServiceAccount = principalType == "service";

        // Check authentication
        if (user.Identity?.IsAuthenticated != true)
        {
            return AuthorizationResult.Denied("User is not authenticated");
        }

        // Check break-glass access
        if (user.HasClaim("break_glass", "true"))
        {
            return ValidateBreakGlassAccess(user, userId, principalType, isServiceAccount, secretName);
        }

        // Determine the category of the secret
        var category = GetSecretCategory(secretName);
        var actionStr = action.ToString().ToLowerInvariant();

        // Check permission hierarchy (most specific to least specific):
        // 1. secrets:* (full admin)
        // 2. secrets:{action}:* (action on all categories)
        // 3. secrets:{action}:{category} (action on category)
        // 4. secrets:{action}:{secretName} (action on specific secret)

        var permissions = new[]
        {
            "secrets:*",
            $"secrets:{actionStr}:*",
            $"secrets:{actionStr}:{category}",
            $"secrets:{actionStr}:{secretName}"
        };

        foreach (var permission in permissions)
        {
            if (HasPermission(user, permission))
            {
                _logger.LogDebug(
                    "Access granted for {Action} on {SecretName} via permission {Permission}",
                    action, secretName, permission);

                return AuthorizationResult.Success(userId, principalType, isServiceAccount: isServiceAccount);
            }
        }

        _logger.LogWarning(
            "Access denied for {Action} on {SecretName} for user {UserId}. Required one of: {Permissions}",
            action, secretName, userId, string.Join(", ", permissions));

        return AuthorizationResult.Denied(
            $"Missing permission for {action} on secret '{secretName}'",
            userId);
    }

    public AuthorizationResult AuthorizeCategory(ClaimsPrincipal user, string category, SecretAction action)
    {
        var userId = user.FindFirstValue("sub");
        var principalType = user.FindFirstValue("principal_type") ?? "user";
        var isServiceAccount = principalType == "service";

        if (user.Identity?.IsAuthenticated != true)
        {
            return AuthorizationResult.Denied("User is not authenticated");
        }

        // Check break-glass
        if (user.HasClaim("break_glass", "true"))
        {
            return ValidateBreakGlassAccess(user, userId, principalType, isServiceAccount, $"category:{category}");
        }

        var actionStr = action.ToString().ToLowerInvariant();

        var permissions = new[]
        {
            "secrets:*",
            $"secrets:{actionStr}:*",
            $"secrets:{actionStr}:{category}"
        };

        foreach (var permission in permissions)
        {
            if (HasPermission(user, permission))
            {
                return AuthorizationResult.Success(userId, principalType, isServiceAccount: isServiceAccount);
            }
        }

        return AuthorizationResult.Denied(
            $"Missing permission for {action} on category '{category}'",
            userId);
    }

    private AuthorizationResult ValidateBreakGlassAccess(
        ClaimsPrincipal user,
        string? userId,
        string principalType,
        bool isServiceAccount,
        string resourceName)
    {
        // Require expiration claim
        var expClaim = user.FindFirstValue("break_glass_exp");
        if (string.IsNullOrWhiteSpace(expClaim))
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: missing expiration claim (break_glass_exp).",
                resourceName, userId);
            return AuthorizationResult.Denied("Break-glass token must include an expiration (break_glass_exp).", userId);
        }

        if (!long.TryParse(expClaim, out var expUnix))
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: invalid expiration format.",
                resourceName, userId);
            return AuthorizationResult.Denied("Break-glass token expiration (break_glass_exp) is not a valid Unix timestamp.", userId);
        }

        var expiration = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        if (expiration > DateTimeOffset.UtcNow + MaxBreakGlassTtl)
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: expiration exceeds maximum TTL of {MaxTtl}.",
                resourceName, userId, MaxBreakGlassTtl);
            return AuthorizationResult.Denied($"Break-glass token TTL exceeds maximum of {MaxBreakGlassTtl.TotalMinutes} minutes.", userId);
        }

        if (expiration <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: token has expired at {Expiration}.",
                resourceName, userId, expiration);
            return AuthorizationResult.Denied("Break-glass token has expired.", userId);
        }

        // Require and validate single-use nonce
        var nonce = user.FindFirstValue("break_glass_nonce");
        if (string.IsNullOrWhiteSpace(nonce))
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: missing nonce claim (break_glass_nonce).",
                resourceName, userId);
            return AuthorizationResult.Denied("Break-glass token must include a single-use nonce (break_glass_nonce).", userId);
        }

        if (!_nonceTracker.TryConsumeNonceAsync(nonce).GetAwaiter().GetResult())
        {
            _logger.LogWarning(
                "Break-glass access DENIED for {ResourceName} by {UserId}: nonce {Nonce} has already been consumed (replay attempt).",
                resourceName, userId, nonce);
            return AuthorizationResult.Denied("Break-glass token nonce has already been used.", userId);
        }

        var reason = user.FindFirstValue("break_glass_reason") ?? "No reason provided";
        _logger.LogWarning(
            "Break-glass access GRANTED for {ResourceName} by {UserId}. Reason: {Reason}. Nonce: {Nonce}. Expires: {Expiration}.",
            resourceName, userId, reason, nonce, expiration);

        return AuthorizationResult.Success(userId, principalType, isBreakGlass: true, isServiceAccount);
    }

    private string GetSecretCategory(string secretName)
    {
        if (_options.AllowedSecrets.OAuth.Contains(secretName, StringComparer.OrdinalIgnoreCase))
            return "oauth";

        if (_options.AllowedSecrets.BetterAuth.Contains(secretName, StringComparer.OrdinalIgnoreCase))
            return "betterauth";

        if (_options.AllowedSecrets.Infrastructure.Contains(secretName, StringComparer.OrdinalIgnoreCase))
            return "infrastructure";

        // Infer from naming convention
        var lowerName = secretName.ToLowerInvariant();
        if (lowerName.StartsWith("oauth-")) return "oauth";
        if (lowerName.StartsWith("betterauth-")) return "betterauth";

        return "custom";
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        return user.Claims.Any(claim =>
            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
    }
}
