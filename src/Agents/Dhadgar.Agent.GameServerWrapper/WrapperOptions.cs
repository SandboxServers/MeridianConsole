using System.Text.RegularExpressions;

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

        // Validate PipeName
        if (string.IsNullOrWhiteSpace(PipeName))
        {
            errors.Add("--pipe is required");
        }
        else if (!PipeName.StartsWith("MeridianAgent_", StringComparison.Ordinal))
        {
            errors.Add("--pipe must start with 'MeridianAgent_'");
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
        else if (!File.Exists(ConfigPath))
        {
            errors.Add($"Configuration file not found: {ConfigPath}");
        }

        return errors;
    }

    /// <summary>
    /// Parses command-line arguments into options.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Parsed options or null if parsing failed.</returns>
    public static (WrapperOptions? Options, IReadOnlyList<string> Errors) Parse(string[] args)
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
            else if (arg == "--server-id" && i + 1 < args.Length)
            {
                serverId = args[++i];
            }
            else if (arg == "--pipe" && i + 1 < args.Length)
            {
                pipeName = args[++i];
            }
            else if (arg == "--config" && i + 1 < args.Length)
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
            return (null, errors);
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
            return (null, validationErrors);
        }

        return (options, errors);
    }
}
