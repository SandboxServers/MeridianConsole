using System.ComponentModel.DataAnnotations;

namespace Dhadgar.ServiceDefaults;

/// <summary>
/// Configuration options for RabbitMQ connection.
/// Shared across all services that use MassTransit messaging.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string Host { get; set; } = "localhost";

    [Required]
    public string Username { get; set; } = "dhadgar";

    [Required]
    public string Password { get; set; } = "dhadgar";

    public string VirtualHost { get; set; } = "/";
}
