using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Microsoft.AspNetCore.Identity;
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
    private readonly IIdentityEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<TokenExchangeService> _logger;
    private readonly ILookupNormalizer _lookupNormalizer;

    public TokenExchangeService(
        IdentityDbContext dbContext,
        IExchangeTokenValidator validator,
        IExchangeTokenReplayStore replayStore,
        IJwtService jwtService,
        IPermissionService permissionService,
        IIdentityEventPublisher eventPublisher,
        TimeProvider timeProvider,
        IOptions<AuthOptions> authOptions,
        ILogger<TokenExchangeService> logger,
        ILookupNormalizer lookupNormalizer)
    {
        _dbContext = dbContext;
        _validator = validator;
        _replayStore = replayStore;
        _jwtService = jwtService;
        _permissionService = permissionService;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _authOptions = authOptions.Value;
        _logger = logger;
        _lookupNormalizer = lookupNormalizer;
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
        var now = _timeProvider.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        async Task ExecuteExchangeCoreAsync()
        {
            // Query for existing user - use DbContext directly for read consistency
            var normalizedEmail = _lookupNormalizer.NormalizeEmail(email);
            var userByExternal = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.ExternalAuthId == externalAuthId, ct);
            var userByEmail = userByExternal is null
                ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct)
                : null;

            user = userByExternal ?? userByEmail;

            if (user is null)
            {
                // Create user directly via DbContext (not UserManager) for transaction atomicity
                user = new User
                {
                    Id = Guid.NewGuid(),
                    ExternalAuthId = externalAuthId,
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    UserName = email,
                    NormalizedUserName = _lookupNormalizer.NormalizeName(email),
                    EmailConfirmed = false,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                    CreatedAt = nowUtc,
                    LastAuthenticatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                _dbContext.Users.Add(user);
            }
            else
            {
                if (!string.Equals(user.ExternalAuthId, externalAuthId, StringComparison.Ordinal))
                {
                    if (!ExternalAuthIdHelper.IsManualId(user.ExternalAuthId))
                    {
                        throw new InvalidOperationException("external_auth_conflict");
                    }

                    user.ExternalAuthId = externalAuthId;
                }

                // Update user directly via DbContext for transaction atomicity
                user.Email = email;
                user.NormalizedEmail = normalizedEmail;
                user.UserName = email;
                user.NormalizedUserName = _lookupNormalizer.NormalizeName(email);
                user.LastAuthenticatedAt = nowUtc;
                user.UpdatedAt = nowUtc;
            }

            // Check for existing login link - use DbContext directly
            var existingLogin = await _dbContext.UserLogins
                .AnyAsync(l => l.UserId == user.Id &&
                               l.LoginProvider == "betterauth" &&
                               l.ProviderKey == externalAuthId, ct);

            if (!existingLogin)
            {
                // Add login directly via DbContext for transaction atomicity
                _dbContext.UserLogins.Add(new IdentityUserLogin<Guid>
                {
                    UserId = user.Id,
                    LoginProvider = "betterauth",
                    ProviderKey = externalAuthId,
                    ProviderDisplayName = "BetterAuth"
                });
            }

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
                    CreatedAt = nowUtc
                };

                membership = new UserOrganization
                {
                    UserId = user.Id,
                    Organization = activeOrg,
                    Role = "owner",
                    JoinedAt = nowUtc,
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

            // Single SaveChangesAsync for all changes - ensures atomicity
            await _dbContext.SaveChangesAsync(ct);
        }

        try
        {
            if (string.Equals(
                _dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal))
            {
                await ExecuteExchangeCoreAsync();
            }
            else
            {
                // Use an execution strategy to handle transient failures
                var strategy = _dbContext.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

                    try
                    {
                        await ExecuteExchangeCoreAsync();
                        await transaction.CommitAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync(ct);
                        _logger.LogError(ex, "Token exchange failed for external user {ExternalAuthId}", externalAuthId);
                        throw;
                    }
                });
            }
        }
        catch (InvalidOperationException ex) when (ex.Message == "external_auth_conflict")
        {
            _logger.LogWarning(
                "External auth ID mismatch for exchange token. User is already linked.");
            return TokenExchangeOutcome.Fail("external_auth_conflict");
        }

        // Re-fetch user after transaction (strategy.ExecuteAsync doesn't return values cleanly)
        var normalizedEmailFinal = _lookupNormalizer.NormalizeEmail(email);
        user = (await _dbContext.Users.FirstOrDefaultAsync(u => u.ExternalAuthId == externalAuthId, ct))!;
        activeOrg = await _dbContext.Organizations.FirstAsync(o => o.Id == user.PreferredOrganizationId, ct);
        membership = await _dbContext.UserOrganizations
            .FirstAsync(uo => uo.UserId == user.Id && uo.OrganizationId == activeOrg.Id, ct);

        var permissions = await _permissionService.CalculatePermissionsAsync(user.Id, activeOrg.Id, ct);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("org_id", activeOrg.Id.ToString()),
            new("email", user.Email!),
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

        var refreshExpiresAt = nowUtc.AddDays(_authOptions.RefreshTokenLifetimeDays);
        var refreshTokenHash = HashToken(refreshToken);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = activeOrg.Id,
            TokenHash = refreshTokenHash,
            IssuedAt = nowUtc,
            ExpiresAt = refreshExpiresAt
        });

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _eventPublisher.PublishUserAuthenticatedAsync(new UserAuthenticated(
                user.Id,
                activeOrg.Id,
                externalAuthId,
                user.Email!,
                clientApp,
                permissions,
                now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish UserAuthenticated event for user {UserId}", user.Id);
        }

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
