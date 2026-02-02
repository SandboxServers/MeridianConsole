using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Dhadgar.Shared.Results;

using Microsoft.CSharp.RuntimeBinder;
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
/// Manages Windows Firewall rules for game server ports using the native COM API.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class manages firewall rules via the Windows Firewall COM API (INetFwPolicy2).
/// All inputs are strictly validated before any operations to prevent injection attacks.
///
/// Validation requirements:
/// - Rule names: alphanumeric, spaces, hyphens, underscores only (max 256 chars)
/// - Ports: 1-65535 range
/// - Protocols: TCP or UDP only
///
/// This code runs on customer hardware with elevated privileges - all changes require security review.
///
/// Benefits of COM API over netsh.exe:
/// - No command-line parsing issues
/// - No localization/language issues with netsh output
/// - Direct API access is more reliable
/// - Better error information from COM exceptions
/// </remarks>
internal sealed partial class FirewallManager : IDisposable
{
    private const int MaxRuleNameLength = 256;
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    private static readonly string[] AllowedProtocols = ["TCP", "UDP"];

    private readonly ILogger<FirewallManager> _logger;
    private readonly object? _firewallPolicy;
    private readonly dynamic? _rules;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public FirewallManager(ILogger<FirewallManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // Initialize COM firewall policy on Windows only
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Use local variables until all steps succeed to ensure atomic initialization
                var policyTypeLocal = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyTypeLocal is null)
                {
                    _logger.LogWarning("Windows Firewall policy COM type not available");
                    return;
                }

                var firewallPolicyLocal = Activator.CreateInstance(policyTypeLocal);
                if (firewallPolicyLocal is null)
                {
                    _logger.LogWarning("Failed to create Windows Firewall policy instance");
                    return;
                }

                // Get the Rules collection - this can throw RuntimeBinderException or COMException
                var rulesLocal = ((dynamic)firewallPolicyLocal).Rules;

