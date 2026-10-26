using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Outcome of a refresh-token redemption.
/// </summary>
public sealed record TokenRefreshOutcome(
    bool Success,
    string? Error,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    Guid? UserId,
    Guid? OrganizationId)
{
    public static TokenRefreshOutcome Fail(string error, Guid? userId = null)
        => new(false, error, null, null, 0, userId, null);
}

/// <summary>
/// Redeems refresh tokens issued by the <c>/exchange</c> flow.
/// </summary>
/// <remarks>
/// <para>
/// Refresh tokens are opaque random values; only their SHA-256 hash is stored in
/// <c>refresh_tokens</c>. Redemption is single-use with rotation: the presented token
/// is revoked and a new access/refresh pair is issued in the same unit of work.
/// </para>
/// <para>
/// Permissions are recalculated from the database on every refresh (via
/// <see cref="IRefreshTokenService.ReloadUserForRefreshAsync"/>), so role changes and
/// membership removal take effect within one access-token lifetime.
/// </para>
/// <para>
/// Reuse detection (OAuth 2.0 Security BCP §4.14.2): presenting a token that has
/// already been revoked is treated as a possible theft, and every active refresh token
/// for that user in that organization is revoked.
/// </para>
/// </remarks>
public sealed class TokenRefreshService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IJwtService _jwtService;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<TokenRefreshService> _logger;

    public TokenRefreshService(
        IdentityDbContext dbContext,
        IRefreshTokenService refreshTokenService,
        IJwtService jwtService,
        TimeProvider timeProvider,
        IOptions<AuthOptions> authOptions,
        ILogger<TokenRefreshService> logger)
    {
        _dbContext = dbContext;
        _refreshTokenService = refreshTokenService;
        _jwtService = jwtService;
        _timeProvider = timeProvider;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<TokenRefreshOutcome> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return TokenRefreshOutcome.Fail("missing_refresh_token");
        }

        var tokenHash = HashToken(refreshToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var stored = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (stored is null)
        {
            return TokenRefreshOutcome.Fail("invalid_refresh_token");
        }

        if (stored.RevokedAt is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} in org {OrgId}; revoking all sessions in that org",
                stored.UserId, stored.OrganizationId);

            await _refreshTokenService.RevokeUserOrgTokensAsync(stored.UserId, stored.OrganizationId, ct);
            return TokenRefreshOutcome.Fail("refresh_token_reused", stored.UserId);
        }

        if (stored.ExpiresAt <= now)
        {
            return TokenRefreshOutcome.Fail("refresh_token_expired", stored.UserId);
        }

        // Re-check the user and membership against current DB state and recompute permissions.
        var reload = await _refreshTokenService.ReloadUserForRefreshAsync(stored.UserId, stored.OrganizationId, ct);
        if (reload is null || reload.Membership is null || string.IsNullOrWhiteSpace(reload.User.Email))
        {
            stored.RevokedAt = now;
            await TrySaveAsync(ct);
            return TokenRefreshOutcome.Fail("user_inactive", stored.UserId);
        }

        if (_authOptions.EmailVerification.RequireVerifiedEmail && !reload.EmailVerified)
        {
            return TokenRefreshOutcome.Fail("email_not_verified", stored.UserId);
        }

        var claims = BuildClaims(reload, stored.OrganizationId);
        var (accessToken, newRefreshToken, expiresIn) = await _jwtService.GenerateTokenPairAsync(claims, ct);

        // Rotate: revoke the presented token and persist the replacement atomically.
        // RevokedAt is a concurrency token, so if a parallel request redeemed the same
        // token first, SaveChanges throws and this request is rejected.
        stored.RevokedAt = now;
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            OrganizationId = stored.OrganizationId,
            TokenHash = HashToken(newRefreshToken),
            IssuedAt = now,
            ExpiresAt = now.AddDays(_authOptions.RefreshTokenLifetimeDays),
            DeviceInfo = stored.DeviceInfo
        });

        if (!await TrySaveAsync(ct))
        {
            return TokenRefreshOutcome.Fail("refresh_token_reused", stored.UserId);
        }

        return new TokenRefreshOutcome(
            true,
            null,
            accessToken,
            newRefreshToken,
            expiresIn,
            stored.UserId,
            stored.OrganizationId);
    }

    /// <summary>
    /// Mirrors the claim set issued by <see cref="TokenExchangeService"/> so refreshed
    /// tokens are indistinguishable from freshly exchanged ones.
    /// </summary>
    private static List<Claim> BuildClaims(RefreshTokenResult reload, Guid organizationId)
    {
        var claims = new List<Claim>
        {
            new("sub", reload.User.Id.ToString()),
            new("org_id", organizationId.ToString()),
            new("email", reload.User.Email!),
            new("role", reload.Role ?? reload.Membership!.Role),
            new("email_verified", reload.EmailVerified.ToString().ToLowerInvariant()),
            new("principal_type", "user")
        };

        foreach (var permission in reload.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        return claims;
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrent refresh token redemption rejected");
            return false;
        }
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
