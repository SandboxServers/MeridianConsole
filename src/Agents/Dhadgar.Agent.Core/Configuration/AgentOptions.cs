using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Root configuration options for the agent.
/// </summary>
public sealed class AgentOptions : IValidatableObject
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Unique identifier assigned during enrollment. Null until enrolled.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>
    /// Organization ID assigned during enrollment. Null until enrolled.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Human-readable name for this node.
    /// </summary>
    [MaxLength(256)]
    public string? NodeName { get; set; }

    /// <summary>
    /// Directory that holds the agent identity state file (<c>agent-state.json</c>).
    /// Defaults to <c>%ProgramData%\Meridian Console\Agent</c> when empty.
    /// </summary>
    public string StateDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Control plane connection settings.
    /// </summary>
    [Required]
    public ControlPlaneOptions ControlPlane { get; set; } = new();

    /// <summary>
    /// Security settings.
    /// </summary>
    [Required]
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Process management settings.
    /// </summary>
    [Required]
    public ProcessOptions Process { get; set; } = new();

    /// <summary>
    /// File handling settings.
    /// </summary>
    [Required]
    public FileOptions Files { get; set; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // NodeId and OrganizationId identify a tenant together, so they are set together or not at all.
        // The client certificate is held by the platform certificate store, not by these options.
        if (NodeId.HasValue != OrganizationId.HasValue)
        {
            yield return new ValidationResult(
                $"{nameof(NodeId)} and {nameof(OrganizationId)} must both be set or both be null",
                [nameof(NodeId), nameof(OrganizationId)]);
        }

        if (!string.IsNullOrWhiteSpace(StateDirectory) && CheckStateDirectory(StateDirectory) is { } stateDirectoryError)
        {
            yield return new ValidationResult(stateDirectoryError, [nameof(StateDirectory)]);
        }
    }

    /// <summary>
    /// Null when <paramref name="directory"/> is an acceptable state directory; otherwise the reason it is not.
    /// Used by Validate and by FileAgentStateStore so both apply the same rule (D-S233).
    /// </summary>
    internal static string? CheckStateDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
        {
            return "StateDirectory must be a fully qualified local path";
        }

        // UNC (\\server\share, //server/share) and device (\\?\, \\.\) paths: the agent's machine account would authenticate to them.
        if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal))
        {
            return "StateDirectory must not be a UNC or device path";
        }

        if (directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
        {
            return "StateDirectory must not contain '..' segments";
        }

        return null;
    }
}
