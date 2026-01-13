using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that transforms exceptions into RFC 7807 Problem Details responses.
/// </summary>
public sealed class ProblemDetailsMiddleware
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
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write problem details response after headers sent. Exception: {ExceptionType}",
                exception.GetType().Name);
            return;
        }

        var traceId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception. TraceId: {TraceId}, Path: {Path}",
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
            extensions = _environment.IsDevelopment()
                ? new { stackTrace = exception.StackTrace }
                : null
        };

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, SerializerOptions);

        await context.Response.WriteAsync(json, context.RequestAborted);
    }
}
