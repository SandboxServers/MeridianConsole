namespace Dhadgar.Contracts.Identity;

public static class MembershipChangeTypes
{
    public const string Invited = "invited";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";
    public const string Expired = "expired";
    public const string Removed = "removed";
    public const string RoleAssigned = "role_assigned";
    public const string ClaimAdded = "claim_added";
    public const string ClaimRemoved = "claim_removed";
}

public record UserAuthenticated(
    Guid UserId,
    Guid OrganizationId,
    string ExternalAuthId,
    string Email,
    string? ClientApp,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset OccurredAtUtc);

public record OrgMembershipChanged(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    string ChangeType,
    string? Role,
    string? ClaimType,
    string? ClaimValue,
    string? ResourceType,
    Guid? ResourceId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);

public record UserDeactivated(
    Guid UserId,
    string ExternalAuthId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a new organization is created.
/// </summary>
public record OrganizationCreated(
    Guid OrganizationId,
    Guid OwnerId,
    string Name,
    string Slug,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when an organization is deleted (soft deleted).
/// </summary>
public record OrganizationDeleted(
    Guid OrganizationId,
    Guid? DeletedByUserId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when organization details are updated.
/// </summary>
public record OrganizationUpdated(
    Guid OrganizationId,
    string Name,
    Guid? UpdatedByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a user's permissions change (role assignment, custom claims, etc.).
/// Other services can use this to invalidate cached permissions.
/// </summary>
public record UserPermissionsChanged(
    Guid UserId,
    Guid OrganizationId,
    IReadOnlyCollection<string> NewPermissions,
    string ChangeReason,
    Guid? ChangedByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a user leaves an organization (removed or left voluntarily).
/// </summary>
public record MemberLeftOrganization(
    Guid UserId,
    Guid OrganizationId,
    string Reason,
    Guid? RemovedByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a custom role is created in an organization.
/// </summary>
public record CustomRoleCreated(
    Guid RoleId,
    Guid OrganizationId,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a custom role is deleted.
/// </summary>
public record CustomRoleDeleted(
    Guid RoleId,
    Guid OrganizationId,
    string RoleName,
    Guid DeletedByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when an OAuth account is linked to a user.
/// </summary>
public record OAuthAccountLinked(
    Guid UserId,
    string Provider,
    string ProviderAccountId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when an OAuth account is unlinked from a user.
/// </summary>
public record OAuthAccountUnlinked(
    Guid UserId,
    string Provider,
    string ProviderAccountId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a user rejects a membership invitation.
/// </summary>
public record InvitationRejected(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when an inviter withdraws/revokes a pending invitation.
/// </summary>
public record InvitationWithdrawn(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    Guid WithdrawnByUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a pending invitation expires without being accepted.
/// </summary>
public record InvitationExpired(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a user requests their own account deletion.
/// </summary>
public record UserDeletionRequested(
    Guid UserId,
    string Email,
    DateTime DeletionScheduledAt,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when organization ownership is transferred to another member.
/// </summary>
public record OrganizationOwnershipTransferred(
    Guid OrganizationId,
    Guid PreviousOwnerId,
    Guid NewOwnerId,
    Guid TransferredByUserId,
    DateTimeOffset OccurredAtUtc);
