using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that transforms exceptions into RFC 7807 Problem Details responses.
/// Ensures consistent error handling across all backend services.
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception in Gateway. TraceId: {TraceId}, Path: {Path}",
            traceId, context.Request.Path);

        var problemDetails = new
        {
            type = "https://meridian.console/errors/internal-server-error",
            title = "Internal Server Error",
            status = (int)HttpStatusCode.InternalServerError,
            detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred. Please contact support with the trace ID.",
            instance = context.Request.Path.ToString(),
            traceId = traceId,
            // Include stack trace only in Development
            extensions = _environment.IsDevelopment()
                ? new { stackTrace = exception.StackTrace }
                : null
        };

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, SerializerOptions);

        await context.Response.WriteAsync(json);
    }
}
