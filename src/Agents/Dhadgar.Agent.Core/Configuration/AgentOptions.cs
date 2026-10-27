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

        if (!string.IsNullOrWhiteSpace(StateDirectory) && !Path.IsPathRooted(StateDirectory))
        {
            yield return new ValidationResult(
                $"{nameof(StateDirectory)} must be an absolute path when set",
                [nameof(StateDirectory)]);
        }
    }
}
