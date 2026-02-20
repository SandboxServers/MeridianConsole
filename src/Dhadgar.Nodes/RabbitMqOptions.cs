using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes;

/// <summary>
/// Configuration options for RabbitMQ connection.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string Host { get; set; } = "localhost";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string VirtualHost { get; set; } = "/";
}
