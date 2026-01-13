using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

public sealed record RoleSummary(
    string Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyCollection<string> Permissions);

public sealed record RoleCreateRequest(
    string Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions);

public sealed record RoleAssignmentRequest(Guid UserId);
public sealed record RoleAssignmentResult(Guid UserId, string Role);

public sealed class RoleService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public RoleService(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<RoleSummary>> ListAsync(Guid organizationId, CancellationToken ct = default)
    {
        var customRoles = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .Where(role => role.OrganizationId == organizationId)
            .OrderBy(role => role.Name)
            .Select(role => new RoleSummary(
                role.Id.ToString("D"),
                role.Name,
                role.Description,
                false,
                role.Permissions))
            .ToListAsync(ct);

        var systemRoles = RoleDefinitions.Roles.Values
            .Select(role => new RoleSummary(
                role.Name.ToLowerInvariant(),
                role.Name,
                role.Description,
                true,
                role.ImpliedClaims))
            .OrderBy(role => role.Name)
            .ToList();

        return systemRoles.Concat(customRoles).ToArray();
    }

    public async Task<ServiceResult<RoleSummary>> GetAsync(
        Guid organizationId,
        string roleIdOrName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return ServiceResult.Fail<RoleSummary>("role_id_required");
        }

        var trimmed = roleIdOrName.Trim();

        if (Guid.TryParse(trimmed, out var roleId))
        {
            var role = await _dbContext.OrganizationRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId, ct);

            if (role is null)
            {
                return ServiceResult.Fail<RoleSummary>("role_not_found");
            }

            return ServiceResult.Ok(new RoleSummary(
                role.Id.ToString("D"),
                role.Name,
                role.Description,
                false,
                role.Permissions));
        }

        if (RoleDefinitions.IsValidRole(trimmed))
        {
            var definition = RoleDefinitions.GetRole(trimmed);
            return ServiceResult.Ok(new RoleSummary(
                trimmed.ToLowerInvariant(),
                definition.Name,
                definition.Description,
                true,
                definition.ImpliedClaims));
        }

        var normalized = NormalizeName(trimmed);
        var custom = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(role =>
                role.OrganizationId == organizationId &&
                role.NormalizedName == normalized, ct);

        if (custom is null)
        {
            return ServiceResult.Fail<RoleSummary>("role_not_found");
        }

        return ServiceResult.Ok(new RoleSummary(
            custom.Id.ToString("D"),
            custom.Name,
            custom.Description,
            false,
            custom.Permissions));
    }

    public async Task<ServiceResult<RoleSummary>> CreateAsync(
        Guid organizationId,
        RoleCreateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult.Fail<RoleSummary>("role_name_required");
        }

        var orgExists = await _dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(org => org.Id == organizationId && org.DeletedAt == null, ct);

        if (!orgExists)
        {
            return ServiceResult.Fail<RoleSummary>("org_not_found");
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 50)
        {
            return ServiceResult.Fail<RoleSummary>("role_name_too_long");
        }

        if (RoleDefinitions.IsValidRole(trimmedName))
        {
            return ServiceResult.Fail<RoleSummary>("reserved_role_name");
        }

        var normalized = NormalizeName(trimmedName);
        var exists = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .AnyAsync(role =>
                role.OrganizationId == organizationId &&
                role.NormalizedName == normalized, ct);

        if (exists)
        {
            return ServiceResult.Fail<RoleSummary>("role_already_exists");
        }

        var permissions = (request.Permissions ?? Array.Empty<string>())
            .Select(permission => permission.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (permissions.Count > 0)
        {
            var knownClaims = await _dbContext.ClaimDefinitions
                .AsNoTracking()
                .Select(cd => cd.Name)
                .ToListAsync(ct);

            var unknown = permissions
                .Except(knownClaims, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unknown.Length > 0)
            {
                return ServiceResult.Fail<RoleSummary>("unknown_permissions");
            }
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var roleEntity = new OrganizationRole
        {
            OrganizationId = organizationId,
            Name = trimmedName,
            NormalizedName = normalized,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Permissions = new System.Collections.ObjectModel.Collection<string>(permissions),
            CreatedAt = now
        };

        _dbContext.OrganizationRoles.Add(roleEntity);
        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult.Ok(new RoleSummary(
            roleEntity.Id.ToString("D"),
            roleEntity.Name,
            roleEntity.Description,
            false,
            roleEntity.Permissions));
    }

    public async Task<ServiceResult<RoleAssignmentResult>> AssignRoleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string roleIdOrName,
        MembershipService membershipService,
        CancellationToken ct = default)
    {
        var resolution = await ResolveRoleAsync(organizationId, roleIdOrName, ct);
        if (resolution is null)
        {
            return ServiceResult.Fail<RoleAssignmentResult>("role_not_found");
        }

        var result = await membershipService.AssignRoleAsync(
            organizationId,
            actorUserId,
            targetUserId,
            resolution.Name,
            ct);

        return result.Success
            ? ServiceResult.Ok(new RoleAssignmentResult(targetUserId, resolution.Name))
            : ServiceResult.Fail<RoleAssignmentResult>(result.Error ?? "assign_failed");
    }

    public async Task<ServiceResult<RoleAssignmentResult>> RevokeRoleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string roleIdOrName,
        MembershipService membershipService,
        CancellationToken ct = default)
    {
        var resolution = await ResolveRoleAsync(organizationId, roleIdOrName, ct);
        if (resolution is null)
        {
            return ServiceResult.Fail<RoleAssignmentResult>("role_not_found");
        }

        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<RoleAssignmentResult>("user_not_found");
        }

        if (!string.Equals(membership.Role, resolution.Name, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult.Fail<RoleAssignmentResult>("role_not_assigned");
        }

        var result = await membershipService.AssignRoleAsync(
            organizationId,
            actorUserId,
            targetUserId,
            "viewer",
            ct);

        return result.Success
            ? ServiceResult.Ok(new RoleAssignmentResult(targetUserId, "viewer"))
            : ServiceResult.Fail<RoleAssignmentResult>(result.Error ?? "revoke_failed");
    }

    public async Task<IReadOnlyCollection<RoleSummary>> SearchAsync(
        Guid organizationId,
        string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RoleSummary>();
        }

        var term = query.Trim();
        var systemMatches = RoleDefinitions.Roles.Values
            .Where(role =>
                role.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                role.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(role => new RoleSummary(
                role.Name.ToLowerInvariant(),
                role.Name,
                role.Description,
                true,
                role.ImpliedClaims))
            .ToList();

        var normalized = NormalizeName(term);
        var pattern = $"%{term}%";
        var customMatches = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .Where(role => role.OrganizationId == organizationId)
            .Where(role =>
                EF.Functions.Like(role.Name, pattern) ||
                (role.Description != null && EF.Functions.Like(role.Description, pattern)) ||
                role.NormalizedName.Contains(normalized))
            .OrderBy(role => role.Name)
            .Select(role => new RoleSummary(
                role.Id.ToString("D"),
                role.Name,
                role.Description,
                false,
                role.Permissions))
            .ToListAsync(ct);

        return systemMatches.Concat(customMatches).ToArray();
    }

    private async Task<RoleResolution?> ResolveRoleAsync(
        Guid organizationId,
        string roleIdOrName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return null;
        }

        var trimmed = roleIdOrName.Trim();

        if (Guid.TryParse(trimmed, out var roleId))
        {
            var role = await _dbContext.OrganizationRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId, ct);
            return role is null ? null : new RoleResolution(role.Name, false);
        }

        if (RoleDefinitions.IsValidRole(trimmed))
        {
            return new RoleResolution(trimmed.ToLowerInvariant(), true);
        }

        var normalized = NormalizeName(trimmed);
        var custom = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(role =>
                role.OrganizationId == organizationId &&
                role.NormalizedName == normalized, ct);

        return custom is null ? null : new RoleResolution(custom.Name, false);
    }

    private static string NormalizeName(string value)
        => value.Trim().ToUpperInvariant();

    private sealed record RoleResolution(string Name, bool IsSystem);
}
