using System.Globalization;
using System.Text.RegularExpressions;

using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

#region Windows Firewall COM Enums

/// <summary>
/// Specifies the IP protocol used by a firewall rule.
/// </summary>
internal enum NetFwIpProtocol
{
    /// <summary>TCP protocol.</summary>
    Tcp = 6,

    /// <summary>UDP protocol.</summary>
    Udp = 17,
}

/// <summary>
/// Specifies the direction of network traffic for a firewall rule.
/// </summary>
internal enum NetFwRuleDirection
{
    /// <summary>Inbound traffic.</summary>
    In = 1,

    /// <summary>Outbound traffic.</summary>
    Out = 2,
}

/// <summary>
/// Specifies the action taken by a firewall rule.
/// </summary>
internal enum NetFwAction
{
    /// <summary>Block the connection.</summary>
    Block = 0,

    /// <summary>Allow the connection.</summary>
    Allow = 1,
}

/// <summary>
/// Specifies the firewall profile types.
/// </summary>
[Flags]
internal enum NetFwProfileType2
{
    /// <summary>Domain profile.</summary>
    Domain = 0x0001,

    /// <summary>Private profile.</summary>
    Private = 0x0002,

    /// <summary>Public profile.</summary>
    Public = 0x0004,

    /// <summary>All profiles.</summary>
    All = 0x7FFFFFFF,
}

#endregion

/// <summary>
/// Manages Windows Firewall rules for game server ports.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class manages firewall rules via an abstracted firewall operations interface.
/// All inputs are strictly validated before any operations to prevent injection attacks.
///
/// Validation requirements:
/// - Rule names: alphanumeric, spaces, hyphens, underscores only (max 256 chars)
/// - Ports: 1-65535 range
/// - Protocols: TCP or UDP only
///
/// This code runs on customer hardware with elevated privileges - all changes require security review.
/// </remarks>
internal sealed partial class FirewallManager : IDisposable
{
    private const int MaxRuleNameLength = 256;
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    private static readonly string[] AllowedProtocols = ["TCP", "UDP"];

