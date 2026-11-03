using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// Builds the problem+json response for a request rejected while binding (malformed JSON, wrong JSON type,
/// oversized or unsupported request). Shared by <see cref="Middleware.ProblemDetailsMiddleware"/> and
/// <see cref="GlobalExceptionHandler"/> so both answer binding failures the same way.
/// </summary>
/// <remarks>
/// The status code is the exception's own 4xx status. The type and title come from a fixed table
/// (D-ML233). Only Development and Testing see the exception message; elsewhere the detail is fixed text.
/// No stack trace is ever included.
/// </remarks>
public static class BindingFailureProblem
{
    /// <summary>Event id for the binding-failure log entry. It never carries "Unhandled exception" text.</summary>
    public static readonly EventId EventId = new(9301, "BindingFailure");

    private const string BadRequestType = "https://meridian.console/errors/bad-request";
    private const string FixedDetail = "The request could not be read.";

    /// <summary>
    /// True for a <see cref="BadHttpRequestException"/> with a 4xx status (this also matches the Kestrel
    /// <c>Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException</c>, which derives from it in .NET 8+).
    /// On success, <paramref name="problem"/> holds the problem with its <see cref="ProblemDetails.Status"/> set.
    /// </summary>
    public static bool TryCreate(
        Exception exception,
        HttpContext context,
        IHostEnvironment env,
        string traceId,
        string correlationId,
        DateTimeOffset now,
        [NotNullWhen(true)] out ProblemDetails? problem)
    {
        if (exception is not BadHttpRequestException badRequest || badRequest.StatusCode is < 400 or > 499)
        {
            problem = null;
            return false;
        }

        var statusCode = badRequest.StatusCode;
        var (type, title) = Describe(statusCode);
        var includeDetails = env.IsDevelopment() || env.IsEnvironment("Testing");

        problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Detail = includeDetails ? badRequest.Message : FixedDetail,
            Instance = context.Request.Path.ToString()
        };

        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["timestamp"] = now;

        return true;
    }

    /// <summary>Logs a binding failure as a Warning. The entry carries no exception message and no stack trace.</summary>
    public static void Log(ILogger logger, int statusCode, PathString path, string traceId) =>
        logger.LogWarning(EventId, "Request rejected at binding. StatusCode: {StatusCode}, Path: {Path}, TraceId: {TraceId}",
            statusCode, path, traceId);

    private static (string Type, string Title) Describe(int statusCode)
    {
        return statusCode switch
        {
            400 => (BadRequestType, "Bad Request"),
            408 => ("https://meridian.console/errors/request-timeout", "Request Timeout"),
            413 => ("https://meridian.console/errors/payload-too-large", "Payload Too Large"),
            415 => ("https://meridian.console/errors/unsupported-media-type", "Unsupported Media Type"),
            431 => ("https://meridian.console/errors/request-header-fields-too-large", "Request Header Fields Too Large"),
            _ => (BadRequestType, "Bad Request")
        };
    }
}
