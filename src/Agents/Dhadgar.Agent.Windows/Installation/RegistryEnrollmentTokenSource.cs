using System.Security;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Reads the one-time enrollment token from <c>HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken</c> (D-S232).
/// </summary>
/// <remarks>
/// SECURITY: The key is opened read-only. The token is never logged; failures carry a code and a fixed message only.
/// A missing key or value means no token is configured, which is not an error.
/// </remarks>
public sealed class RegistryEnrollmentTokenSource : IEnrollmentTokenSource
{
    private readonly ILogger<RegistryEnrollmentTokenSource> _logger;

    /// <param name="logger">Logger for read failures. Never receives the token value.</param>
    public RegistryEnrollmentTokenSource(ILogger<RegistryEnrollmentTokenSource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Result<string> GetToken()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AgentRegistry.KeyPath, writable: false);
            if (key is null)
            {
                return Result<string>.Success(string.Empty);
            }

            var value = key.GetValue(AgentRegistry.EnrollmentTokenValueName);
            if (value is null)
            {
                return Result<string>.Success(string.Empty);
            }

            if (value is not string token)
            {
                _logger.LogError(
                    "Enrollment token value {KeyPath}\\{ValueName} is not a string",
                    AgentRegistry.KeyPath,
                    AgentRegistry.EnrollmentTokenValueName);
                return Result<string>.Failure("[Enrollment.TokenSource] Enrollment token value has an unexpected type");
            }

            return Result<string>.Success(token.Trim());
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Access denied reading the enrollment token from {KeyPath}", AgentRegistry.KeyPath);
            return Result<string>.Failure("[Enrollment.TokenSource] Access denied reading the enrollment token");
        }
        catch (UnauthorizedAccessException ex)
        {
            // Registry ACL denials surface as UnauthorizedAccessException on some runtimes
            _logger.LogError(ex, "Access denied reading the enrollment token from {KeyPath}", AgentRegistry.KeyPath);
            return Result<string>.Failure("[Enrollment.TokenSource] Access denied reading the enrollment token");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read the enrollment token from {KeyPath}", AgentRegistry.KeyPath);
            return Result<string>.Failure("[Enrollment.TokenSource] Failed to read the enrollment token");
        }
    }
}
