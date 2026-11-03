namespace Dhadgar.Contracts.Identity;

/// <summary>Claim names Identity issues and other services read. One source for both sides.</summary>
public static class TokenClaims
{
    /// <summary>The organization a token acts in; a Guid in "D" format. Nodes TenantScoped compares it with the route's organizationId.</summary>
    public const string OrganizationId = "org_id";

    /// <summary>"service" on client_credentials tokens.</summary>
    public const string PrincipalType = "principal_type";

    public const string ServicePrincipal = "service";
}
