using Dhadgar.Shared.Results;

namespace Dhadgar.Servers.Services;

public interface IServerLifecycleService
{
    /// <summary>
    /// Starts a server.
    /// </summary>
    Task<Result<bool>> StartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Stops a server gracefully.
    /// </summary>
    Task<Result<bool>> StopServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Restarts a server.
    /// </summary>
    Task<Result<bool>> RestartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Force-kills a server.
    /// </summary>
    Task<Result<bool>> KillServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions a server to a new status with validation, scoped to the specified organization.
    /// </summary>
    Task<Result<bool>> TransitionStatusAsync(
        Guid organizationId,
        Guid serverId,
        Data.Entities.ServerStatus newStatus,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions a server's power state, scoped to the specified organization.
    /// </summary>
    Task<Result<bool>> TransitionPowerStateAsync(
        Guid organizationId,
        Guid serverId,
        Data.Entities.ServerPowerState newPowerState,
        CancellationToken ct = default);
}
