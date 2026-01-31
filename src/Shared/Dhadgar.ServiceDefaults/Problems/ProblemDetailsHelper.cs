using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dhadgar.ServiceDefaults.Problems;

/// <summary>
/// Helper for creating consistent RFC 7807 ProblemDetails responses with standardized error codes and tracing.
/// </summary>
/// <remarks>
/// <para>
/// All methods automatically include:
/// </para>
/// <list type="bullet">
/// <item><description>errorCode: Machine-readable error identifier for client handling</description></item>
/// <item><description>traceId: Distributed tracing ID for correlation across services</description></item>
/// <item><description>type: URI pointing to error documentation at https://errors.meridianconsole.com/</description></item>
/// </list>
/// <para>
/// <strong>Usage Example:</strong>
/// </para>
/// <code>
/// using Dhadgar.ServiceDefaults.Problems;
///
/// // Using predefined error codes
/// return ProblemDetailsHelper.NotFound(ErrorCodes.Nodes.NodeNotFound);
///
/// // With custom detail message
/// return ProblemDetailsHelper.NotFound(
///     ErrorCodes.Nodes.NodeNotFound,
///     $"Node '{nodeId}' was not found in organization '{orgId}'."
/// );
///
/// // Without predefined constants (generates default message)
/// return ProblemDetailsHelper.BadRequest("invalid_input");
/// </code>
/// </remarks>
public static class ProblemDetailsHelper
{
    private const string TypeUriBase = "https://errors.meridianconsole.com/";

    /// <summary>
    /// Creates a 400 Bad Request ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "invalid_platform").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 400.</returns>
    public static ProblemHttpResult BadRequest(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 401 Unauthorized ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "invalid_token").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 401.</returns>
    public static ProblemHttpResult Unauthorized(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 403 Forbidden ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "access_denied").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 403.</returns>
    public static ProblemHttpResult Forbidden(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 404 Not Found ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "node_not_found").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 404.</returns>
    public static ProblemHttpResult NotFound(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 409 Conflict ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "already_in_maintenance").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 409.</returns>
    public static ProblemHttpResult Conflict(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 422 Unprocessable Entity ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "invalid_capacity").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 422.</returns>
    public static ProblemHttpResult UnprocessableEntity(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Unprocessable Entity",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Creates a 500 Internal Server Error ProblemDetails response.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "database_error").</param>
    /// <param name="detail">Human-readable explanation. If null, generates a default message from the error code.</param>
    /// <returns>A ProblemDetails response with status 500.</returns>
    public static ProblemHttpResult InternalServerError(string errorCode, string? detail = null) =>
        CreateProblem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            errorCode: errorCode,
            detail: detail);

    /// <summary>
    /// Generates a default human-readable detail message from an error code.
    /// </summary>
    /// <param name="errorCode">The error code to generate a message for.</param>
    /// <returns>A human-readable error message. If no specific message exists, converts the error code to sentence case.</returns>
    /// <remarks>
    /// This method provides default messages for common error codes across all services.
    /// Services can provide custom detail messages by passing them explicitly to the error methods.
    /// </remarks>
    public static string GetDefaultDetail(string errorCode) => errorCode switch
    {
        // Node errors
        "node_not_found" => "The specified node was not found or you don't have access to it.",
        "already_in_maintenance" => "The node is already in maintenance mode.",
        "not_in_maintenance" => "The node is not currently in maintenance mode.",
        "invalid_token" => "The enrollment token is invalid, expired, or already used.",
        "invalid_platform" => "The platform must be 'linux' or 'windows'.",
        "node_decommissioned" => "The node has been decommissioned and cannot be modified.",
        "already_decommissioned" => "The node is already decommissioned.",
        "name_already_exists" => "A node with this name already exists in the organization.",
        "node_unavailable" => "The node is not available for reservations.",
        "capacity_data_missing" => "The node does not have capacity data configured.",

        // Capacity reservation errors
        "reservation_not_found" => "The specified reservation was not found.",
        "reservation_expired" => "The reservation has expired.",
        "reservation_claimed" => "The reservation has already been claimed.",
        "reservation_released" => "The reservation has been released.",
        "reservation_already_released" => "The reservation has already been released or expired.",
        "insufficient_memory" => "Insufficient memory available on the node.",
        "insufficient_disk" => "Insufficient disk space available on the node.",

        // Identity errors
        "user_not_found" => "The specified user was not found.",
        "organization_not_found" => "The specified organization was not found.",
        "role_not_found" => "The specified role was not found.",
        "member_not_found" => "The specified member was not found in this organization.",
        "already_member" => "The user is already a member of this organization.",
        "invalid_email" => "The email address is invalid.",
        "email_already_exists" => "A user with this email address already exists.",

        // Secrets errors
        "secret_not_found" => "The specified secret was not found.",
        "access_denied" => "You do not have permission to access this resource.",
        "invalid_secret_name" => "The secret name contains invalid characters.",
        "secret_too_large" => "The secret value exceeds the maximum allowed size.",
        "rate_limit_exceeded" => "You have exceeded the rate limit for this operation.",

        // Authentication errors
        "invalid_credentials" => "The provided credentials are invalid.",
        "account_locked" => "Your account has been locked due to too many failed login attempts.",
        "session_expired" => "Your session has expired. Please log in again.",
        "token_expired" => "The authentication token has expired.",
        "invalid_oauth_provider" => "The OAuth provider is not supported.",

        // Generic errors
        "internal_error" => "An internal server error occurred. Please try again later.",
        "database_error" => "A database error occurred. Please contact support if this persists.",
        "validation_failed" => "The request contains invalid data.",

        // Default: convert snake_case to sentence case
        _ => errorCode.Replace("_", " ", StringComparison.Ordinal)
    };

    /// <summary>
    /// Creates a ProblemDetails response with standardized extensions.
    /// </summary>
    private static ProblemHttpResult CreateProblem(
        int statusCode,
        string title,
        string errorCode,
        string? detail)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["traceId"] = traceId
        };

        return TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"{TypeUriBase}{statusCode}",
            extensions: extensions);
    }
}
