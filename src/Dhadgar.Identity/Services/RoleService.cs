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

public sealed record RoleUpdateRequest(
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions);

public sealed record RoleMember(
    Guid UserId,
    string Email,
    string? DisplayName,
    DateTime JoinedAt);

public sealed record RoleAssignmentRequest(Guid UserId);
public sealed record RoleAssignmentResult(Guid UserId, string Role);

public sealed class RoleService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        IdentityDbContext dbContext,
        IPermissionService permissionService,
        TimeProvider timeProvider,
        ILogger<RoleService> logger)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _timeProvider = timeProvider;
        _logger = logger;
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

    /// <summary>
    /// Creates a custom role with the specified permissions.
    /// SECURITY: The actor can only grant permissions they themselves have.
    /// This prevents privilege escalation attacks.
    /// </summary>
    public async Task<ServiceResult<RoleSummary>> CreateAsync(
        Guid organizationId,
        Guid actorUserId,
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

            // SECURITY: Prevent privilege escalation - actor cannot grant permissions they don't have
            var actorPermissions = await _permissionService.CalculatePermissionsAsync(
                actorUserId, organizationId, ct);

            var actorPermissionSet = new HashSet<string>(actorPermissions, StringComparer.OrdinalIgnoreCase);
            var escalatedPermissions = permissions
                .Where(p => !actorPermissionSet.Contains(p))
                .ToArray();

            if (escalatedPermissions.Length > 0)
            {
                _logger.LogWarning(
                    "Privilege escalation attempt: User {UserId} tried to create role with permissions they don't have: {Permissions}",
                    actorUserId, string.Join(", ", escalatedPermissions));
                return ServiceResult.Fail<RoleSummary>("cannot_grant_unowned_permissions");
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

        _logger.LogInformation(
            "Custom role created: {RoleName} in org {OrgId} by user {UserId} with {PermissionCount} permissions",
            trimmedName, organizationId, actorUserId, permissions.Count);

        return ServiceResult.Ok(new RoleSummary(
            roleEntity.Id.ToString("D"),
            roleEntity.Name,
            roleEntity.Description,
            false,
            roleEntity.Permissions));
    }

    /// <summary>
    /// Assigns a role to a user.
    /// SECURITY: For custom roles, validates the actor has all permissions in the role.
    /// This prevents privilege escalation by assigning a role with permissions the actor lacks.
    /// </summary>
    public async Task<ServiceResult<RoleAssignmentResult>> AssignRoleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string roleIdOrName,
        MembershipService membershipService,
        CancellationToken ct = default)
    {
        var resolution = await ResolveRoleWithPermissionsAsync(organizationId, roleIdOrName, ct);
        if (resolution is null)
        {
            return ServiceResult.Fail<RoleAssignmentResult>("role_not_found");
        }

        // SECURITY: For custom roles, validate actor has all permissions being granted
        if (!resolution.IsSystem && resolution.Permissions.Count > 0)
        {
            var actorPermissions = await _permissionService.CalculatePermissionsAsync(
                actorUserId, organizationId, ct);

            var actorPermissionSet = new HashSet<string>(actorPermissions, StringComparer.OrdinalIgnoreCase);
            var escalatedPermissions = resolution.Permissions
                .Where(p => !actorPermissionSet.Contains(p))
                .ToArray();

            if (escalatedPermissions.Length > 0)
            {
                _logger.LogWarning(
                    "Privilege escalation attempt: User {UserId} tried to assign role with permissions they don't have: {Permissions}",
                    actorUserId, string.Join(", ", escalatedPermissions));
                return ServiceResult.Fail<RoleAssignmentResult>("cannot_assign_role_with_unowned_permissions");
            }
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

    /// <summary>
    /// Updates a custom role's name, description, or permissions.
    /// SECURITY: The actor can only grant permissions they themselves have.
    /// System roles cannot be updated.
    /// </summary>
    public async Task<ServiceResult<RoleSummary>> UpdateAsync(
        Guid organizationId,
        Guid actorUserId,
        string roleIdOrName,
        RoleUpdateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return ServiceResult.Fail<RoleSummary>("role_id_required");
        }

        var trimmed = roleIdOrName.Trim();

        // Check if it's a system role (cannot be updated)
        if (RoleDefinitions.IsValidRole(trimmed))
        {
            return ServiceResult.Fail<RoleSummary>("cannot_update_system_role");
        }

        OrganizationRole? role = null;

        if (Guid.TryParse(trimmed, out var roleId))
        {
            role = await _dbContext.OrganizationRoles
                .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId, ct);
        }
        else
        {
            var normalized = NormalizeName(trimmed);
            role = await _dbContext.OrganizationRoles
                .FirstOrDefaultAsync(r =>
                    r.OrganizationId == organizationId &&
                    r.NormalizedName == normalized, ct);
        }

        if (role is null)
        {
            return ServiceResult.Fail<RoleSummary>("role_not_found");
        }

        // Update name if provided
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var newName = request.Name.Trim();
            if (newName.Length > 50)
            {
                return ServiceResult.Fail<RoleSummary>("role_name_too_long");
            }

            if (RoleDefinitions.IsValidRole(newName))
            {
                return ServiceResult.Fail<RoleSummary>("reserved_role_name");
            }

            var newNormalized = NormalizeName(newName);
            if (newNormalized != role.NormalizedName)
            {
                var exists = await _dbContext.OrganizationRoles
                    .AsNoTracking()
                    .AnyAsync(r =>
                        r.OrganizationId == organizationId &&
                        r.NormalizedName == newNormalized &&
                        r.Id != role.Id, ct);

                if (exists)
                {
                    return ServiceResult.Fail<RoleSummary>("role_already_exists");
                }
            }

            role.Name = newName;
            role.NormalizedName = newNormalized;
        }

        // Update description if provided (can be set to empty to clear)
        if (request.Description is not null)
        {
            role.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        // Update permissions if provided
        if (request.Permissions is not null)
        {
            var permissions = request.Permissions
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
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

                // SECURITY: Prevent privilege escalation - actor cannot grant permissions they don't have
                var actorPermissions = await _permissionService.CalculatePermissionsAsync(
                    actorUserId, organizationId, ct);

                var actorPermissionSet = new HashSet<string>(actorPermissions, StringComparer.OrdinalIgnoreCase);
                var escalatedPermissions = permissions
                    .Where(p => !actorPermissionSet.Contains(p))
                    .ToArray();

                if (escalatedPermissions.Length > 0)
                {
                    _logger.LogWarning(
                        "Privilege escalation attempt: User {UserId} tried to update role with permissions they don't have: {Permissions}",
                        actorUserId, string.Join(", ", escalatedPermissions));
                    return ServiceResult.Fail<RoleSummary>("cannot_grant_unowned_permissions");
                }
            }

            role.Permissions = new System.Collections.ObjectModel.Collection<string>(permissions);
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Custom role updated: {RoleId} ({RoleName}) in org {OrgId} by user {UserId}",
            role.Id, role.Name, organizationId, actorUserId);

        return ServiceResult.Ok(new RoleSummary(
            role.Id.ToString("D"),
            role.Name,
            role.Description,
            false,
            role.Permissions));
    }

    /// <summary>
    /// Deletes a custom role.
    /// System roles cannot be deleted.
    /// Roles with active members cannot be deleted.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid organizationId,
        Guid actorUserId,
        string roleIdOrName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return ServiceResult.Fail<bool>("role_id_required");
        }

        var trimmed = roleIdOrName.Trim();

        // Check if it's a system role (cannot be deleted)
        if (RoleDefinitions.IsValidRole(trimmed))
        {
            return ServiceResult.Fail<bool>("cannot_delete_system_role");
        }

        OrganizationRole? role = null;

        if (Guid.TryParse(trimmed, out var roleId))
        {
            role = await _dbContext.OrganizationRoles
                .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId, ct);
        }
        else
        {
            var normalized = NormalizeName(trimmed);
            role = await _dbContext.OrganizationRoles
                .FirstOrDefaultAsync(r =>
                    r.OrganizationId == organizationId &&
                    r.NormalizedName == normalized, ct);
        }

        if (role is null)
        {
            return ServiceResult.Fail<bool>("role_not_found");
        }

        // Check if any active members have this role
        var hasMembers = await _dbContext.UserOrganizations
            .AsNoTracking()
            .AnyAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.Role == role.Name &&
                uo.LeftAt == null, ct);

        if (hasMembers)
        {
            return ServiceResult.Fail<bool>("role_has_active_members");
        }

        _dbContext.OrganizationRoles.Remove(role);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Custom role deleted: {RoleId} ({RoleName}) in org {OrgId} by user {UserId}",
            role.Id, role.Name, organizationId, actorUserId);

        return ServiceResult.Ok(true);
    }

    /// <summary>
    /// Gets all members assigned to a specific role.
    /// Works for both system roles and custom roles.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyCollection<RoleMember>>> GetMembersAsync(
        Guid organizationId,
        string roleIdOrName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return ServiceResult.Fail<IReadOnlyCollection<RoleMember>>("role_id_required");
        }

        var trimmed = roleIdOrName.Trim();
        string roleName;

        // Determine the role name to query
        if (Guid.TryParse(trimmed, out var roleId))
        {
            var role = await _dbContext.OrganizationRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId, ct);

            if (role is null)
            {
                return ServiceResult.Fail<IReadOnlyCollection<RoleMember>>("role_not_found");
            }

            roleName = role.Name;
        }
        else if (RoleDefinitions.IsValidRole(trimmed))
        {
            roleName = trimmed.ToLowerInvariant();
        }
        else
        {
            var normalized = NormalizeName(trimmed);
            var role = await _dbContext.OrganizationRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.OrganizationId == organizationId &&
                    r.NormalizedName == normalized, ct);

            if (role is null)
            {
                return ServiceResult.Fail<IReadOnlyCollection<RoleMember>>("role_not_found");
            }

            roleName = role.Name;
        }

        // Get all active members with this role
        // Note: EF.Functions.Collate or case-insensitive database collation handles case matching
        var members = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Include(uo => uo.User)
            .Where(uo =>
                uo.OrganizationId == organizationId &&
                EF.Functions.Like(uo.Role, roleName) &&
                uo.LeftAt == null)
            .Select(uo => new RoleMember(
                uo.UserId,
                uo.User.Email ?? string.Empty,
                uo.User.DisplayName,
                uo.JoinedAt))
            .OrderBy(m => m.Email)
            .ToListAsync(ct);

        return ServiceResult.Ok<IReadOnlyCollection<RoleMember>>(members);
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

    /// <summary>
    /// Resolves a role by ID or name, including its permissions for validation.
    /// Used for privilege escalation checks when assigning roles.
    /// </summary>
    private async Task<RoleResolutionWithPermissions?> ResolveRoleWithPermissionsAsync(
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
            return role is null
                ? null
                : new RoleResolutionWithPermissions(role.Name, false, role.Permissions);
        }

        if (RoleDefinitions.IsValidRole(trimmed))
        {
            var definition = RoleDefinitions.GetRole(trimmed);
            return new RoleResolutionWithPermissions(
                trimmed.ToLowerInvariant(),
                true,
                definition.ImpliedClaims);
        }

        var normalized = NormalizeName(trimmed);
        var custom = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(role =>
                role.OrganizationId == organizationId &&
                role.NormalizedName == normalized, ct);

        return custom is null
            ? null
            : new RoleResolutionWithPermissions(custom.Name, false, custom.Permissions);
    }

    private static string NormalizeName(string value)
        => value.Trim().ToUpperInvariant();

    private sealed record RoleResolution(string Name, bool IsSystem);
    private sealed record RoleResolutionWithPermissions(
        string Name,
        bool IsSystem,
        IReadOnlyCollection<string> Permissions);
}
