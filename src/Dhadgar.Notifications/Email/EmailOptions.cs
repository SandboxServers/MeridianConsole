namespace Dhadgar.Notifications.Email;

/// <summary>
/// Configuration options for SMTP email sending.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Gets or sets the SMTP server hostname.</summary>
    public string SmtpHost { get; set; } = "localhost";

    /// <summary>Gets or sets the SMTP server port.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Gets or sets the SMTP username for authentication.</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>Gets or sets the SMTP password for authentication.</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>Gets or sets whether to use TLS/SSL.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Gets or sets the sender display name.</summary>
    public string SenderName { get; set; } = "Meridian Alerts";

    /// <summary>Gets or sets the sender email address.</summary>
    public string SenderEmail { get; set; } = "alerts@meridian.local";

    /// <summary>Gets or sets the alert recipient email address(es), comma-separated.</summary>
    public string AlertRecipients { get; set; } = "";

    /// <summary>Gets or sets whether email alerts are enabled.</summary>
    public bool Enabled { get; set; } = true;
}
