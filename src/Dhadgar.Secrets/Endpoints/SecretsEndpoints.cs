using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Audit;
using Dhadgar.Secrets.Services;
using Dhadgar.Secrets.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Endpoints;

public static class SecretsEndpoints
{
    public static void MapSecretsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/secrets")
            .WithTags("Secrets")
            .RequireAuthorization();

        // Get a single secret by name
        group.MapGet("/{secretName}", GetSecret)
            .WithName("GetSecret")
            .WithDescription("Retrieves a single secret by name. Only allowed secrets can be retrieved.")
            .Produces<SecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        // Get multiple secrets by names (batch)
        group.MapPost("/batch", GetSecretsBatch)
            .WithName("GetSecretsBatch")
            .WithDescription("Retrieves multiple secrets by name. Only allowed secrets will be returned.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        // Get all OAuth secrets
        group.MapGet("/oauth", GetOAuthSecrets)
            .WithName("GetOAuthSecrets")
            .WithDescription("Retrieves all configured OAuth provider secrets.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        // Get BetterAuth secrets
        group.MapGet("/betterauth", GetBetterAuthSecrets)
            .WithName("GetBetterAuthSecrets")
            .WithDescription("Retrieves secrets required by the BetterAuth service.")
            .Produces<SecretsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        // Get infrastructure secrets
        group.MapGet("/infrastructure", GetInfrastructureSecrets)
            .WithName("GetInfrastructureSecrets")
            .WithDescription("Retrieves infrastructure secrets (database, messaging).")
            .Produces<SecretsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetSecret(
        string secretName,
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretsAuthorizationService authService,
        [FromServices] ISecretsAuditLogger auditLogger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate input
        var validation = SecretNameValidator.Validate(secretName);
        if (!validation.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.Secrets.InvalidSecretName,
                validation.ErrorMessage);
        }

        // Check if secret is in allowed list
        if (!provider.IsAllowed(secretName))
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "read",
                UserId: context.User.FindFirst("sub")?.Value,
                Reason: "Secret not in allowed list",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        // Authorize
        var authResult = authService.Authorize(context.User, secretName, SecretAction.Read);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "read",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        // Fetch secret
        var value = await provider.GetSecretAsync(secretName, ct);

        if (value is null)
        {
            auditLogger.LogAccess(new SecretAuditEvent(
                SecretName: secretName,
                Action: "read",
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                Success: false,
                CorrelationId: context.TraceIdentifier,
                IsBreakGlass: authResult.IsBreakGlass,
                IsServiceAccount: authResult.IsServiceAccount));

            return ProblemDetailsHelper.NotFound(
                ErrorCodes.Secrets.SecretNotFound,
                $"Secret '{secretName}' not found or not configured.");
        }

        // Log successful access
        auditLogger.LogAccess(new SecretAuditEvent(
            SecretName: secretName,
            Action: "read",
            UserId: authResult.UserId,
            PrincipalType: authResult.PrincipalType,
            Success: true,
            CorrelationId: context.TraceIdentifier,
            IsBreakGlass: authResult.IsBreakGlass,
            IsServiceAccount: authResult.IsServiceAccount));

        return Results.Ok(new SecretResponse(secretName, value));
    }

    private static async Task<IResult> GetSecretsBatch(
        [FromBody] BatchSecretsRequest request,
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretsAuthorizationService authService,
        [FromServices] ISecretsAuditLogger auditLogger,
        HttpContext context,
        CancellationToken ct)
    {
        if (request.SecretNames is null || request.SecretNames.Count == 0)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.Generic.ValidationFailed,
                "SecretNames is required.");
        }

        // Validate all secret names
        foreach (var name in request.SecretNames)
        {
            var validation = SecretNameValidator.Validate(name);
            if (!validation.IsValid)
            {
                return ProblemDetailsHelper.BadRequest(
                    ErrorCodes.Secrets.InvalidSecretName,
                    $"Invalid secret name '{name}': {validation.ErrorMessage}");
            }
        }

