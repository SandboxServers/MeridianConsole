using System.Diagnostics;

namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Configures Windows Service installation and recovery options.
/// </summary>
/// <remarks>
/// SECURITY: Service name is hardcoded to prevent command injection.
/// If parameterization is needed in the future, add strict validation:
/// - Allow only alphanumeric characters, hyphens, and underscores
/// - Reject shell metacharacters (quotes, semicolons, pipes, backticks)
/// - Maximum length of 256 characters
/// </remarks>
public static class ServiceInstaller
{
    /// <summary>
    /// Configures the Windows Service recovery options.
    /// Must be run after service installation with administrator privileges.
    /// </summary>
    /// <remarks>
    /// Recovery configuration:
    /// - First failure: restart after 5 seconds
    /// - Second failure: restart after 10 seconds
    /// - Subsequent failures: restart after 30 seconds
    /// - Reset failure count after 24 hours (86400 seconds)
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when sc.exe fails to configure recovery.</exception>
    public static void ConfigureRecovery()
    {
        // SECURITY: Service name is intentionally hardcoded to prevent command injection
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"failure {Program.ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Failed to start sc.exe");
        }

        // Read output before WaitForExit to avoid buffer deadlock
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }

            throw new InvalidOperationException("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to configure service recovery. Exit code: {process.ExitCode}. Error: {error}");
        }
    }

    /// <summary>
    /// Sets the service description.
    /// </summary>
    /// <param name="description">The service description text.</param>
    /// <exception cref="InvalidOperationException">Thrown when sc.exe fails.</exception>
    public static void SetDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        // SECURITY: Escape quotes in description to prevent command injection
        var escapedDescription = description.Replace("\"", "\\\"", StringComparison.Ordinal);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"description {Program.ServiceName} \"{escapedDescription}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Failed to start sc.exe");
        }

        // Read output before WaitForExit to avoid buffer deadlock
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }

            throw new InvalidOperationException("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to set service description. Exit code: {process.ExitCode}. Error: {error}");
        }
    }

    /// <summary>
    /// Configures the service to start automatically with delayed start.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when sc.exe fails.</exception>
    public static void ConfigureDelayedAutoStart()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"config {Program.ServiceName} start= delayed-auto",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Failed to start sc.exe");
        }

        // Read output before WaitForExit to avoid buffer deadlock
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }

            throw new InvalidOperationException("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to configure delayed auto-start. Exit code: {process.ExitCode}. Error: {error}");
        }
    }
}
