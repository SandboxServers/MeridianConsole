using System.ComponentModel.DataAnnotations;

namespace Dhadgar.ServiceDefaults.Messaging;

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

    /// <summary>
    /// AMQP port of the RabbitMQ broker. Defaults to 5672.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5672;
}
