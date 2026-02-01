using System.Net;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;

namespace Dhadgar.Notifications.Services;

/// <summary>
/// Email provider using Microsoft Graph API to send emails via Office 365.
/// Requires an Azure AD app registration with Mail.Send permission.
/// Implements retry with exponential backoff and throttling (429) handling.
/// </summary>
public sealed class Office365EmailProvider : IEmailProvider, IDisposable
{
    private const int MaxRetryAttempts = 3;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30000;

    private GraphServiceClient? _graphClient;
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

        ObjectDisposedException.ThrowIf(_graphClient is null, this);

        // Use htmlBody if available, otherwise fall back to textBody
        var useHtml = !string.IsNullOrWhiteSpace(htmlBody);
        var bodyContent = useHtml ? htmlBody : textBody ?? string.Empty;
        var bodyType = useHtml ? BodyType.Html : BodyType.Text;

        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = bodyType,
                Content = bodyContent
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

        // Retry with exponential backoff for transient failures
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                await _graphClient.Users[_senderEmail].SendMail.PostAsync(requestBody, cancellationToken: ct);

                _logger.LogInformation(
                    "Email sent successfully via Office 365. Subject: {Subject}, Recipients: {Count}, Attempt: {Attempt}",
                    subject, recipients.Count, attempt);

                return new EmailSendResult(true);
            }
            catch (ODataError ex) when (IsThrottled(ex))
            {
                var retryAfter = GetRetryAfter(ex);
                _logger.LogWarning(
                    "Graph API throttled (429). Subject: {Subject}, Attempt: {Attempt}/{MaxAttempts}, RetryAfter: {RetryAfter}s",
                    subject, attempt, MaxRetryAttempts, retryAfter.TotalSeconds);

                if (attempt >= MaxRetryAttempts)
                {
                    return new EmailSendResult(false, $"Throttled after {MaxRetryAttempts} attempts. Try again later.");
                }

                await Task.Delay(retryAfter, ct);
            }
            catch (ODataError ex) when (IsTransient(ex))
            {
                var delay = GetExponentialBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "Transient Graph API error. Subject: {Subject}, Attempt: {Attempt}/{MaxAttempts}, Status: {StatusCode}",
                    subject, attempt, MaxRetryAttempts, ex.ResponseStatusCode);

                if (attempt >= MaxRetryAttempts)
                {
                    return new EmailSendResult(false, $"Failed after {MaxRetryAttempts} attempts: {ex.Message}");
                }

                await Task.Delay(delay, ct);
            }
            catch (ODataError ex)
            {
                // Non-transient error - don't retry
                _logger.LogError(
                    ex,
                    "Graph API error (non-retryable). Subject: {Subject}, Status: {StatusCode}, Code: {ErrorCode}",
                    subject, ex.ResponseStatusCode, ex.Error?.Code);
                return new EmailSendResult(false, $"Graph API error: {ex.Error?.Message ?? ex.Message}");
            }
            catch (OperationCanceledException)
            {
                throw; // Rethrow cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error sending email. Subject: {Subject}, ExceptionType: {ExceptionType}",
                    subject, ex.GetType().Name);
                return new EmailSendResult(false, $"Unexpected error: {ex.Message}");
            }
        }

        // All paths return within the loop, but compiler needs this for type safety
        throw new InvalidOperationException("Retry loop exited unexpectedly");
    }

    public void Dispose()
    {
        if (_graphClient is not null)
        {
            _graphClient.Dispose();
            _graphClient = null;
        }
    }

    /// <summary>
    /// Checks if the error is a throttling (429) response.
    /// </summary>
    private static bool IsThrottled(ODataError ex) =>
        ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Checks if the error is a transient failure that should be retried.
    /// </summary>
    private static bool IsTransient(ODataError ex) =>
        ex.ResponseStatusCode is
            (int)HttpStatusCode.InternalServerError or
            (int)HttpStatusCode.BadGateway or
            (int)HttpStatusCode.ServiceUnavailable or
            (int)HttpStatusCode.GatewayTimeout or
            (int)HttpStatusCode.RequestTimeout;

    /// <summary>
    /// Extracts Retry-After duration from throttling response.
    /// Falls back to default delay if header not present.
    /// </summary>
    private static TimeSpan GetRetryAfter(ODataError ex)
    {
        // ODataError doesn't expose headers directly, so use fixed default
        // In production, consider using a custom DelegatingHandler to capture Retry-After header
        _ = ex; // Unused for now, but parameter kept for future header extraction
        return TimeSpan.FromSeconds(30); // Default for Graph API throttling
    }

    /// <summary>
    /// Calculates exponential backoff delay with jitter.
    /// </summary>
    private static TimeSpan GetExponentialBackoff(int attempt)
    {
        // 1s, 2s, 4s, etc. with some jitter
        var baseDelay = BaseDelayMs * (1 << (attempt - 1));
        // CA5394 suppressed: Jitter for retry backoff is not a security feature;
        // it prevents thundering herd, not cryptographic attacks
#pragma warning disable CA5394
        var jitter = Random.Shared.Next(0, 500); // Add 0-500ms jitter
#pragma warning restore CA5394
        var totalDelay = Math.Min(baseDelay + jitter, MaxDelayMs);
        return TimeSpan.FromMilliseconds(totalDelay);
    }
}
