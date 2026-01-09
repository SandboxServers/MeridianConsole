using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> CalculatePermissionsAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PermissionService(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<string>> CalculatePermissionsAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
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
            return Array.Empty<string>();
        }

        var roleDefinition = RoleDefinitions.GetRole(membership.Role);
        var permissions = new HashSet<string>(roleDefinition.ImpliedClaims, StringComparer.OrdinalIgnoreCase);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var customClaims = await _dbContext.UserOrganizationClaims
            .AsNoTracking()
            .Where(c => c.UserOrganizationId == membership.Id)
            .Where(c => c.ExpiresAt == null || c.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var claim in customClaims)
        {
            if (claim.ClaimType == ClaimType.Grant)
            {
                permissions.Add(claim.ClaimValue);
            }
            else if (claim.ClaimType == ClaimType.Deny)
            {
                permissions.Remove(claim.ClaimValue);
            }
        }

        return permissions.ToArray();
    }
}
