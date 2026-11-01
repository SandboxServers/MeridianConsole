using System.ComponentModel.DataAnnotations;
using Dhadgar.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services.CommandSigning;

/// <summary>
/// Configuration for the command signing key (configuration section <c>CommandSigning</c>).
/// </summary>
public sealed class CommandSigningOptions : IValidateOptions<CommandSigningOptions>
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CommandSigning";

    /// <summary>
    /// Absolute path of the PEM-encoded P-256 private key. When unset, the key is stored as
    /// <c>command-signing-key.pem</c> in the CA storage directory. Must not contain <c>..</c> segments.
    /// </summary>
    public string? KeyPath { get; set; }

    /// <summary>
    /// Lifetime of a signed command, in seconds. Must not exceed the agent limit
    /// (<see cref="AgentMessageLimits.MaxCommandLifetimeSeconds"/>); a longer lifetime makes conforming agents reject every command.
    /// Default: 60.
    /// </summary>
    [Range(10, AgentMessageLimits.MaxCommandLifetimeSeconds, ErrorMessage = "CommandTtlSeconds must be between 10 and 60")]
    public int CommandTtlSeconds { get; set; } = 60;

    /// <summary>
    /// Returns true when <paramref name="keyPath"/> is an absolute path without <c>..</c> segments.
    /// </summary>
    public static bool IsAcceptableKeyPath(string keyPath) =>
        Path.IsPathFullyQualified(keyPath) && !keyPath.Split('/', '\\').Contains("..");

    /// <summary>
    /// Validates the key path when one is configured.
    /// </summary>
    public ValidateOptionsResult Validate(string? name, CommandSigningOptions options)
    {
        if (options.KeyPath is not null && !IsAcceptableKeyPath(options.KeyPath))
        {
            return ValidateOptionsResult.Fail(
                ["CommandSigning:KeyPath must be an absolute path without '..' segments"]);
        }

        return ValidateOptionsResult.Success;
    }
}