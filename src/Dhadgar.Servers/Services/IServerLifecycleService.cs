namespace Dhadgar.Servers.Services;

public interface IServerLifecycleService
{
    /// <summary>
    /// Starts a server.
    /// </summary>
    Task<ServiceResult<bool>> StartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Stops a server gracefully.
    /// </summary>
    Task<ServiceResult<bool>> StopServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Restarts a server.
    /// </summary>
    Task<ServiceResult<bool>> RestartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Force-kills a server.
    /// </summary>
    Task<ServiceResult<bool>> KillServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions a server to a new status with validation.
    /// </summary>
    Task<ServiceResult<bool>> TransitionStatusAsync(
        Guid serverId,
        Data.Entities.ServerStatus newStatus,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions a server's power state.
    /// </summary>
    Task<ServiceResult<bool>> TransitionPowerStateAsync(
        Guid serverId,
        Data.Entities.ServerPowerState newPowerState,
        CancellationToken ct = default);
}
