namespace Dhadgar.Notifications.Services;

/// <summary>
/// Result of an email send operation.
/// </summary>
public record EmailSendResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Abstraction for email sending providers (Office 365, SendGrid, etc.).
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Sends an email to the specified recipient.
    /// </summary>
    Task<EmailSendResult> SendEmailAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends an email to multiple recipients.
    /// </summary>
    Task<EmailSendResult> SendEmailAsync(
        IEnumerable<string> recipientEmails,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default);
}
