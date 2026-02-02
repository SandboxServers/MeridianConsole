using System.Diagnostics;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Manages Windows Firewall rules for game server ports.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class executes netsh.exe commands to manage firewall rules.
/// All inputs are strictly validated before command execution to prevent command injection.
///
/// Validation requirements:
/// - Rule names: alphanumeric, spaces, hyphens, underscores only (max 256 chars)
/// - Ports: 1-65535 range
/// - Protocols: TCP or UDP only
///
/// This code runs on customer hardware with elevated privileges - all changes require security review.
/// </remarks>
internal sealed partial class FirewallManager
{
    private const int MaxRuleNameLength = 256;
    private const int MinPort = 1;
    private const int MaxPort = 65535;
    private const int CommandTimeoutSeconds = 30;

    private static readonly string[] AllowedProtocols = ["TCP", "UDP"];

    private readonly ILogger<FirewallManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public FirewallManager(ILogger<FirewallManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Regex pattern for validating firewall rule names.
    /// Only allows alphanumeric characters, spaces, hyphens, and underscores.
    /// </summary>
    /// <remarks>
    /// SECURITY: This pattern prevents command injection by rejecting any special characters
    /// that could be interpreted by the shell (quotes, semicolons, pipes, backticks, etc.).
    /// </remarks>
    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_]+$", RegexOptions.Compiled)]
    private static partial Regex RuleNamePattern();

    /// <summary>
    /// Creates an inbound firewall rule to allow traffic on a specific port.
    /// </summary>
    /// <param name="port">The port number to allow (1-65535).</param>
    /// <param name="ruleName">The name for the firewall rule.</param>
    /// <param name="protocol">The protocol (TCP or UDP). Defaults to TCP.</param>
    /// <returns>True if the rule was created successfully; false otherwise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when port is outside valid range.</exception>
    /// <exception cref="ArgumentException">Thrown when ruleName or protocol is invalid.</exception>
    public bool AllowInboundPort(int port, string ruleName, string protocol = "TCP")
    {
        ValidatePort(port);
        ValidateRuleName(ruleName);
        ValidateProtocol(protocol);

        _logger.LogInformation(
            "Creating inbound firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}",
            ruleName,
            port,
            protocol);

        // SECURITY: All parameters have been validated above before command construction
        var arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol={protocol} localport={port}";

        var result = ExecuteNetshCommand(arguments);

        if (result)
        {
            _logger.LogInformation(
                "Successfully created firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}",
                ruleName,
                port,
                protocol);
        }
        else
        {
            _logger.LogError(
                "Failed to create firewall rule: Name={RuleName}, Port={Port}, Protocol={Protocol}",
                ruleName,
                port,
                protocol);
        }

        return result;
    }

    /// <summary>
    /// Removes a firewall rule by name.
    /// </summary>
    /// <param name="ruleName">The name of the firewall rule to remove.</param>
    /// <returns>True if the rule was removed successfully; false otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown when ruleName is invalid.</exception>
    public bool RemoveRule(string ruleName)
    {
        ValidateRuleName(ruleName);

        _logger.LogInformation("Removing firewall rule: Name={RuleName}", ruleName);

        // SECURITY: ruleName has been validated above before command construction
        var arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"";

        var result = ExecuteNetshCommand(arguments);

        if (result)
        {
            _logger.LogInformation("Successfully removed firewall rule: Name={RuleName}", ruleName);
        }
        else
        {
            _logger.LogWarning("Failed to remove firewall rule (may not exist): Name={RuleName}", ruleName);
        }

        return result;
    }

    /// <summary>
    /// Checks if a firewall rule with the specified name exists.
    /// </summary>
    /// <param name="ruleName">The name of the firewall rule to check.</param>
    /// <returns>True if the rule exists; false otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown when ruleName is invalid.</exception>
    public bool RuleExists(string ruleName)
    {
        ValidateRuleName(ruleName);

        _logger.LogDebug("Checking if firewall rule exists: Name={RuleName}", ruleName);

        // SECURITY: ruleName has been validated above before command construction
        var arguments = $"advfirewall firewall show rule name=\"{ruleName}\"";

        var (exitCode, output, _) = ExecuteNetshCommandWithOutput(arguments);

        // netsh returns 0 when rule exists and outputs rule details
        // netsh returns 1 when rule doesn't exist with message "No rules match the specified criteria"
        var exists = exitCode == 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase);

