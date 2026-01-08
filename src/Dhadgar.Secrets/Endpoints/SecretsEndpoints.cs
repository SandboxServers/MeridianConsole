using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Mvc;

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
        CancellationToken ct)
    {
        if (!provider.IsAllowed(secretName))
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
        CancellationToken ct)
    {
        if (request.SecretNames is null || request.SecretNames.Count == 0)
        {
            return Results.BadRequest(new { error = "SecretNames is required." });
        }

        var secrets = await provider.GetSecretsAsync(request.SecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetOAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        CancellationToken ct)
    {
        var oauthSecretNames = options.Value.AllowedSecrets.OAuth;
        var secrets = await provider.GetSecretsAsync(oauthSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetBetterAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        CancellationToken ct)
    {
        var betterAuthSecretNames = options.Value.AllowedSecrets.BetterAuth;
        var secrets = await provider.GetSecretsAsync(betterAuthSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetInfrastructureSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] Microsoft.Extensions.Options.IOptions<Options.SecretsOptions> options,
        CancellationToken ct)
    {
        var infraSecretNames = options.Value.AllowedSecrets.Infrastructure;
        var secrets = await provider.GetSecretsAsync(infraSecretNames, ct);

        return Results.Ok(new SecretsResponse(secrets));
    }
}

public record SecretResponse(string Name, string Value);
public record SecretsResponse(Dictionary<string, string> Secrets);
public record BatchSecretsRequest(List<string> SecretNames);
