using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// Global exception handler that transforms exceptions into RFC 9457 Problem Details responses.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly EventId ExceptionEventId = new(9300, "UnhandledException");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Get trace context using fallback chain
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.Items["CorrelationId"]?.ToString()
            ?? httpContext.TraceIdentifier
            ?? "unknown";

        var correlationId = httpContext.Items["CorrelationId"]?.ToString()
            ?? httpContext.TraceIdentifier
            ?? "unknown";

        // Classify exception to status code and type
        var (statusCode, errorType, title) = ClassifyException(exception);

        // Log the exception
        _logger.Log(
            statusCode >= 500 ? LogLevel.Error : LogLevel.Warning,
            ExceptionEventId,
            exception,
            "Unhandled exception. TraceId: {TraceId}, CorrelationId: {CorrelationId}, Path: {Path}, StatusCode: {StatusCode}",
            traceId, correlationId, httpContext.Request.Path, statusCode);

        // Build Problem Details response
        var problemDetails = new ProblemDetails
        {
            Type = errorType,
            Title = title,
            Status = statusCode,
            Instance = httpContext.Request.Path,
            Detail = GetSafeDetail(exception, statusCode)
        };

        // Add trace context extensions
        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["timestamp"] = _timeProvider.GetUtcNow();

        // Add validation errors if applicable
        if (exception is ValidationException validationException && validationException.Errors is not null)
        {
            problemDetails.Extensions["errors"] = validationException.Errors;
        }

        // Write response with explicit content type
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await httpContext.Response.WriteAsync(json, cancellationToken);

        return true;
    }

    /// <summary>
    /// Classifies an exception into HTTP status code, error type URI, and title.
    /// </summary>
    private static (int StatusCode, string ErrorType, string Title) ClassifyException(Exception exception)
    {
        return exception switch
        {
            // Domain exceptions use their own classification
            DomainException domainEx => (domainEx.StatusCode, domainEx.ErrorType, GetTitleForStatusCode(domainEx.StatusCode)),

            // Standard .NET exceptions mapped to appropriate status codes
            ArgumentNullException => (400, "https://meridian.console/errors/bad-request", "Bad Request"),
            ArgumentException => (400, "https://meridian.console/errors/bad-request", "Bad Request"),
            UnauthorizedAccessException => (401, "https://meridian.console/errors/unauthorized", "Unauthorized"),
            KeyNotFoundException => (404, "https://meridian.console/errors/not-found", "Not Found"),
            NotImplementedException => (501, "https://meridian.console/errors/not-implemented", "Not Implemented"),
            TimeoutException => (504, "https://meridian.console/errors/gateway-timeout", "Gateway Timeout"),
            OperationCanceledException => (499, "https://meridian.console/errors/client-closed-request", "Client Closed Request"),

            // Default to internal server error
            _ => (500, "https://meridian.console/errors/internal-server-error", "Internal Server Error")
        };
    }

    /// <summary>
    /// Gets a title for a given HTTP status code.
    /// </summary>
    private static string GetTitleForStatusCode(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            429 => "Too Many Requests",
            499 => "Client Closed Request",
            500 => "Internal Server Error",
            501 => "Not Implemented",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Error"
        };
    }

    /// <summary>
    /// Gets the exception detail, hiding internal details in production for 5xx errors.
    /// </summary>
    private string GetSafeDetail(Exception exception, int statusCode)
    {
        var includeDetails = _environment.IsDevelopment() || _environment.IsEnvironment("Testing");

        // Always include detail for client errors (4xx)
        if (statusCode < 500)
        {
            return exception.Message;
        }

        // For server errors (5xx), only include detail in development/testing
        return includeDetails
            ? exception.Message
            : "An unexpected error occurred. Please contact support with the trace ID.";
    }
}
