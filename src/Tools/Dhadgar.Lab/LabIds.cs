namespace Dhadgar.Lab;

/// <summary>
/// Fixed identities the lab seeds into product data. Never used outside the lab.
/// </summary>
public static class LabIds
{
    public static readonly Guid OrganizationId = new("4d4c4142-0000-4000-8000-000000000001");
    public static readonly Guid TenantBOrganizationId = new("4d4c4142-0000-4000-8000-000000000002"); // L56 twin variant tenant
    public const string CreatedByUserId = "meridian-lab";
}
