using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dhadgar.Nodes.Endpoints;

/// <summary>
/// Helper for creating consistent RFC 7807 ProblemDetails responses.
/// </summary>
public static class ProblemDetailsHelper
{
    /// <summary>
    /// Creates a 401 Unauthorized ProblemDetails response.
    /// </summary>
    public static ProblemHttpResult Unauthorized(string errorCode, string? detail = null) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"https://httpstatuses.com/401",
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    /// <summary>
    /// Creates a 400 Bad Request ProblemDetails response.
    /// </summary>
    public static ProblemHttpResult BadRequest(string errorCode, string? detail = null) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"https://httpstatuses.com/400",
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    /// <summary>
    /// Creates a 404 Not Found ProblemDetails response.
    /// </summary>
    public static ProblemHttpResult NotFound(string errorCode, string? detail = null) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"https://httpstatuses.com/404",
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    /// <summary>
    /// Creates a 409 Conflict ProblemDetails response.
    /// </summary>
    public static ProblemHttpResult Conflict(string errorCode, string? detail = null) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"https://httpstatuses.com/409",
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    /// <summary>
    /// Creates a 422 Unprocessable Entity ProblemDetails response.
    /// </summary>
    public static ProblemHttpResult UnprocessableEntity(string errorCode, string? detail = null) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Unprocessable Entity",
            detail: detail ?? GetDefaultDetail(errorCode),
            type: $"https://httpstatuses.com/422",
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    private static string GetDefaultDetail(string errorCode) => errorCode switch
    {
        "node_not_found" => "The specified node was not found or you don't have access to it.",
        "already_in_maintenance" => "The node is already in maintenance mode.",
        "not_in_maintenance" => "The node is not currently in maintenance mode.",
        "invalid_token" => "The enrollment token is invalid, expired, or already used.",
        "invalid_platform" => "The platform must be 'linux' or 'windows'.",
        "node_decommissioned" => "The node has been decommissioned and cannot be modified.",
        "already_decommissioned" => "The node is already decommissioned.",
        "name_already_exists" => "A node with this name already exists in the organization.",
        // Capacity reservation errors
        "reservation_not_found" => "The specified reservation was not found.",
        "reservation_expired" => "The reservation has expired.",
        "reservation_claimed" => "The reservation has already been claimed.",
        "reservation_released" => "The reservation has been released.",
        "reservation_already_released" => "The reservation has already been released or expired.",
        "node_unavailable" => "The node is not available for reservations.",
        "capacity_data_missing" => "The node does not have capacity data configured.",
        "insufficient_memory" => "Insufficient memory available on the node.",
        "insufficient_disk" => "Insufficient disk space available on the node.",
        _ => errorCode.Replace("_", " ")
    };
}
