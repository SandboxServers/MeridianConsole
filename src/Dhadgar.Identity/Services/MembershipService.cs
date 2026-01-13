using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Identity.Services;

public sealed record MemberSummary(
    Guid UserId,
    Guid OrganizationId,
    string Email,
    string Role,
    bool IsActive,
    DateTime JoinedAt);

public sealed record MemberInviteRequest(
    Guid? UserId,
    string? Email,
    string? Role);

public sealed record MemberRoleRequest(string Role);

public sealed record MemberClaimRequest(
    ClaimType ClaimType,
    string ClaimValue,
    string? ResourceType,
    Guid? ResourceId,
    DateTime? ExpiresAt);

public sealed class MembershipService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIdentityEventPublisher _eventPublisher;
    private readonly ILogger<MembershipService> _logger;

    public MembershipService(
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        IIdentityEventPublisher eventPublisher,
        ILogger<MembershipService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<MemberSummary>> ListMembersAsync(Guid organizationId, CancellationToken ct = default)
    {
        var members = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == organizationId && uo.LeftAt == null)
            .Join(_dbContext.Users.AsNoTracking(),
                membership => membership.UserId,
                user => user.Id,
                (membership, user) => new MemberSummary(
                    membership.UserId,
                    membership.OrganizationId,
                    user.Email,
                    membership.Role,
                    membership.IsActive,
                    membership.JoinedAt))
            .ToListAsync(ct);

        return members;
    }

    public async Task<ServiceResult<UserOrganization>> InviteAsync(
        Guid organizationId,
        Guid invitedByUserId,
        MemberInviteRequest request,
        CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null, ct);

        if (org is null)
        {
            return ServiceResult.Fail<UserOrganization>("org_not_found");
        }

        if (!org.Settings.AllowMemberInvites)
        {
            return ServiceResult.Fail<UserOrganization>("invites_disabled");
        }

        var roleInput = string.IsNullOrWhiteSpace(request.Role) ? "viewer" : request.Role!;
        var roleResolution = await ResolveRoleAsync(organizationId, roleInput, ct);
        if (roleResolution is null)
        {
            return ServiceResult.Fail<UserOrganization>("invalid_role");
        }

        User? user = null;
        if (request.UserId.HasValue)
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == request.UserId.Value, ct);
        }
        else if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim();
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        }

        if (user is null)
        {
            return ServiceResult.Fail<UserOrganization>("user_not_found");
        }

        var activeMembersCount = await _dbContext.UserOrganizations
            .AsNoTracking()
            .CountAsync(uo => uo.OrganizationId == organizationId && uo.LeftAt == null && uo.IsActive, ct);

        if (activeMembersCount >= org.Settings.MaxMembers)
        {
            return ServiceResult.Fail<UserOrganization>("member_limit_reached");
        }

        var existingMembership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId && uo.UserId == user.Id && uo.LeftAt == null, ct);

        if (existingMembership is not null)
        {
            return ServiceResult.Fail<UserOrganization>(existingMembership.IsActive ? "already_member" : "invitation_exists");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var membership = new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            Role = roleResolution.Name,
            IsActive = false,
            JoinedAt = now,
            InvitedByUserId = invitedByUserId
        };

        _dbContext.UserOrganizations.Add(membership);
        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            user.Id,
            membership.Id,
            MembershipChangeTypes.Invited,
            membership.Role,
            null,
            null,
            null,
            null,
            invitedByUserId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(membership);
    }

    public async Task<ServiceResult<UserOrganization>> AcceptInviteAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<UserOrganization>("invite_not_found");
        }

        if (membership.IsActive)
        {
            return ServiceResult.Ok(membership);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        membership.IsActive = true;
        membership.InvitationAcceptedAt = now;
        membership.JoinedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.Accepted,
            membership.Role,
            null,
            null,
            null,
            null,
            userId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(membership);
    }

    public async Task<ServiceResult<bool>> RemoveMemberAsync(
        Guid organizationId,
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<bool>("membership_not_found");
        }

        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (org is not null && org.OwnerId == targetUserId)
        {
            return ServiceResult.Fail<bool>("cannot_remove_owner");
        }

        membership.LeftAt = _timeProvider.GetUtcNow().UtcDateTime;
        membership.IsActive = false;

        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.Removed,
            membership.Role,
            null,
            null,
            null,
            null,
            null,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<UserOrganization>> AssignRoleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string role,
        CancellationToken ct = default)
    {
        var roleResolution = await ResolveRoleAsync(organizationId, role, ct);
        if (roleResolution is null)
        {
            return ServiceResult.Fail<UserOrganization>("invalid_role");
        }

        var actorMembership = await _dbContext.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == actorUserId &&
                uo.LeftAt == null &&
                uo.IsActive,
                ct);

        if (actorMembership is null)
        {
            return ServiceResult.Fail<UserOrganization>("actor_not_member");
        }

        if (roleResolution.IsSystem &&
            RoleDefinitions.IsValidRole(actorMembership.Role) &&
            !RoleDefinitions.CanAssignRole(actorMembership.Role, roleResolution.Name))
        {
            return ServiceResult.Fail<UserOrganization>("forbidden_role_assignment");
        }

        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<UserOrganization>("membership_not_found");
        }

        membership.Role = roleResolution.Name;
        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.RoleAssigned,
            membership.Role,
            null,
            null,
            null,
            null,
            actorUserId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(membership);
    }

    public async Task<ServiceResult<UserOrganizationClaim>> AddClaimAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        MemberClaimRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClaimValue))
        {
            return ServiceResult.Fail<UserOrganizationClaim>("claim_value_required");
        }

        var knownClaim = await _dbContext.ClaimDefinitions
            .AsNoTracking()
            .AnyAsync(cd => cd.Name == request.ClaimValue, ct);

        if (!knownClaim)
        {
            return ServiceResult.Fail<UserOrganizationClaim>("unknown_claim");
        }

        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<UserOrganizationClaim>("membership_not_found");
        }

        var existing = await _dbContext.UserOrganizationClaims
            .FirstOrDefaultAsync(c =>
                c.UserOrganizationId == membership.Id &&
                c.ClaimType == request.ClaimType &&
                c.ClaimValue == request.ClaimValue &&
                c.ResourceType == request.ResourceType &&
                c.ResourceId == request.ResourceId,
                ct);

        if (existing is not null)
        {
            return ServiceResult.Ok(existing);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var claim = new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = request.ClaimType,
            ClaimValue = request.ClaimValue,
            ResourceType = request.ResourceType,
            ResourceId = request.ResourceId,
            ExpiresAt = request.ExpiresAt,
            GrantedAt = now,
            GrantedByUserId = actorUserId
        };

        _dbContext.UserOrganizationClaims.Add(claim);
        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.ClaimAdded,
            membership.Role,
            request.ClaimType.ToString(),
            request.ClaimValue,
            request.ResourceType,
            request.ResourceId,
            actorUserId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(claim);
    }

    public async Task<ServiceResult<bool>> RemoveClaimAsync(
        Guid organizationId,
        Guid targetUserId,
        Guid claimId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<bool>("membership_not_found");
        }

        var claim = await _dbContext.UserOrganizationClaims
            .FirstOrDefaultAsync(c => c.Id == claimId && c.UserOrganizationId == membership.Id, ct);

        if (claim is null)
        {
            return ServiceResult.Fail<bool>("claim_not_found");
        }

        _dbContext.UserOrganizationClaims.Remove(claim);
        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.ClaimRemoved,
            membership.Role,
            claim.ClaimType.ToString(),
            claim.ClaimValue,
            claim.ResourceType,
            claim.ResourceId,
            null,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(true);
    }

    private async Task PublishMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct)
    {
        try
        {
            await _eventPublisher.PublishOrgMembershipChangedAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish OrgMembershipChanged event for user {UserId} in org {OrganizationId}",
                message.UserId,
                message.OrganizationId);
        }
    }

    private async Task<RoleResolution?> ResolveRoleAsync(
        Guid organizationId,
        string roleInput,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleInput))
        {
            return null;
        }

        var trimmed = roleInput.Trim();
        if (RoleDefinitions.IsValidRole(trimmed))
        {
            return new RoleResolution(trimmed.ToLowerInvariant(), true);
        }

        var normalized = trimmed.ToUpperInvariant();
        var customRole = await _dbContext.OrganizationRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(role =>
                role.OrganizationId == organizationId &&
                role.NormalizedName == normalized, ct);

        return customRole is null
            ? null
            : new RoleResolution(customRole.Name, false);
    }

    private sealed record RoleResolution(string Name, bool IsSystem);
}