                // All steps succeeded - assign to fields
                _firewallPolicy = firewallPolicyLocal;
                _rules = rulesLocal;
            }
            catch (COMException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to initialize Windows Firewall COM API: {Message} (0x{HResult:X8})",
                    ex.Message,
                    ex.HResult);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Access denied initializing Windows Firewall COM API. Ensure the agent runs with administrator privileges.");
            }
            catch (RuntimeBinderException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to access Windows Firewall Rules collection: {Message}",
                    ex.Message);
            }
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

        if (!OperatingSystem.IsWindows() || _firewallPolicy is null || _rules is null)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        _logger.LogInformation(
            "Creating inbound firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}",
            ruleName,
            port,
            protocol);

        try
        {
            // Create new firewall rule via COM
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (ruleType is null)
            {
                return Result<bool>.Failure("Windows Firewall rule COM type not available.");
            }

            var rule = Activator.CreateInstance(ruleType);
            if (rule is null)
            {
                return Result<bool>.Failure("Failed to create firewall rule instance.");
            }

            try
            {
                dynamic dynamicRule = rule;

                // Configure the rule
                dynamicRule.Name = ruleName;
                dynamicRule.Description = string.Format(
                    CultureInfo.InvariantCulture,
                    "Meridian Console managed rule for port {0}/{1}",
                    port,
                    protocol);
                dynamicRule.Protocol = protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase)
                    ? (int)NetFwIpProtocol.Tcp
                    : (int)NetFwIpProtocol.Udp;
                dynamicRule.LocalPorts = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                dynamicRule.Action = (int)NetFwAction.Allow;
                dynamicRule.Direction = (int)NetFwRuleDirection.In;
                dynamicRule.Enabled = true;
                dynamicRule.Profiles = (int)NetFwProfileType2.All;

                // Add the rule to the firewall
                _rules.Add(rule);

                _logger.LogInformation(
                    "Successfully created firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}",
                    ruleName,
                    port,
                    protocol);

                return Result<bool>.Success(true);
            }
            finally
            {
                Marshal.ReleaseComObject(rule);
            }
        }
        catch (COMException ex)
        {
            _logger.LogError(
                ex,
                "COM error creating firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}, HResult={HResult}",
                ruleName,
                port,
                protocol,
                ex.HResult);

            return Result<bool>.Failure($"Failed to create firewall rule: {ex.Message} (0x{ex.HResult:X8})");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(
                ex,
                "Access denied creating firewall rule: Name={RuleName}. Ensure the agent runs with administrator privileges.",
                ruleName);

            return Result<bool>.Failure("Access denied. Administrator privileges are required to modify firewall rules.");
        }
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

        if (!OperatingSystem.IsWindows() || _firewallPolicy is null || _rules is null)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        _logger.LogInformation("Removing firewall rule: Name={RuleName}", ruleName);

        try
        {
            _rules.Remove(ruleName);

            _logger.LogInformation("Successfully removed firewall rule: Name={RuleName}", ruleName);

            return Result<bool>.Success(true);
        }
        catch (COMException ex) when (IsRuleNotFoundError(ex))
        {
            _logger.LogWarning("Firewall rule not found: Name={RuleName}", ruleName);
            return Result<bool>.Failure($"Firewall rule '{ruleName}' not found.");
        }
        catch (COMException ex)
        {
            _logger.LogError(
                ex,
                "COM error removing firewall rule: Name={RuleName}, HResult={HResult}",
                ruleName,
                ex.HResult);

            return Result<bool>.Failure($"Failed to remove firewall rule: {ex.Message} (0x{ex.HResult:X8})");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(
                ex,
                "Access denied removing firewall rule: Name={RuleName}. Ensure the agent runs with administrator privileges.",
                ruleName);

            return Result<bool>.Failure("Access denied. Administrator privileges are required to modify firewall rules.");
        }
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

        if (!OperatingSystem.IsWindows() || _firewallPolicy is null || _rules is null)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        _logger.LogDebug("Checking if firewall rule exists: Name={RuleName}", ruleName);

        object? rule = null;
        try
        {
            // Try to get the rule by name - throws if not found
            rule = _rules.Item(ruleName);

            _logger.LogDebug("Firewall rule exists: Name={RuleName}, Exists=True", ruleName);
            return Result<bool>.Success(true);
        }
        catch (COMException ex) when (IsRuleNotFoundError(ex))
        {
            _logger.LogDebug("Firewall rule exists: Name={RuleName}, Exists=False", ruleName);
            return Result<bool>.Success(false);
        }
        catch (COMException ex)
        {
            _logger.LogError(
                ex,
                "COM error checking firewall rule: Name={RuleName}, HResult={HResult}",
                ruleName,
                ex.HResult);

            return Result<bool>.Failure($"Failed to check firewall rule: {ex.Message} (0x{ex.HResult:X8})");
        }
        finally
        {
            // Release the COM object to avoid RCW accumulation
            if (rule is not null)
            {
                try
                {
                    Marshal.ReleaseComObject(rule);
                }
                catch (InvalidComObjectException)
                {
                    // Object may have already been released
                }
            }
        }
    }

    /// <summary>
    /// Determines if a COM exception indicates the rule was not found.
    /// </summary>
    /// <param name="ex">The COM exception to check.</param>
    /// <returns>True if the error indicates rule not found; otherwise, false.</returns>
    private static bool IsRuleNotFoundError(COMException ex)
    {
        // HRESULT 0x80070002 = ERROR_FILE_NOT_FOUND (rule not found)
        // HRESULT 0x80004005 = E_FAIL (sometimes returned for missing rules)
        return ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x80004005);
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
    /// Disposes of COM resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_rules is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_rules);
            }
            catch (InvalidComObjectException)
            {
                // Object may have already been released
            }
        }

        if (_firewallPolicy is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_firewallPolicy);
            }
            catch (InvalidComObjectException)
            {
                // Object may have already been released
            }
        }

        _disposed = true;
    }
}
