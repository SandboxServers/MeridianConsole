namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Writes the problem+json bodies for mTLS authentication failures (C5).
/// Shared by <see cref="MtlsMiddleware"/> and <see cref="AgentMtlsAuthenticationHandler"/>.
/// </summary>
internal static class MtlsProblemWriter
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc7235#section-3.1";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc7231#section-6.5.3";

    /// <summary>
    /// Writes a 401 problem+json response with the given machine-readable error code.
    /// </summary>
    public static Task WriteUnauthorizedAsync(HttpContext context, string errorCode, string errorMessage)
    {
        return WriteAsync(context, StatusCodes.Status401Unauthorized, UnauthorizedType, "Unauthorized",
            errorCode, errorMessage);
    }

    /// <summary>
    /// Writes a 403 problem+json response with the given machine-readable error code.
    /// </summary>
    public static Task WriteForbiddenAsync(HttpContext context, string errorCode, string errorMessage)
    {
        return WriteAsync(context, StatusCodes.Status403Forbidden, ForbiddenType, "Forbidden",
            errorCode, errorMessage);
    }

    private static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string type,
        string title,
        string errorCode,
        string errorMessage)
    {
        context.Response.StatusCode = statusCode;

        var problemDetails = new
        {
            type,
            title,
            status = statusCode,
            detail = errorMessage,
            instance = context.Request.Path.Value,
            errorCode
        };

        // Pass the content type here: WriteAsJsonAsync would otherwise replace it with application/json.
        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }
}
