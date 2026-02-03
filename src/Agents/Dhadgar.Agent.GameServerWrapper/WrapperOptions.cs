using System.Text.RegularExpressions;

using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.GameServerWrapper;

/// <summary>
/// Command-line options for the GameServerWrapper.
/// </summary>
/// <remarks>
/// SECURITY: All options are validated before use.
/// </remarks>
public sealed partial class WrapperOptions
{
    /// <summary>
    /// Pattern for valid server IDs.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ValidServerIdPattern();

    /// <summary>
    /// Pattern for valid pipe names: MeridianAgent_{agentId}\{serverId}
    /// Agent ID and server ID must be alphanumeric with hyphens/underscores.
    /// </summary>
    [GeneratedRegex(@"^MeridianAgent_[a-zA-Z0-9\-_]+\\[a-zA-Z0-9\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ValidPipeNamePattern();

    /// <summary>
    /// The server identifier from the control plane.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// The named pipe to connect to for IPC with the agent.
    /// </summary>
    public required string PipeName { get; init; }

    /// <summary>
    /// Path to the server configuration file.
    /// </summary>
    public required string ConfigPath { get; init; }

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <returns>List of validation errors, or empty if valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // Validate ServerId
        if (string.IsNullOrWhiteSpace(ServerId))
        {
            errors.Add("--server-id is required");
        }
        else if (!ValidServerIdPattern().IsMatch(ServerId))
        {
            errors.Add("--server-id contains invalid characters");
        }

        // Validate PipeName with regex for strict format enforcement
        if (string.IsNullOrWhiteSpace(PipeName))
        {
            errors.Add("--pipe is required");
        }
        else if (!ValidPipeNamePattern().IsMatch(PipeName))
        {
            errors.Add("--pipe must match format 'MeridianAgent_{agentId}\\{serverId}' with alphanumeric IDs");
        }

        // Validate ConfigPath
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            errors.Add("--config is required");
        }
        else if (!Path.IsPathRooted(ConfigPath))
        {
            errors.Add("--config must be an absolute path");
        }
        else
        {
            // Path traversal validation using normalization
            var (normalizedPath, isValid) = TryGetFullPath(ConfigPath);
            if (!isValid)
            {
                errors.Add("--config is not a valid path");
            }
            else if (!NormalizedPathsMatch(ConfigPath, normalizedPath!))
            {
                errors.Add("--config contains path traversal sequences");
            }
            else if (!File.Exists(ConfigPath))
            {
                errors.Add($"Configuration file not found: {ConfigPath}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Parses command-line arguments into options.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Result containing parsed options or error.</returns>
    public static Result<WrapperOptions> Parse(string[] args)
    {
        string? serverId = null;
        string? pipeName = null;
        string? configPath = null;
        var errors = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--server-id=", StringComparison.OrdinalIgnoreCase))
            {
                serverId = arg["--server-id=".Length..];
            }
            else if (arg.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase))
            {
                pipeName = arg["--pipe=".Length..];
            }
            else if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                // Handle quoted paths
                var value = arg["--config=".Length..];
                if (value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value[1..^1];
                }
                configPath = value;
            }
            else if (arg.Equals("--server-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverId = args[++i];
            }
            else if (arg.Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipeName = args[++i];
            }
            else if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
            else if (!arg.StartsWith('-'))
            {
                // Ignore positional arguments
            }
            else
            {
                errors.Add($"Unknown argument: {arg}");
            }
        }

        if (serverId is null || pipeName is null || configPath is null)
        {
            if (serverId is null) errors.Add("Missing required argument: --server-id");
            if (pipeName is null) errors.Add("Missing required argument: --pipe");
            if (configPath is null) errors.Add("Missing required argument: --config");
            return Result<WrapperOptions>.Failure(string.Join("; ", errors));
        }

        var options = new WrapperOptions
        {
            ServerId = serverId,
            PipeName = pipeName,
            ConfigPath = configPath
        };

        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            return Result<WrapperOptions>.Failure(string.Join("; ", validationErrors));
        }

        return Result<WrapperOptions>.Success(options);
    }

    /// <summary>
    /// Attempts to get the full path, returning success/failure without throwing.
    /// </summary>
    private static (string? NormalizedPath, bool IsValid) TryGetFullPath(string path)
    {
        try
        {
            return (Path.GetFullPath(path), true);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Compares paths accounting for trailing separators and OS case sensitivity.
    /// </summary>
    private static bool NormalizedPathsMatch(string original, string normalized)
    {
        // Use OS-aware comparison: case-insensitive on Windows, case-sensitive on Unix
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var originalTrimmed = original.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTrimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return originalTrimmed.Equals(normalizedTrimmed, comparison);
    }
}
