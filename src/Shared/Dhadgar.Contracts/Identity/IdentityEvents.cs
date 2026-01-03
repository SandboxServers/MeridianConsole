namespace Dhadgar.Contracts.Identity;

public static class MembershipChangeTypes
{
    public const string Invited = "invited";
    public const string Accepted = "accepted";
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
