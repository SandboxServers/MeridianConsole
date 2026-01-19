using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Dhadgar.Notifications.Services;

/// <summary>
/// Email provider using Microsoft Graph API to send emails via Office 365.
/// Requires an Azure AD app registration with Mail.Send permission.
/// </summary>
public sealed class Office365EmailProvider : IEmailProvider, IDisposable
{
    private readonly GraphServiceClient _graphClient;
    private readonly string _senderEmail;
    private readonly ILogger<Office365EmailProvider> _logger;

    public Office365EmailProvider(
        IConfiguration configuration,
        ILogger<Office365EmailProvider> logger)
    {
        _logger = logger;

        var tenantId = configuration["Office365:TenantId"]
            ?? throw new InvalidOperationException("Office365:TenantId not configured");
        var clientId = configuration["Office365:ClientId"]
            ?? throw new InvalidOperationException("Office365:ClientId not configured");
        var clientSecret = configuration["Office365:ClientSecret"]
            ?? throw new InvalidOperationException("Office365:ClientSecret not configured");
        _senderEmail = configuration["Office365:SenderEmail"]
            ?? throw new InvalidOperationException("Office365:SenderEmail not configured");

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        _logger.LogInformation("Office365EmailProvider initialized for sender: {Sender}", _senderEmail);
    }

    public async Task<EmailSendResult> SendEmailAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default)
    {
        return await SendEmailAsync(new[] { recipientEmail }, subject, htmlBody, textBody, ct);
    }

    public async Task<EmailSendResult> SendEmailAsync(
        IEnumerable<string> recipientEmails,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default)
    {
        var recipients = recipientEmails.ToList();
        if (recipients.Count == 0)
        {
            return new EmailSendResult(false, "No recipients specified");
        }

        try
        {
            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlBody
                },
                ToRecipients = recipients.Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = email }
                }).ToList()
            };

            var requestBody = new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            };

            await _graphClient.Users[_senderEmail].SendMail.PostAsync(requestBody, cancellationToken: ct);

            _logger.LogInformation(
                "Email sent successfully via Office 365. Subject: {Subject}, Recipients: {Count}",
                subject, recipients.Count);

            return new EmailSendResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via Office 365. Subject: {Subject}", subject);
            return new EmailSendResult(false, "Failed to send email. Check logs for details.");
        }
    }

    public void Dispose()
    {
        // GraphServiceClient in v5.x manages its own HttpClient lifecycle
        // No explicit disposal needed
    }
}
