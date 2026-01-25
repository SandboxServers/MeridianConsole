using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that transforms exceptions into RFC 9457 Problem Details responses.
/// Acts as a fallback safety net for any exceptions not caught by IExceptionHandler.
/// </summary>
/// <remarks>
/// For new services, prefer using <see cref="Dhadgar.ServiceDefaults.Errors.ErrorHandlingExtensions.AddDhadgarErrorHandling"/>
/// which registers <see cref="Dhadgar.ServiceDefaults.Errors.GlobalExceptionHandler"/> as the primary exception handler.
/// This middleware exists for backwards compatibility and as an additional safety net.
/// </remarks>
public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment,
        TimeProvider? timeProvider = null)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // TRACE-04: Get TraceId from Activity.Current (set by OTEL AspNetCore instrumentation)
        // Fall back to CorrelationId if no active trace, then to HttpContext.TraceIdentifier
        var traceId = Activity.Current?.TraceId.ToString()
            ?? context.Items["CorrelationId"]?.ToString()
            ?? context.TraceIdentifier
            ?? "unknown";

        // ERR-02: Also include correlationId as a separate field
        var correlationId = context.Items["CorrelationId"]?.ToString()
            ?? context.TraceIdentifier
            ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception (fallback handler). TraceId: {TraceId}, CorrelationId: {CorrelationId}, Path: {Path}",
            traceId, correlationId, context.Request.Path);

        var includeDetails = _environment.IsDevelopment() || _environment.IsEnvironment("Testing");

        // Use standard ProblemDetails class for RFC 9457 compliance
        var problemDetails = new ProblemDetails
        {
            Type = "https://meridian.console/errors/internal-server-error",
            Title = "Internal Server Error",
            Status = (int)HttpStatusCode.InternalServerError,
            Detail = includeDetails
                ? exception.Message
                : "An unexpected error occurred. Please contact support with the trace ID.",
            Instance = context.Request.Path.ToString()
        };

        // Add trace context extensions
        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["timestamp"] = _timeProvider.GetUtcNow();

        // In development, also include stack trace
        if (includeDetails)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problemDetails, context.RequestAborted);
    }
}
