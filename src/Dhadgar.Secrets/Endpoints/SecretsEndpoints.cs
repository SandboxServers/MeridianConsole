using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Endpoints;

public static class SecretsEndpoints
{
    public static void MapSecretsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/secrets")
            .WithTags("Secrets");

        // Get a single secret by name
        group.MapGet("/{secretName}", GetSecret)
            .WithName("GetSecret")
            .WithDescription("Retrieves a single secret by name. Only allowed secrets can be retrieved.")
            .Produces<SecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        // Get multiple secrets by names (batch)
        group.MapPost("/batch", GetSecretsBatch)
            .WithName("GetSecretsBatch")
            .WithDescription("Retrieves multiple secrets by name. Only allowed secrets will be returned.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK);

        // Get all OAuth secrets
        group.MapGet("/oauth", GetOAuthSecrets)
            .WithName("GetOAuthSecrets")
            .WithDescription("Retrieves all configured OAuth provider secrets.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK);

        // Get BetterAuth secrets
        group.MapGet("/betterauth", GetBetterAuthSecrets)
            .WithName("GetBetterAuthSecrets")
            .WithDescription("Retrieves secrets required by the BetterAuth service.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK);

        // Get infrastructure secrets
        group.MapGet("/infrastructure", GetInfrastructureSecrets)
            .WithName("GetInfrastructureSecrets")
            .WithDescription("Retrieves infrastructure secrets (database, messaging).")
            .Produces<SecretsResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetSecret(
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

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (!HasSecretPermission(context.User, secretName, options.Value))
        {
            return Results.Forbid();
        }

        var value = await provider.GetSecretAsync(secretName, ct);

        if (value is null)
        {
            return Results.NotFound(new { error = $"Secret '{secretName}' not found or not configured." });
        }

        return Results.Ok(new SecretResponse(secretName, value));
    }

    private static async Task<IResult> GetSecretsBatch(
        [FromBody] BatchSecretsRequest request,
        [FromServices] ISecretProvider provider,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (request.SecretNames is null || request.SecretNames.Count == 0)
        {
            return Results.BadRequest(new { error = "SecretNames is required." });
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var authorized = request.SecretNames
            .Where(name => provider.IsAllowed(name))
            .Where(name => HasSecretPermission(context.User, name, options.Value))
            .ToArray();

        if (authorized.Length == 0)
        {
            return Results.Forbid();
        }

        var secrets = await provider.GetSecretsAsync(authorized, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetOAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (!HasPermission(context.User, options.Value.Permissions.OAuthRead))
        {
            return Results.Forbid();
        }

        var oauthSecretNames = options.Value.AllowedSecrets.OAuth;
        var secrets = await provider.GetSecretsAsync(oauthSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetBetterAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (!HasPermission(context.User, options.Value.Permissions.BetterAuthRead))
        {
            return Results.Forbid();
        }

        var betterAuthSecretNames = options.Value.AllowedSecrets.BetterAuth;
        var secrets = await provider.GetSecretsAsync(betterAuthSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetInfrastructureSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (!HasPermission(context.User, options.Value.Permissions.InfrastructureRead))
        {
            return Results.Forbid();
        }

        var infraSecretNames = options.Value.AllowedSecrets.Infrastructure;
        var secrets = await provider.GetSecretsAsync(infraSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static bool HasSecretPermission(
        System.Security.Claims.ClaimsPrincipal user,
        string secretName,
        Options.SecretsOptions options)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var directPermission = $"secrets:read:{secretName}";
        if (HasPermission(user, directPermission))
        {
            return true;
        }

        if (ContainsSecret(options.AllowedSecrets.OAuth, secretName))
        {
            return HasPermission(user, options.Permissions.OAuthRead);
        }

        if (ContainsSecret(options.AllowedSecrets.BetterAuth, secretName))
        {
            return HasPermission(user, options.Permissions.BetterAuthRead);
        }

        if (ContainsSecret(options.AllowedSecrets.Infrastructure, secretName))
        {
            return HasPermission(user, options.Permissions.InfrastructureRead);
        }

        return false;
    }

    private static bool ContainsSecret(IEnumerable<string> secrets, string secretName)
    {
        return secrets.Any(name => string.Equals(name, secretName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, string permission)
    {
        return user.Claims.Any(claim =>
            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
    }
}

public record SecretResponse(string Name, string Value);
public record SecretsResponse(Dictionary<string, string> Secrets);
public record BatchSecretsRequest(IReadOnlyList<string> SecretNames);
