using System.Diagnostics;
using Dhadgar.Shared.Results;

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
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static Result ConfigureRecovery()
    {
        // SECURITY: Service name is intentionally hardcoded to prevent command injection
        using var process = Process.Start(new ProcessStartInfo
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
            return Result.Failure("Failed to start sc.exe");
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

            return Result.Failure("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            return Result.Failure(
                $"Failed to configure service recovery. Exit code: {process.ExitCode}. Error: {error}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Sets the service description.
    /// </summary>
    /// <param name="description">The service description text.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static Result SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure("Description is required and cannot be empty.");
        }

        // SECURITY: Escape quotes in description to prevent command injection
        var escapedDescription = description.Replace("\"", "\\\"", StringComparison.Ordinal);

        using var process = Process.Start(new ProcessStartInfo
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
            return Result.Failure("Failed to start sc.exe");
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

            return Result.Failure("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            return Result.Failure(
                $"Failed to set service description. Exit code: {process.ExitCode}. Error: {error}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Configures the service to start automatically with delayed start.
    /// </summary>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static Result ConfigureDelayedAutoStart()
    {
        using var process = Process.Start(new ProcessStartInfo
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
            return Result.Failure("Failed to start sc.exe");
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

            return Result.Failure("sc.exe command timed out.");
        }

        if (process.ExitCode != 0)
        {
            return Result.Failure(
                $"Failed to configure delayed auto-start. Exit code: {process.ExitCode}. Error: {error}");
        }

        return Result.Success();
    }
}