        var userId = context.User.FindFirst("sub")?.Value;
        var authorizedSecrets = new List<string>();
        var deniedCount = 0;

        foreach (var secretName in request.SecretNames)
        {
            if (!provider.IsAllowed(secretName))
            {
                deniedCount++;
                continue;
            }

            var authResult = authService.Authorize(context.User, secretName, SecretAction.Read);
            if (authResult.IsAuthorized)
            {
                authorizedSecrets.Add(secretName);
            }
            else
            {
                deniedCount++;
            }
        }

        if (authorizedSecrets.Count == 0)
        {
            auditLogger.LogBatchAccess(new SecretBatchAccessEvent(
                RequestedSecrets: request.SecretNames.ToList(),
                AccessedCount: 0,
                DeniedCount: request.SecretNames.Count,
                UserId: userId,
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        var secrets = await provider.GetSecretsAsync(authorizedSecrets, ct);

        auditLogger.LogBatchAccess(new SecretBatchAccessEvent(
            RequestedSecrets: request.SecretNames.ToList(),
            AccessedCount: secrets.Count,
            DeniedCount: deniedCount,
            UserId: userId,
            CorrelationId: context.TraceIdentifier));

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetOAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretsAuthorizationService authService,
        [FromServices] ISecretsAuditLogger auditLogger,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        var authResult = authService.AuthorizeCategory(context.User, "oauth", SecretAction.Read);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: "[oauth-category]",
                Action: "read",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        var oauthSecretNames = options.Value.AllowedSecrets.OAuth;
        var secrets = await provider.GetSecretsAsync(oauthSecretNames, ct);

        auditLogger.LogAccess(new SecretAuditEvent(
            SecretName: "[oauth-category]",
            Action: "read",
            UserId: authResult.UserId,
            PrincipalType: authResult.PrincipalType,
            Success: true,
            CorrelationId: context.TraceIdentifier,
            IsBreakGlass: authResult.IsBreakGlass,
            IsServiceAccount: authResult.IsServiceAccount));

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetBetterAuthSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretsAuthorizationService authService,
        [FromServices] ISecretsAuditLogger auditLogger,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        var authResult = authService.AuthorizeCategory(context.User, "betterauth", SecretAction.Read);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: "[betterauth-category]",
                Action: "read",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        var betterAuthSecretNames = options.Value.AllowedSecrets.BetterAuth;
        var secrets = await provider.GetSecretsAsync(betterAuthSecretNames, ct);

        auditLogger.LogAccess(new SecretAuditEvent(
            SecretName: "[betterauth-category]",
            Action: "read",
            UserId: authResult.UserId,
            PrincipalType: authResult.PrincipalType,
            Success: true,
            CorrelationId: context.TraceIdentifier,
            IsBreakGlass: authResult.IsBreakGlass,
            IsServiceAccount: authResult.IsServiceAccount));

        return Results.Ok(new SecretsResponse(secrets));
    }

    private static async Task<IResult> GetInfrastructureSecrets(
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretsAuthorizationService authService,
        [FromServices] ISecretsAuditLogger auditLogger,
        [FromServices] IOptions<Options.SecretsOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        var authResult = authService.AuthorizeCategory(context.User, "infrastructure", SecretAction.Read);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: "[infrastructure-category]",
                Action: "read",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        var infraSecretNames = options.Value.AllowedSecrets.Infrastructure;
        var secrets = await provider.GetSecretsAsync(infraSecretNames, ct);

        auditLogger.LogAccess(new SecretAuditEvent(
            SecretName: "[infrastructure-category]",
            Action: "read",
            UserId: authResult.UserId,
            PrincipalType: authResult.PrincipalType,
            Success: true,
            CorrelationId: context.TraceIdentifier,
            IsBreakGlass: authResult.IsBreakGlass,
            IsServiceAccount: authResult.IsServiceAccount));

        return Results.Ok(new SecretsResponse(secrets));
    }
}

public record SecretResponse(string Name, string Value);
public record SecretsResponse(Dictionary<string, string> Secrets);
public record BatchSecretsRequest(IReadOnlyList<string> SecretNames);