    private readonly ILogger<FirewallManager> _logger;
    private readonly IFirewallOperations _firewallOperations;
    private readonly bool _ownsFirewallOperations;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallManager"/> class using the default COM-based implementation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public FirewallManager(ILogger<FirewallManager> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallManager"/> class with a custom firewall operations implementation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="firewallOperations">The firewall operations implementation, or null to use the default COM-based implementation.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public FirewallManager(ILogger<FirewallManager> logger, IFirewallOperations? firewallOperations)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        if (firewallOperations is null)
        {
            _firewallOperations = new ComFirewallOperations(_logger);
            _ownsFirewallOperations = true;
        }
        else
        {
            _firewallOperations = firewallOperations;
            _ownsFirewallOperations = false;
        }
    }

    /// <summary>
    /// Regex pattern for validating firewall rule names.
    /// Only allows alphanumeric characters, spaces, hyphens, and underscores.
    /// </summary>
    /// <remarks>
    /// SECURITY: This pattern prevents injection by rejecting any special characters
    /// that could be interpreted maliciously. Uses literal space instead of \s to
    /// prevent tabs/newlines/control characters.
    /// </remarks>
    [GeneratedRegex(@"^[a-zA-Z0-9 \-_]+$", RegexOptions.Compiled)]
    private static partial Regex RuleNamePattern();

    /// <summary>
    /// Creates an inbound firewall rule to allow traffic on a specific port.
    /// </summary>
    /// <param name="port">The port number to allow (1-65535).</param>
    /// <param name="ruleName">The name for the firewall rule.</param>
    /// <param name="protocol">The protocol (TCP or UDP). Defaults to TCP.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public Result<bool> AllowInboundPort(int port, string ruleName, string protocol = "TCP")
    {
        var validationResult = ValidateInputs(port, ruleName, protocol);
        if (validationResult.IsFailure)
        {
            return Result<bool>.Failure(validationResult.Error);
        }

        if (!_firewallOperations.IsAvailable)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        var description = string.Format(
            CultureInfo.InvariantCulture,
            "Meridian Console managed rule for port {0}/{1}",
            port,
            protocol);

        return _firewallOperations.AddRule(ruleName, description, protocol, port);
    }

    /// <summary>
    /// Removes a firewall rule by name.
    /// </summary>
    /// <param name="ruleName">The name of the firewall rule to remove.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public Result<bool> RemoveRule(string ruleName)
    {
        var validationResult = ValidateRuleNameInput(ruleName);
        if (validationResult.IsFailure)
        {
            return Result<bool>.Failure(validationResult.Error);
        }

        if (!_firewallOperations.IsAvailable)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        return _firewallOperations.RemoveRule(ruleName);
    }

    /// <summary>
    /// Checks if a firewall rule with the specified name exists.
    /// </summary>
    /// <param name="ruleName">The name of the firewall rule to check.</param>
    /// <returns>A result containing true if the rule exists, false if not, or a failure with an error message.</returns>
    public Result<bool> RuleExists(string ruleName)
    {
        var validationResult = ValidateRuleNameInput(ruleName);
        if (validationResult.IsFailure)
        {
            return Result<bool>.Failure(validationResult.Error);
        }

        if (!_firewallOperations.IsAvailable)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        return _firewallOperations.RuleExists(ruleName);
    }

    /// <summary>
    /// Validates all inputs for AllowInboundPort.
    /// </summary>
    /// <param name="port">The port to validate.</param>
    /// <param name="ruleName">The rule name to validate.</param>
    /// <param name="protocol">The protocol to validate.</param>
    /// <returns>A result indicating validation success or failure.</returns>
    private static Result ValidateInputs(int port, string ruleName, string protocol)
    {
        var portResult = ValidatePort(port);
        if (portResult.IsFailure)
        {
            return portResult;
        }

        var ruleNameResult = ValidateRuleName(ruleName);
        if (ruleNameResult.IsFailure)
        {
            return ruleNameResult;
        }

        return ValidateProtocol(protocol);
    }

    /// <summary>
    /// Validates the rule name only (for RemoveRule and RuleExists).
    /// </summary>
    /// <param name="ruleName">The rule name to validate.</param>
    /// <returns>A result indicating validation success or failure.</returns>
    private static Result ValidateRuleNameInput(string ruleName)
    {
        return ValidateRuleName(ruleName);
    }

    /// <summary>
    /// Validates that the port number is within the valid range (1-65535).
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <returns>A result indicating validation success or failure.</returns>
    private static Result ValidatePort(int port)
    {
        if (port < MinPort || port > MaxPort)
        {
            return Result.Failure(FormattableString.Invariant($"Port must be between {MinPort} and {MaxPort}. Provided: {port}"));
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that the protocol is either TCP or UDP.
    /// </summary>
    /// <param name="protocol">The protocol to validate.</param>
    /// <returns>A result indicating validation success or failure.</returns>
    private static Result ValidateProtocol(string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return Result.Failure("Protocol cannot be null or empty.");
        }

        if (!AllowedProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure($"Protocol must be one of: {string.Join(", ", AllowedProtocols)}. Provided: {protocol}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that the rule name is safe for use with the firewall API.
    /// </summary>
    /// <param name="ruleName">The rule name to validate.</param>
    /// <returns>A result indicating validation success or failure.</returns>
    /// <remarks>
    /// SECURITY: This validation is critical to prevent injection attacks.
    /// The regex pattern ensures only safe characters are allowed:
    /// - Alphanumeric characters (a-z, A-Z, 0-9)
    /// - Spaces
    /// - Hyphens (-)
    /// - Underscores (_)
    ///
    /// Any shell metacharacters or special characters will cause validation to fail.
    /// </remarks>
    private static Result ValidateRuleName(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return Result.Failure("Rule name cannot be null or empty.");
        }

        // SECURITY: Reject "all" keyword to prevent accidental deletion of all firewall rules
        if (string.Equals(ruleName, "all", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("Rule name 'all' is reserved and cannot be used.");
        }

        if (ruleName.Length > MaxRuleNameLength)
        {
            return Result.Failure(string.Format(
                CultureInfo.InvariantCulture,
                "Rule name must not exceed {0} characters. Provided length: {1}",
                MaxRuleNameLength,
                ruleName.Length));
        }

        if (!RuleNamePattern().IsMatch(ruleName))
        {
            return Result.Failure("Rule name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Disposes of resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Only dispose the firewall operations if we own it (created it ourselves)
        if (_ownsFirewallOperations && _firewallOperations is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }
}