        _logger.LogDebug("Firewall rule exists check: Name={RuleName}, Exists={Exists}", ruleName, exists);

        return exists;
    }

    /// <summary>
    /// Validates that the port number is within the valid range (1-65535).
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when port is outside valid range.</exception>
    private static void ValidatePort(int port)
    {
        if (port < MinPort || port > MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                $"Port must be between {MinPort} and {MaxPort}.");
        }
    }

    /// <summary>
    /// Validates that the protocol is either TCP or UDP.
    /// </summary>
    /// <param name="protocol">The protocol to validate.</param>
    /// <exception cref="ArgumentException">Thrown when protocol is not TCP or UDP.</exception>
    private static void ValidateProtocol(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol, nameof(protocol));

        if (!AllowedProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Protocol must be one of: {string.Join(", ", AllowedProtocols)}.",
                nameof(protocol));
        }
    }

    /// <summary>
    /// Validates that the rule name is safe for use in netsh commands.
    /// </summary>
    /// <param name="ruleName">The rule name to validate.</param>
    /// <exception cref="ArgumentException">Thrown when ruleName is null, empty, too long, or contains invalid characters.</exception>
    /// <remarks>
    /// SECURITY: This validation is critical to prevent command injection attacks.
    /// The regex pattern ensures only safe characters are allowed:
    /// - Alphanumeric characters (a-z, A-Z, 0-9)
    /// - Spaces
    /// - Hyphens (-)
    /// - Underscores (_)
    ///
    /// Any shell metacharacters (quotes, semicolons, pipes, backticks, etc.) will cause validation to fail.
    /// </remarks>
    private static void ValidateRuleName(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName, nameof(ruleName));

        if (ruleName.Length > MaxRuleNameLength)
        {
            throw new ArgumentException(
                $"Rule name must not exceed {MaxRuleNameLength} characters.",
                nameof(ruleName));
        }

        if (!RuleNamePattern().IsMatch(ruleName))
        {
            throw new ArgumentException(
                "Rule name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed.",
                nameof(ruleName));
        }
    }

    /// <summary>
    /// Executes a netsh command and returns whether it succeeded.
    /// </summary>
    /// <param name="arguments">The netsh command arguments.</param>
    /// <returns>True if the command exited with code 0; false otherwise.</returns>
    private bool ExecuteNetshCommand(string arguments)
    {
        var (exitCode, _, _) = ExecuteNetshCommandWithOutput(arguments);
        return exitCode == 0;
    }

    /// <summary>
    /// Executes a netsh command and returns the exit code and output.
    /// </summary>
    /// <param name="arguments">The netsh command arguments.</param>
    /// <returns>A tuple containing (exitCode, standardOutput, standardError).</returns>
    private (int ExitCode, string Output, string Error) ExecuteNetshCommandWithOutput(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // SECURITY: Use ArgumentList instead of Arguments for defense-in-depth.
            // Even though inputs are validated, ArgumentList provides additional protection
            // against command injection by passing arguments as discrete values.
            foreach (var arg in ParseNetshArguments(arguments))
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();

            // Read output before waiting to avoid deadlock on full buffers
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            var completed = process.WaitForExit(TimeSpan.FromSeconds(CommandTimeoutSeconds));

            if (!completed)
            {
                _logger.LogError("netsh command timed out after {Timeout} seconds", CommandTimeoutSeconds);
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }

                return (-1, string.Empty, "Command timed out");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogDebug(
                    "netsh command failed with exit code {ExitCode}. Output: {Output}, Error: {Error}",
                    process.ExitCode,
                    output,
                    error);
            }

            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception executing netsh command");
            return (-1, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Parses a netsh command string into individual arguments.
    /// Handles quoted strings correctly (e.g., name="My Rule Name").
    /// </summary>
    /// <param name="commandLine">The netsh command arguments as a single string.</param>
    /// <returns>A list of individual arguments.</returns>
    private static List<string> ParseNetshArguments(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
