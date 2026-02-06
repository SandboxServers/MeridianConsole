using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Abstraction for Windows Firewall operations to enable testability.
/// </summary>
/// <remarks>
/// This interface abstracts the COM-based firewall operations, allowing tests
/// to verify command formation and logic without executing real firewall changes.
/// </remarks>
internal interface IFirewallOperations
{
    /// <summary>
    /// Gets a value indicating whether the firewall API is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Adds a firewall rule with the specified configuration.
    /// </summary>
    /// <param name="ruleName">The name of the rule.</param>
    /// <param name="description">The rule description.</param>
    /// <param name="protocol">The protocol (TCP or UDP).</param>
    /// <param name="port">The port number.</param>
    /// <returns>A result indicating success or failure.</returns>
    Result<bool> AddRule(string ruleName, string description, string protocol, int port);

    /// <summary>
    /// Removes a firewall rule by name.
    /// </summary>
    /// <param name="ruleName">The name of the rule to remove.</param>
    /// <returns>A result indicating success or failure.</returns>
    Result<bool> RemoveRule(string ruleName);

    /// <summary>
    /// Checks if a firewall rule exists.
    /// </summary>
    /// <param name="ruleName">The name of the rule to check.</param>
    /// <returns>A result containing true if the rule exists, false if not.</returns>
    Result<bool> RuleExists(string ruleName);
}
