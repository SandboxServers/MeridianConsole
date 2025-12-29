using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

public sealed record TokenExchangeOutcome(
    bool Success,
    string? Error,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    Guid? UserId,
    Guid? OrganizationId,
    IReadOnlyCollection<string>? Permissions)
{
    public static TokenExchangeOutcome Fail(string error)
        => new(false, error, null, null, 0, null, null, null);
}

public sealed class TokenExchangeService
{
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(2);

    private readonly IdentityDbContext _dbContext;
    private readonly IExchangeTokenValidator _validator;
    private readonly IExchangeTokenReplayStore _replayStore;
    private readonly IJwtService _jwtService;
    private readonly IPermissionService _permissionService;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<TokenExchangeService> _logger;

    public TokenExchangeService(
        IdentityDbContext dbContext,
        IExchangeTokenValidator validator,
        IExchangeTokenReplayStore replayStore,
        IJwtService jwtService,
        IPermissionService permissionService,
        TimeProvider timeProvider,
        IOptions<AuthOptions> authOptions,
        ILogger<TokenExchangeService> logger)
    {
        _dbContext = dbContext;
        _validator = validator;
        _replayStore = replayStore;
        _jwtService = jwtService;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<TokenExchangeOutcome> ExchangeAsync(string exchangeToken, CancellationToken ct = default)
    {
        var principal = await _validator.ValidateAsync(exchangeToken, ct);
        if (principal is null)
        {
            return TokenExchangeOutcome.Fail("invalid_exchange_token");
        }

        if (!string.Equals(principal.FindFirst("purpose")?.Value, "token_exchange", StringComparison.Ordinal))
        {
            return TokenExchangeOutcome.Fail("invalid_purpose");
        }

        var externalAuthId = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email");
        var jti = principal.FindFirstValue("jti");
        var clientApp = principal.FindFirstValue("client_app");

        if (string.IsNullOrWhiteSpace(externalAuthId) || string.IsNullOrWhiteSpace(email))
        {
            return TokenExchangeOutcome.Fail("missing_claims");
        }

        if (string.IsNullOrWhiteSpace(jti))
        {
            return TokenExchangeOutcome.Fail("missing_jti");
        }

        var wasSet = await _replayStore.MarkAsUsedAsync(jti, ReplayWindow, ct);
        if (!wasSet)
        {
            _logger.LogWarning("Exchange token replay detected: {Jti}", jti);
            return TokenExchangeOutcome.Fail("token_already_used");
        }

        User user;
        Organization activeOrg;
        UserOrganization membership;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.ExternalAuthId == externalAuthId, ct)
                ?? new User
                {
                    ExternalAuthId = externalAuthId,
                    Email = email,
                    EmailVerified = false,
                    CreatedAt = now
                };

            if (user.Id == Guid.Empty)
            {
                user.Id = Guid.NewGuid();
            }

            user.Email = email;
            user.LastAuthenticatedAt = now;
            user.UpdatedAt = now;

            if (_dbContext.Entry(user).State == EntityState.Detached)
            {
                _dbContext.Users.Add(user);
            }

            await _dbContext.SaveChangesAsync(ct);

            var memberships = await _dbContext.UserOrganizations
                .Where(uo => uo.UserId == user.Id && uo.LeftAt == null && uo.IsActive)
                .OrderBy(uo => uo.JoinedAt)
                .ToListAsync(ct);

            if (memberships.Count == 0)
            {
                activeOrg = new Organization
                {
                    Name = "Default Organization",
                    Slug = $"user-{user.Id:N}",
                    OwnerId = user.Id,
                    CreatedAt = now
                };

                membership = new UserOrganization
                {
                    UserId = user.Id,
                    Organization = activeOrg,
                    Role = "owner",
                    JoinedAt = now,
                    IsActive = true
                };

                _dbContext.Organizations.Add(activeOrg);
                _dbContext.UserOrganizations.Add(membership);

                user.PreferredOrganizationId = activeOrg.Id;
            }
            else
            {
                membership = memberships.First();

                if (user.PreferredOrganizationId.HasValue)
                {
                    var preferred = memberships.FirstOrDefault(uo => uo.OrganizationId == user.PreferredOrganizationId.Value);
                    if (preferred is not null)
                    {
                        membership = preferred;
                    }
                }

                activeOrg = await _dbContext.Organizations
                    .FirstAsync(o => o.Id == membership.OrganizationId, ct);

                if (user.PreferredOrganizationId != activeOrg.Id)
                {
                    user.PreferredOrganizationId = activeOrg.Id;
                }
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Token exchange failed for external user {ExternalAuthId}", externalAuthId);
            throw;
        }

        var permissions = await _permissionService.CalculatePermissionsAsync(user.Id, activeOrg.Id, ct);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("org_id", activeOrg.Id.ToString()),
            new("email", user.Email),
            new("role", membership.Role)
        };

        if (!string.IsNullOrWhiteSpace(clientApp))
        {
            claims.Add(new Claim("client_app", clientApp));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var (accessToken, refreshToken, expiresIn) = await _jwtService.GenerateTokenPairAsync(claims, ct);

        var refreshExpiresAt = now.AddDays(_authOptions.RefreshTokenLifetimeDays);
        var refreshTokenHash = HashToken(refreshToken);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = activeOrg.Id,
            TokenHash = refreshTokenHash,
            IssuedAt = now,
            ExpiresAt = refreshExpiresAt
        });

        await _dbContext.SaveChangesAsync(ct);

        return new TokenExchangeOutcome(
            true,
            null,
            accessToken,
            refreshToken,
            expiresIn,
            user.Id,
            activeOrg.Id,
            permissions);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
