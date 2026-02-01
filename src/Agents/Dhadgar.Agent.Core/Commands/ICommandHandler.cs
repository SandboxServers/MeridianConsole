namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Interface for command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// The command type this handler processes.
    /// </summary>
    string CommandType { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    /// <param name="envelope">Command envelope containing metadata and payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command execution result.</returns>
    Task<CommandResult> ExecuteAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly-typed command handler interface.
/// </summary>
/// <typeparam name="TPayload">Type of the command payload.</typeparam>
public interface ICommandHandler<TPayload> : ICommandHandler
    where TPayload : class
{
    /// <summary>
    /// Execute the command with a typed payload.
    /// </summary>
    /// <param name="envelope">Command envelope containing metadata.</param>
    /// <param name="payload">Deserialized command payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command execution result.</returns>
    Task<CommandResult> ExecuteAsync(CommandEnvelope envelope, TPayload payload, CancellationToken cancellationToken = default);
}
