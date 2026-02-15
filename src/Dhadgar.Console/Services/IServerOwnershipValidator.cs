namespace Dhadgar.Console.Services;

public interface IServerOwnershipValidator
{
    /// <summary>
    /// Verifies that a server belongs to the specified organization.
    /// </summary>
    Task<bool> ValidateOwnershipAsync(Guid serverId, Guid organizationId, CancellationToken ct = default);
}
