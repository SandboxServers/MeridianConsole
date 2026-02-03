using System.Globalization;
using System.Runtime.InteropServices;

using Dhadgar.Shared.Results;

using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Default implementation of <see cref="IFirewallOperations"/> using Windows Firewall COM API.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class manages firewall rules via the Windows Firewall COM API (INetFwPolicy2).
/// This code runs on customer hardware with elevated privileges - all changes require security review.
///
/// Benefits of COM API over netsh.exe:
/// - No command-line parsing issues
/// - No localization/language issues with netsh output
/// - Direct API access is more reliable
/// - Better error information from COM exceptions
/// </remarks>
internal sealed class ComFirewallOperations : IFirewallOperations, IDisposable
{
    private readonly ILogger _logger;
    private readonly object? _firewallPolicy;
    private readonly dynamic? _rules;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComFirewallOperations"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ComFirewallOperations(ILogger logger)
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

    /// <inheritdoc/>
    public bool IsAvailable => OperatingSystem.IsWindows() && _firewallPolicy is not null && _rules is not null;

    /// <inheritdoc/>
    public Result<bool> AddRule(string ruleName, string description, string protocol, int port)
    {
        if (!IsAvailable)
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
                dynamicRule.Description = description;
                dynamicRule.Protocol = protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase)
                    ? (int)NetFwIpProtocol.Tcp
                    : (int)NetFwIpProtocol.Udp;
                dynamicRule.LocalPorts = port.ToString(CultureInfo.InvariantCulture);
                dynamicRule.Action = (int)NetFwAction.Allow;
                dynamicRule.Direction = (int)NetFwRuleDirection.In;
                dynamicRule.Enabled = true;
                dynamicRule.Profiles = (int)NetFwProfileType2.All;

                // Add the rule to the firewall (null already checked via IsAvailable)
                _rules!.Add(rule);

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

    /// <inheritdoc/>
    public Result<bool> RemoveRule(string ruleName)
    {
        if (!IsAvailable)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        _logger.LogInformation("Removing firewall rule: Name={RuleName}", ruleName);

        try
        {
            // Null already checked via IsAvailable
            _rules!.Remove(ruleName);

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

    /// <inheritdoc/>
    public Result<bool> RuleExists(string ruleName)
    {
        if (!IsAvailable)
        {
            return Result<bool>.Failure("Windows Firewall API is not available on this system.");
        }

        _logger.LogDebug("Checking if firewall rule exists: Name={RuleName}", ruleName);

        object? rule = null;
        try
        {
            // Try to get the rule by name - throws if not found (null already checked via IsAvailable)
            rule = _rules!.Item(ruleName);

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
