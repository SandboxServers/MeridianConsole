using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Validates incoming commands before execution.
/// </summary>
public interface ICommandValidator
{
    /// <summary>
    /// Validate a command envelope.
    /// </summary>
    /// <param name="envelope">Command envelope to validate.</param>
    /// <returns>Validation result.</returns>
    Result<CommandEnvelope> Validate(CommandEnvelope? envelope);
}
