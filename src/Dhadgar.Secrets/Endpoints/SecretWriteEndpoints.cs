using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Audit;
using Dhadgar.Secrets.Services;
using Dhadgar.Secrets.Validation;
using Microsoft.AspNetCore.Mvc;

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
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Delete a secret
        group.MapDelete("/{secretName}", DeleteSecret)
            .WithName("DeleteSecret")
            .WithDescription("Delete a secret (soft delete if vault has soft delete enabled).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SetSecret(
        string secretName,
        [FromBody] SetSecretRequest request,
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
            return Results.BadRequest(new { error = validation.ErrorMessage });
        }

        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Results.BadRequest(new { error = "Value is required." });
        }

        // Check if secret is in allowed list
        if (!provider.IsAllowed(secretName))
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "write",
                UserId: context.User.FindFirst("sub")?.Value,
                Reason: "Secret not in allowed list",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        // Authorize
        var authResult = authService.Authorize(context.User, secretName, SecretAction.Write);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "write",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        try
        {
            var success = await provider.SetSecretAsync(secretName, request.Value, ct);

            auditLogger.LogModification(new SecretModificationEvent(
                SecretName: secretName,
                Action: "write",
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                Success: success,
                CorrelationId: context.TraceIdentifier,
                ErrorMessage: success ? null : "SetSecretAsync returned false"));

            if (!success)
            {
                return Results.Forbid();
            }

            return Results.Ok(new SetSecretResponse(secretName, true));
        }
        catch (InvalidOperationException ex)
        {
            auditLogger.LogModification(new SecretModificationEvent(
                SecretName: secretName,
                Action: "write",
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                Success: false,
                CorrelationId: context.TraceIdentifier,
                ErrorMessage: ex.Message));

            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RotateSecret(
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
            return Results.BadRequest(new { error = validation.ErrorMessage });
        }

        // Check if secret is in allowed list
        if (!provider.IsAllowed(secretName))
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "rotate",
                UserId: context.User.FindFirst("sub")?.Value,
                Reason: "Secret not in allowed list",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        // Authorize - rotation requires specific permission
        var authResult = authService.Authorize(context.User, secretName, SecretAction.Rotate);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "rotate",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        try
        {
            var (version, createdAt) = await provider.RotateSecretAsync(secretName, ct);

            auditLogger.LogRotation(new SecretRotationEvent(
                SecretName: secretName,
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                NewVersion: version,
                Success: true,
                CorrelationId: context.TraceIdentifier));

            return Results.Ok(new RotateSecretResponse(
                Name: secretName,
                Version: version,
                RotatedAt: createdAt,
                ExpiresAt: null
            ));
        }
        catch (UnauthorizedAccessException ex)
        {
            auditLogger.LogRotation(new SecretRotationEvent(
                SecretName: secretName,
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                NewVersion: null,
                Success: false,
                CorrelationId: context.TraceIdentifier,
                ErrorMessage: ex.Message));

            return Results.Forbid();
        }
        catch (Exception ex)
        {
            auditLogger.LogRotation(new SecretRotationEvent(
                SecretName: secretName,
                UserId: authResult.UserId,
                PrincipalType: authResult.PrincipalType,
                NewVersion: null,
                Success: false,
                CorrelationId: context.TraceIdentifier,
                ErrorMessage: ex.Message));

            return Results.Problem(
                title: "Rotation failed",
                detail: "An error occurred during secret rotation.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeleteSecret(
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
            return Results.BadRequest(new { error = validation.ErrorMessage });
        }

        // Check if secret is in allowed list
        if (!provider.IsAllowed(secretName))
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "delete",
                UserId: context.User.FindFirst("sub")?.Value,
                Reason: "Secret not in allowed list",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        // Authorize - delete requires write permission
        var authResult = authService.Authorize(context.User, secretName, SecretAction.Delete);
        if (!authResult.IsAuthorized)
        {
            auditLogger.LogAccessDenied(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "delete",
                UserId: authResult.UserId,
                Reason: authResult.DenialReason ?? "Authorization denied",
                CorrelationId: context.TraceIdentifier));

            return Results.Forbid();
        }

        var success = await provider.DeleteSecretAsync(secretName, ct);

        auditLogger.LogModification(new SecretModificationEvent(
            SecretName: secretName,
            Action: "delete",
            UserId: authResult.UserId,
            PrincipalType: authResult.PrincipalType,
            Success: success,
            CorrelationId: context.TraceIdentifier,
            ErrorMessage: success ? null : "Secret not found"));

        if (!success)
        {
            return Results.NotFound(new { error = $"Secret '{secretName}' not found." });
        }

        return Results.NoContent();
    }
}

public record SetSecretRequest(string Value);
public record SetSecretResponse(string Name, bool Updated);
public record RotateSecretResponse(string Name, string Version, DateTime RotatedAt, DateTime? ExpiresAt);
