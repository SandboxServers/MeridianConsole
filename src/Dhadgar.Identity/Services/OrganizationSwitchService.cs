using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

public sealed record OrganizationSwitchOutcome(
    bool Success,
    string? Error,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    Guid? OrganizationId,
    IReadOnlyCollection<string>? Permissions)
{
    public static OrganizationSwitchOutcome Fail(string error)
        => new(false, error, null, null, 0, null, null);
}

public sealed class OrganizationSwitchService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly IPermissionService _permissionService;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;

    public OrganizationSwitchService(
        IdentityDbContext dbContext,
        IJwtService jwtService,
        IPermissionService permissionService,
        TimeProvider timeProvider,
        IOptions<AuthOptions> authOptions)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
        _authOptions = authOptions.Value;
    }

    public async Task<OrganizationSwitchOutcome> SwitchAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return OrganizationSwitchOutcome.Fail("user_not_found");
        }

        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null, ct);

        if (org is null)
        {
            return OrganizationSwitchOutcome.Fail("org_not_found");
        }

        var membership = await _dbContext.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo =>
                uo.UserId == userId &&
                uo.OrganizationId == organizationId &&
                uo.LeftAt == null &&
                uo.IsActive,
                ct);

        if (membership is null)
        {
            return OrganizationSwitchOutcome.Fail("membership_not_found");
        }

        if (user.PreferredOrganizationId != organizationId)
        {
            user.PreferredOrganizationId = organizationId;
            user.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(ct);
        }

        var permissions = await _permissionService.CalculatePermissionsAsync(userId, organizationId, ct);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("org_id", organizationId.ToString()),
            new("email", user.Email),
            new("role", membership.Role)
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var (accessToken, refreshToken, expiresIn) = await _jwtService.GenerateTokenPairAsync(claims, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var refreshExpiresAt = now.AddDays(_authOptions.RefreshTokenLifetimeDays);
        var refreshTokenHash = HashToken(refreshToken);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            TokenHash = refreshTokenHash,
            IssuedAt = now,
            ExpiresAt = refreshExpiresAt
        });

        await _dbContext.SaveChangesAsync(ct);

        return new OrganizationSwitchOutcome(
            true,
            null,
            accessToken,
            refreshToken,
            expiresIn,
            organizationId,
            permissions);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
