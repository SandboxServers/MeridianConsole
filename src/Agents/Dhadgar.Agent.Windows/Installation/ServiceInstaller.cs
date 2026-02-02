using System.Diagnostics;
using System.Globalization;
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
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static async Task<Result> ConfigureRecoveryAsync(CancellationToken cancellationToken = default)
    {
        // SECURITY: Service name is intentionally hardcoded to prevent command injection
        return await RunScCommandAsync(
            $"failure {Program.ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000",
            "configure service recovery",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the service description.
    /// </summary>
    /// <param name="description">The service description text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static async Task<Result> SetDescriptionAsync(string description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure("Description is required and cannot be empty.");
        }

        // SECURITY: Escape quotes in description to prevent command injection
        var escapedDescription = description.Replace("\"", "\\\"", StringComparison.Ordinal);

        return await RunScCommandAsync(
            $"description {Program.ServiceName} \"{escapedDescription}\"",
            "set service description",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Configures the service to start automatically with delayed start.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure with an error message.</returns>
    public static async Task<Result> ConfigureDelayedAutoStartAsync(CancellationToken cancellationToken = default)
    {
        return await RunScCommandAsync(
            $"config {Program.ServiceName} start= delayed-auto",
            "configure delayed auto-start",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an sc.exe command with proper async output handling and timeout.
    /// </summary>
    /// <param name="arguments">The arguments to pass to sc.exe.</param>
    /// <param name="operationDescription">Description of the operation for error messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    private static async Task<Result> RunScCommandAsync(
        string arguments,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (process is null)
        {
            return Result.Failure("Failed to start sc.exe");
        }

        // Start async reads immediately to avoid buffer deadlock
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        // Create a combined cancellation token for the timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeout);

        try
        {
            // Wait for process exit with timeout
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (not user cancellation)
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }

            // Still await the output tasks to completion (they should complete quickly after kill)
            try
            {
                await Task.WhenAll(errorTask, outputTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Output reads may also be cancelled
            }

            return Result.Failure("sc.exe command timed out.");
        }

        // Process exited normally, get the output
        string error;
        try
        {
            error = await errorTask.ConfigureAwait(false);
            _ = await outputTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Operation was cancelled.");
        }

        if (process.ExitCode != 0)
        {
            return Result.Failure(string.Format(
                CultureInfo.InvariantCulture,
                "Failed to {0}. Exit code: {1}. Error: {2}",
                operationDescription,
                process.ExitCode,
                error));
        }

        return Result.Success();
    }
}
