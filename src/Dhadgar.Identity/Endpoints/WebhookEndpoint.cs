using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Endpoints;

public static class WebhookEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/webhooks/better-auth", Handle)
            .RequireRateLimiting("auth");
    }

    private static async Task<IResult> Handle(
        HttpContext context,
        IdentityDbContext dbContext,
        IIdentityEventPublisher eventPublisher,
        IWebhookSecretProvider secretProvider,
        IOptions<WebhookOptions> webhookOptions,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WebhookEndpoint");
        var options = webhookOptions.Value;

        // Read the raw body for signature validation
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        context.Request.Body.Position = 0;

        // Get the webhook secret from Key Vault
        string? secret;
        try
        {
            secret = await secretProvider.GetBetterAuthSecretAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve webhook secret from Key Vault");
            return Results.Problem(
                title: "Configuration Error",
                detail: "Unable to validate webhook signature",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Validate webhook signature
        if (!ValidateSignature(context, rawBody, secret, options, environment, logger))
        {
            return Results.Unauthorized();
        }

        using var document = JsonDocument.Parse(rawBody);
        if (!TryGetEventName(document.RootElement, out var eventName))
        {
            return Results.BadRequest(new { error = "missing_event" });
        }

        var data = document.RootElement.TryGetProperty("data", out var dataElement)
            ? dataElement
            : default;

        var externalAuthId = GetString(data, "externalAuthId", "id", "userId", "user_id", "sub");
        if (string.IsNullOrWhiteSpace(externalAuthId))
        {
            logger.LogWarning("Better Auth webhook {Event} missing user identifier.", eventName);
            return Results.Ok();
        }

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.ExternalAuthId == externalAuthId, ct);

        if (user is null)
        {
            logger.LogInformation("Better Auth webhook {Event} ignored for unknown user {ExternalAuthId}.", eventName, externalAuthId);
            return Results.Ok();
        }

        var now = timeProvider.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        switch (eventName)
        {
            case "user.deleted":
            {
                if (user.DeletedAt is null)
                {
                    user.DeletedAt = nowUtc;
                }

                user.UpdatedAt = nowUtc;

                var memberships = await dbContext.UserOrganizations
                    .Where(uo => uo.UserId == user.Id && uo.LeftAt == null)
                    .ToListAsync(ct);

                foreach (var membership in memberships)
                {
                    membership.LeftAt = nowUtc;
                    membership.IsActive = false;
                }

                await dbContext.SaveChangesAsync(ct);

                try
                {
                    await eventPublisher.PublishUserDeactivatedAsync(new UserDeactivated(
                        user.Id,
                        user.ExternalAuthId,
                        "user.deleted",
                        now), ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to publish UserDeactivated event for user {UserId}.", user.Id);
                }

                return Results.Ok();
            }
            case "user.updated":
            {
                var updated = false;
                var email = GetString(data, "email");
                if (!string.IsNullOrWhiteSpace(email) && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = email;
                    updated = true;
                }

                if (TryGetBool(data, out var emailVerified, "emailVerified", "email_verified"))
                {
                    if (emailVerified != user.EmailVerified)
                    {
                        user.EmailVerified = emailVerified;
                        updated = true;
                    }
                }

                if (updated)
                {
                    user.UpdatedAt = nowUtc;
                    await dbContext.SaveChangesAsync(ct);
                }

                return Results.Ok();
            }
            case "passkey.registered":
            {
                user.HasPasskeysRegistered = true;
                user.LastPasskeyAuthAt = nowUtc;
                user.UpdatedAt = nowUtc;

                await dbContext.SaveChangesAsync(ct);
                return Results.Ok();
            }
            default:
                logger.LogInformation("Better Auth webhook {Event} received with no handler.", eventName);
                return Results.Ok();
        }
    }

    /// <summary>
    /// Validates the webhook signature using HMAC-SHA256.
    /// Format: "t=timestamp,v1=signature" where signature = HMAC-SHA256(timestamp.body, secret)
    /// </summary>
    private static bool ValidateSignature(
        HttpContext context,
        string rawBody,
        string? secret,
        WebhookOptions options,
        IHostEnvironment environment,
        ILogger logger)
    {
        // In development/testing, allow skipping validation if no secret is available
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            {
                logger.LogWarning("Webhook signature validation skipped - no secret available (dev/test only)");
                return true;
            }

            logger.LogError("Webhook signature validation failed - no secret available in production");
            return false;
        }

        var signatureHeader = context.Request.Headers[options.SignatureHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            logger.LogWarning("Webhook signature validation failed - missing {Header} header", options.SignatureHeader);
            return false;
        }

        // Parse signature header: "t=timestamp,v1=signature"
        var parts = signatureHeader.Split(',');
        string? timestamp = null;
        string? signature = null;

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            switch (kv[0].Trim())
            {
                case "t":
                    timestamp = kv[1].Trim();
                    break;
                case "v1":
                    signature = kv[1].Trim();
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature))
        {
            logger.LogWarning("Webhook signature validation failed - invalid signature format");
            return false;
        }

        // Validate timestamp to prevent replay attacks
        if (long.TryParse(timestamp, out var timestampSeconds))
        {
            var webhookTime = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
            var now = DateTimeOffset.UtcNow;
            var age = Math.Abs((now - webhookTime).TotalSeconds);

            if (age > options.MaxTimestampAgeSeconds)
            {
                logger.LogWarning("Webhook signature validation failed - timestamp too old ({AgeSeconds}s)", age);
                return false;
            }
        }
        else
        {
            logger.LogWarning("Webhook signature validation failed - invalid timestamp format");
            return false;
        }

        // Compute expected signature: HMAC-SHA256(timestamp.body, secret)
        var signedPayload = $"{timestamp}.{rawBody}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);

        var expectedSignature = Convert.ToHexString(HMACSHA256.HashData(secretBytes, payloadBytes)).ToLowerInvariant();

        // Use constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            logger.LogWarning("Webhook signature validation failed - signature mismatch");
            return false;
        }

        return true;
    }

    private static bool TryGetEventName(JsonElement element, out string eventName)
    {
        eventName = GetString(element, "event", "type", "name") ?? string.Empty;
        eventName = eventName.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(eventName);
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryGetBool(JsonElement element, out bool value, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric))
            {
                value = numeric != 0;
                return true;
            }
        }

        value = false;
        return false;
    }
}
