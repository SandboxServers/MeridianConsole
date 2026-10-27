using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Supplies the one-time enrollment token. The token is read on every enrollment attempt, so an operator
/// can correct a bad token without restarting the agent.
/// </summary>
public interface IEnrollmentTokenSource
{
    /// <summary>
    /// Returns the configured token. <c>Success(string.Empty)</c> means no token is configured (D-S232).
    /// A <c>Failure</c> carries a code only, never the token text.
    /// </summary>
    Result<string> GetToken();
}