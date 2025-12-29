using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

    public MembershipService(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
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
            return ServiceResult<UserOrganization>.Fail("org_not_found");
        }

        if (!org.Settings.AllowMemberInvites)
        {
            return ServiceResult<UserOrganization>.Fail("invites_disabled");
        }

        var role = string.IsNullOrWhiteSpace(request.Role) ? "viewer" : request.Role!.Trim().ToLowerInvariant();
        if (!RoleDefinitions.IsValidRole(role))
        {
            return ServiceResult<UserOrganization>.Fail("invalid_role");
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
            return ServiceResult<UserOrganization>.Fail("user_not_found");
        }

        var activeMembersCount = await _dbContext.UserOrganizations
            .AsNoTracking()
            .CountAsync(uo => uo.OrganizationId == organizationId && uo.LeftAt == null && uo.IsActive, ct);

        if (activeMembersCount >= org.Settings.MaxMembers)
        {
            return ServiceResult<UserOrganization>.Fail("member_limit_reached");
        }

        var existingMembership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId && uo.UserId == user.Id && uo.LeftAt == null, ct);

        if (existingMembership is not null)
        {
            return ServiceResult<UserOrganization>.Fail(existingMembership.IsActive ? "already_member" : "invitation_exists");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var membership = new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            Role = role,
            IsActive = false,
            JoinedAt = now,
            InvitedByUserId = invitedByUserId
        };

        _dbContext.UserOrganizations.Add(membership);
        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<UserOrganization>.Ok(membership);
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
            return ServiceResult<UserOrganization>.Fail("invite_not_found");
        }

        if (membership.IsActive)
        {
            return ServiceResult<UserOrganization>.Ok(membership);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        membership.IsActive = true;
        membership.InvitationAcceptedAt = now;
        membership.JoinedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<UserOrganization>.Ok(membership);
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
            return ServiceResult<bool>.Fail("membership_not_found");
        }

        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (org is not null && org.OwnerId == targetUserId)
        {
            return ServiceResult<bool>.Fail("cannot_remove_owner");
        }

        membership.LeftAt = _timeProvider.GetUtcNow().UtcDateTime;
        membership.IsActive = false;

        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<UserOrganization>> AssignRoleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string role,
        CancellationToken ct = default)
    {
        if (!RoleDefinitions.IsValidRole(role))
        {
            return ServiceResult<UserOrganization>.Fail("invalid_role");
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
            return ServiceResult<UserOrganization>.Fail("actor_not_member");
        }

        if (!RoleDefinitions.CanAssignRole(actorMembership.Role, role))
        {
            return ServiceResult<UserOrganization>.Fail("forbidden_role_assignment");
        }

        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult<UserOrganization>.Fail("membership_not_found");
        }

        membership.Role = role;
        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<UserOrganization>.Ok(membership);
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
            return ServiceResult<UserOrganizationClaim>.Fail("claim_value_required");
        }

        var knownClaim = await _dbContext.ClaimDefinitions
            .AsNoTracking()
            .AnyAsync(cd => cd.Name == request.ClaimValue, ct);

        if (!knownClaim)
        {
            return ServiceResult<UserOrganizationClaim>.Fail("unknown_claim");
        }

        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult<UserOrganizationClaim>.Fail("membership_not_found");
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
            return ServiceResult<UserOrganizationClaim>.Ok(existing);
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

        return ServiceResult<UserOrganizationClaim>.Ok(claim);
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
            return ServiceResult<bool>.Fail("membership_not_found");
        }

        var claim = await _dbContext.UserOrganizationClaims
            .FirstOrDefaultAsync(c => c.Id == claimId && c.UserOrganizationId == membership.Id, ct);

        if (claim is null)
        {
            return ServiceResult<bool>.Fail("claim_not_found");
        }

        _dbContext.UserOrganizationClaims.Remove(claim);
        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<bool>.Ok(true);
    }
}
