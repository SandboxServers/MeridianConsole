using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that logs HTTP requests and responses with correlation context.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
            stopwatch.Stop();

            var level = context.Response.StatusCode >= 500
                ? LogLevel.Error
                : context.Response.StatusCode >= 400
                    ? LogLevel.Warning
                    : LogLevel.Information;

            _logger.Log(level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [CorrelationId: {CorrelationId}]",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "HTTP {Method} {Path} failed after {ElapsedMs}ms [CorrelationId: {CorrelationId}]",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds,
                correlationId);
            throw;
        }
    }
}
