using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Dhadgar.Secrets.Endpoints;

public static class SecretWriteEndpoints
{
    public static void MapSecretWriteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/secrets")
            .WithTags("Secrets")
            .RequireAuthorization();

        // Set/update a secret
        group.MapPut("/{secretName}", SetSecret)
            .WithName("SetSecret")
            .WithDescription("Set or update a secret value. Only allowed secrets can be updated.")
            .Produces<SetSecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        // Rotate a secret
        group.MapPost("/{secretName}/rotate", RotateSecret)
            .WithName("RotateSecret")
            .WithDescription("Rotate a secret (generate new cryptographically secure value).")
            .Produces<RotateSecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Delete a secret
        group.MapDelete("/{secretName}", DeleteSecret)
            .WithName("DeleteSecret")
            .WithDescription("Delete a secret (soft delete if vault has soft delete enabled).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SetSecret(
        string secretName,
        [FromBody] SetSecretRequest request,
        [FromServices] ISecretProvider provider,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Results.BadRequest(new { error = "Value is required." });
        }

        if (!provider.IsAllowed(secretName))
        {
            return Results.Forbid();
        }

        // Check write permission
        if (!HasWritePermission(context.User, secretName, options.Value))
        {
            return Results.Forbid();
        }

        try
        {
            var success = await provider.SetSecretAsync(secretName, request.Value, ct);
            if (!success)
            {
                return Results.Forbid();
            }

            return Results.Ok(new SetSecretResponse(secretName, true));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RotateSecret(
        string secretName,
        [FromServices] ISecretProvider provider,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (!provider.IsAllowed(secretName))
        {
            return Results.Forbid();
        }

        // Check rotate permission (stricter than write)
        if (!HasRotatePermission(context.User, secretName))
        {
            return Results.Forbid();
        }

        try
        {
            var (version, createdAt) = await provider.RotateSecretAsync(secretName, ct);

            return Results.Ok(new RotateSecretResponse(
                Name: secretName,
                Version: version,
                RotatedAt: createdAt,
                ExpiresAt: null // No expiration for rotated secrets
            ));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> DeleteSecret(
        string secretName,
        [FromServices] ISecretProvider provider,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (!provider.IsAllowed(secretName))
        {
            return Results.Forbid();
        }

        // Check delete permission (requires write permission)
        if (!HasWritePermission(context.User, secretName, options.Value))
        {
            return Results.Forbid();
        }

        var success = await provider.DeleteSecretAsync(secretName, ct);

        if (!success)
        {
            return Results.NotFound(new { error = $"Secret '{secretName}' not found." });
        }

        return Results.NoContent();
    }

    private static bool HasWritePermission(ClaimsPrincipal user, string secretName, Options.SecretsOptions options)
    {
        // Direct secret write permission
        var directPermission = $"secrets:write:{secretName}";
        if (HasPermission(user, directPermission))
        {
            return true;
        }

        // Category-based write permissions
        if (ContainsSecret(options.AllowedSecrets.OAuth, secretName))
        {
            return HasPermission(user, "secrets:write:oauth");
        }

        if (ContainsSecret(options.AllowedSecrets.BetterAuth, secretName))
        {
            return HasPermission(user, "secrets:write:betterauth");
        }

        if (ContainsSecret(options.AllowedSecrets.Infrastructure, secretName))
        {
            return HasPermission(user, "secrets:write:infrastructure");
        }

        return false;
    }

    private static bool HasRotatePermission(ClaimsPrincipal user, string secretName)
    {
        var rotatePermission = $"secrets:rotate:{secretName}";
        return HasPermission(user, rotatePermission);
    }

    private static bool ContainsSecret(IEnumerable<string> secrets, string secretName)
    {
        return secrets.Any(name => string.Equals(name, secretName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        return user.Claims.Any(claim =>
            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
    }
}

public record SetSecretRequest(string Value);
public record SetSecretResponse(string Name, bool Updated);
public record RotateSecretResponse(string Name, string Version, DateTime RotatedAt, DateTime? ExpiresAt);
