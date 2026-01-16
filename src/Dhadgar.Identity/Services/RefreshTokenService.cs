using System.Security.Claims;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Handles refresh token validation, permission reload, and token revocation.
/// This ensures that when a token is refreshed, the user's current
/// permissions are loaded from the database, not carried over from the old token.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Reloads user data and permissions from the database for a token refresh.
    /// Returns null if the user is no longer active or valid.
    /// </summary>
    Task<RefreshTokenResult?> ReloadUserForRefreshAsync(
        Guid userId,
        Guid? currentOrganizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Stores a new refresh token hash for tracking and revocation.
    /// </summary>
    Task<RefreshToken> CreateTokenAsync(
        Guid userId,
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        string? deviceInfo = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates that a refresh token hash is valid and not revoked.
    /// </summary>
    Task<bool> ValidateTokenAsync(
        string tokenHash,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a specific refresh token by its hash.
    /// </summary>
    Task<bool> RevokeTokenAsync(
        string tokenHash,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all refresh tokens for a user (logout from all devices).
    /// </summary>
    Task<int> RevokeAllUserTokensAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all refresh tokens for a user in a specific organization.
    /// </summary>
    Task<int> RevokeUserOrgTokensAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets active sessions (non-revoked, non-expired tokens) for a user.
    /// </summary>
    Task<IReadOnlyCollection<SessionInfo>> GetUserSessionsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a specific session by its ID.
    /// </summary>
    Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default);
}

/// <summary>
/// Information about an active user session (refresh token).
/// </summary>
public sealed record SessionInfo(
    Guid Id,
    Guid OrganizationId,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string? DeviceInfo);

public sealed record RefreshTokenResult(
    User User,
    UserOrganization? Membership,
    IReadOnlyCollection<string> Permissions,
    string? Role,
    bool EmailVerified);

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        IdentityDbContext dbContext,
        IPermissionService permissionService,
        TimeProvider timeProvider,
        ILogger<RefreshTokenService> logger)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RefreshTokenResult?> ReloadUserForRefreshAsync(
        Guid userId,
        Guid? currentOrganizationId,
        CancellationToken ct = default)
    {
        // Load user from database to verify they still exist and are active
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);

        if (user is null)
        {
            _logger.LogWarning("Refresh token rejected: User {UserId} not found or deleted", userId);
            return null;
        }

        // Determine which organization to use
        var organizationId = currentOrganizationId ?? user.PreferredOrganizationId;

        if (organizationId is null)
        {
            _logger.LogWarning("Refresh token rejected: User {UserId} has no organization", userId);
            return null;
        }

        // Load membership to verify user is still a member
        var membership = await _dbContext.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo =>
                uo.UserId == userId &&
                uo.OrganizationId == organizationId.Value &&
                uo.LeftAt == null &&
                uo.IsActive,
                ct);

        if (membership is null)
        {
            _logger.LogWarning(
                "Refresh token rejected: User {UserId} is no longer a member of organization {OrgId}",
                userId, organizationId);
            return null;
        }

        // Recalculate permissions from database (roles may have changed)
        var permissions = await _permissionService.CalculatePermissionsAsync(
            userId,
            organizationId.Value,
            ct);

        _logger.LogDebug(
            "Refresh token: Reloaded {PermissionCount} permissions for user {UserId} in org {OrgId}",
            permissions.Count, userId, organizationId);

        return new RefreshTokenResult(user, membership, permissions, membership.Role, user.EmailVerified);
    }

    public async Task<RefreshToken> CreateTokenAsync(
        Guid userId,
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        string? deviceInfo = null,
        CancellationToken ct = default)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = tokenHash,
            IssuedAt = _timeProvider.GetUtcNow().DateTime,
            ExpiresAt = expiresAt,
            DeviceInfo = deviceInfo
        };

        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Created refresh token {TokenId} for user {UserId} in org {OrgId}",
            token.Id, userId, organizationId);

        return token;
    }

    public async Task<bool> ValidateTokenAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var isValid = await _dbContext.RefreshTokens
            .AsNoTracking()
            .AnyAsync(t =>
                t.TokenHash == tokenHash &&
                t.RevokedAt == null &&
                t.ExpiresAt > now,
                ct);

        return isValid;
    }

    public async Task<bool> RevokeTokenAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        var token = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null, ct);

        if (token is null)
        {
            return false;
        }

        token.RevokedAt = _timeProvider.GetUtcNow().DateTime;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked refresh token {TokenId} for user {UserId}",
            token.Id, token.UserId);

        return true;
    }

    public async Task<int> RevokeAllUserTokensAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var activeTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked all {Count} refresh tokens for user {UserId}",
            activeTokens.Count, userId);

        return activeTokens.Count;
    }

    public async Task<int> RevokeUserOrgTokensAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var orgTokens = await _dbContext.RefreshTokens
            .Where(t =>
                t.UserId == userId &&
                t.OrganizationId == organizationId &&
                t.RevokedAt == null &&
                t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in orgTokens)
        {
            token.RevokedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked {Count} refresh tokens for user {UserId} in org {OrgId}",
            orgTokens.Count, userId, organizationId);

        return orgTokens.Count;
    }

    public async Task<IReadOnlyCollection<SessionInfo>> GetUserSessionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var sessions = await _dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t =>
                t.UserId == userId &&
                t.RevokedAt == null &&
                t.ExpiresAt > now)
            .OrderByDescending(t => t.IssuedAt)
            .Select(t => new SessionInfo(
                t.Id,
                t.OrganizationId,
                t.IssuedAt,
                t.ExpiresAt,
                t.DeviceInfo))
            .ToListAsync(ct);

        return sessions;
    }

    public async Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var token = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t =>
                t.Id == sessionId &&
                t.UserId == userId &&
                t.RevokedAt == null,
                ct);

        if (token is null)
        {
            return false;
        }

        token.RevokedAt = _timeProvider.GetUtcNow().DateTime;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked session {SessionId} for user {UserId}",
            sessionId, userId);

        return true;
    }
}

/// <summary>
/// Extension methods for building claims from RefreshTokenResult.
/// </summary>
public static class RefreshTokenClaimsBuilder
{
    /// <summary>
    /// Creates a ClaimsIdentity from the refresh token result with fresh permissions.
    /// </summary>
    public static ClaimsIdentity BuildClaimsIdentity(
        this RefreshTokenResult result,
        string authenticationType)
    {
        var identity = new ClaimsIdentity(authenticationType);

        // Core identity claims
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, result.User.Id.ToString()));

        if (!string.IsNullOrWhiteSpace(result.User.Email))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, result.User.Email));
        }

        if (!string.IsNullOrWhiteSpace(result.User.DisplayName))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, result.User.DisplayName));
        }

        // Organization context
        if (result.Membership is not null)
        {
            identity.AddClaim(new Claim("org_id", result.Membership.OrganizationId.ToString()));

            if (!string.IsNullOrWhiteSpace(result.Role))
            {
                identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, result.Role));
            }
        }

        // Email verification status
        identity.AddClaim(new Claim("email_verified", result.EmailVerified.ToString().ToLowerInvariant()));

        // Add all current permissions as claims
        foreach (var permission in result.Permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        return identity;
    }
}
