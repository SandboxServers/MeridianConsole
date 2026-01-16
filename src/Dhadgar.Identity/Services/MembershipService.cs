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

/// <summary>
/// Summary of a pending invitation for a user.
/// </summary>
public sealed record PendingInvitationSummary(
    Guid MembershipId,
    Guid OrganizationId,
    string OrganizationName,
    string Role,
    Guid? InvitedByUserId,
    string? InvitedByEmail,
    DateTime InvitedAt,
    DateTime? ExpiresAt);

/// <summary>
/// Options for invitation behavior.
/// </summary>
public sealed class InvitationOptions
{
    /// <summary>
    /// Default invitation expiration in days. Set to 0 for no expiration.
    /// </summary>
    public int DefaultExpirationDays { get; set; } = 7;
}

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
        var expiresAt = now.AddDays(7); // Default 7-day expiration

        var membership = new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            Role = roleResolution.Name,
            IsActive = false,
            JoinedAt = now,
            InvitedByUserId = invitedByUserId,
            InvitationExpiresAt = expiresAt
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

        // Check if invitation has expired
        if (membership.InvitationExpiresAt.HasValue && membership.InvitationExpiresAt.Value < now)
        {
            return ServiceResult.Fail<UserOrganization>("invite_expired");
        }

        membership.IsActive = true;
        membership.InvitationAcceptedAt = now;
        membership.InvitationExpiresAt = null; // Clear expiration once accepted
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

    /// <summary>
    /// Get all pending invitations for a user across all organizations.
    /// </summary>
    public async Task<IReadOnlyCollection<PendingInvitationSummary>> GetPendingInvitationsForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var invitations = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo =>
                uo.UserId == userId &&
                !uo.IsActive &&
                uo.LeftAt == null &&
                (uo.InvitationExpiresAt == null || uo.InvitationExpiresAt > now))
            .Join(_dbContext.Organizations.AsNoTracking(),
                uo => uo.OrganizationId,
                o => o.Id,
                (uo, o) => new { Membership = uo, Organization = o })
            .Select(x => new PendingInvitationSummary(
                x.Membership.Id,
                x.Membership.OrganizationId,
                x.Organization.Name,
                x.Membership.Role,
                x.Membership.InvitedByUserId,
                null, // Will be filled separately if needed
                x.Membership.JoinedAt,
                x.Membership.InvitationExpiresAt))
            .ToListAsync(ct);

        return invitations;
    }

    /// <summary>
    /// Reject a pending invitation (user declines to join).
    /// </summary>
    public async Task<ServiceResult<bool>> RejectInviteAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                !uo.IsActive &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<bool>("invite_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Check if already expired
        if (membership.InvitationExpiresAt.HasValue && membership.InvitationExpiresAt.Value < now)
        {
            return ServiceResult.Fail<bool>("invite_expired");
        }

        membership.LeftAt = now;
        membership.IsActive = false;

        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.Rejected,
            membership.Role,
            null,
            null,
            null,
            null,
            userId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(true);
    }

    /// <summary>
    /// Withdraw a pending invitation (inviter revokes the invite).
    /// </summary>
    public async Task<ServiceResult<bool>> WithdrawInviteAsync(
        Guid organizationId,
        Guid targetUserId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == targetUserId &&
                !uo.IsActive &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<bool>("invite_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        membership.LeftAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(new OrgMembershipChanged(
            organizationId,
            membership.UserId,
            membership.Id,
            MembershipChangeTypes.Withdrawn,
            membership.Role,
            null,
            null,
            null,
            null,
            actorUserId,
            _timeProvider.GetUtcNow()), ct);

        return ServiceResult.Ok(true);
    }

    /// <summary>
    /// Mark expired invitations as left (called by cleanup service).
    /// </summary>
    public async Task<int> MarkExpiredInvitationsAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var expiredCount = await _dbContext.UserOrganizations
            .Where(uo =>
                !uo.IsActive &&
                uo.LeftAt == null &&
                uo.InvitationExpiresAt != null &&
                uo.InvitationExpiresAt < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LeftAt, now),
                ct);

        return expiredCount;
    }

    /// <summary>
    /// Bulk invite multiple users to an organization.
    /// Returns per-item results (partial success is possible).
    /// </summary>
    public async Task<BulkOperationResult<Guid>> BulkInviteAsync(
        Guid organizationId,
        Guid invitedByUserId,
        IReadOnlyCollection<MemberInviteRequest> requests,
        CancellationToken ct = default)
    {
        const int maxBulkSize = 50;

        if (requests.Count == 0)
        {
            return new BulkOperationResult<Guid>([], []);
        }

        if (requests.Count > maxBulkSize)
        {
            var error = new BulkItemError<Guid>(Guid.Empty, "too_many_requests", $"Maximum {maxBulkSize} invites per request");
            return new BulkOperationResult<Guid>([], [error]);
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkItemError<Guid>>();

        foreach (var request in requests)
        {
            var itemId = request.UserId ?? Guid.Empty;
            try
            {
                var result = await InviteAsync(organizationId, invitedByUserId, request, ct);
                if (result.Success && result.Value is not null)
                {
                    succeeded.Add(result.Value.UserId);
                }
                else
                {
                    failed.Add(new BulkItemError<Guid>(itemId, result.Error ?? "unknown_error", null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk invite for {Email}", request.Email);
                failed.Add(new BulkItemError<Guid>(itemId, "internal_error", ex.Message));
            }
        }

        return new BulkOperationResult<Guid>(succeeded, failed);
    }

    /// <summary>
    /// Bulk remove multiple members from an organization.
    /// Returns per-item results (partial success is possible).
    /// </summary>
    public async Task<BulkOperationResult<Guid>> BulkRemoveAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken ct = default)
    {
        const int maxBulkSize = 50;

        if (memberIds.Count == 0)
        {
            return new BulkOperationResult<Guid>([], []);
        }

        if (memberIds.Count > maxBulkSize)
        {
            var error = new BulkItemError<Guid>(Guid.Empty, "too_many_requests", $"Maximum {maxBulkSize} removals per request");
            return new BulkOperationResult<Guid>([], [error]);
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkItemError<Guid>>();

        foreach (var memberId in memberIds)
        {
            try
            {
                var result = await RemoveMemberAsync(organizationId, memberId, ct);
                if (result.Success)
                {
                    succeeded.Add(memberId);
                }
                else
                {
                    failed.Add(new BulkItemError<Guid>(memberId, result.Error ?? "unknown_error", null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk remove for member {MemberId}", memberId);
                failed.Add(new BulkItemError<Guid>(memberId, "internal_error", ex.Message));
            }
        }

        return new BulkOperationResult<Guid>(succeeded, failed);
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
