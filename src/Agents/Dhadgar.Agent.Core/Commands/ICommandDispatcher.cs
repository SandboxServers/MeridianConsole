using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Dispatches commands to appropriate handlers.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// Dispatch a command to the appropriate handler.
    /// </summary>
    /// <param name="envelope">Command envelope to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing command execution result on success.</returns>
    Task<Result<CommandResult>> DispatchAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a command handler.
    /// </summary>
    /// <param name="handler">Handler to register.</param>
    void RegisterHandler(ICommandHandler handler);

    /// <summary>
    /// Check if a handler is registered for a command type.
    /// </summary>
    /// <param name="commandType">Command type to check.</param>
    /// <returns>True if a handler is registered.</returns>
    bool HasHandler(string commandType);
}
