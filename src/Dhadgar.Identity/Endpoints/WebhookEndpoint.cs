using System.Text.Json;
using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;

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
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        // TODO: Validate Better Auth webhook signature before processing.
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
        if (!TryGetEventName(document.RootElement, out var eventName))
        {
            return Results.BadRequest(new { error = "missing_event" });
        }
        var logger = loggerFactory.CreateLogger("WebhookEndpoint");
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
