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
    /// Human-readable name for this node.
    /// </summary>
    [MaxLength(256)]
    public string? NodeName { get; set; }

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
        // If enrolled (NodeId set), security options must have valid certificate configuration
        if (NodeId.HasValue)
        {
            // Either CertificateThumbprint or (CertificatePath + PrivateKeyPath) must be configured
            var hasThumbprint = !string.IsNullOrEmpty(Security.CertificateThumbprint);
            var hasCertPath = !string.IsNullOrEmpty(Security.CertificatePath);
            var hasKeyPath = !string.IsNullOrEmpty(Security.PrivateKeyPath);

            if (!hasThumbprint && !hasCertPath && !hasKeyPath)
            {
                yield return new ValidationResult(
                    "Enrolled agents must have certificate configuration. " +
                    $"Specify either {nameof(SecurityOptions.CertificateThumbprint)} or " +
                    $"both {nameof(SecurityOptions.CertificatePath)} and {nameof(SecurityOptions.PrivateKeyPath)}.",
                    [nameof(Security)]);
            }
        }
    }
}
