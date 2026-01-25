using System.Diagnostics;
using Dhadgar.ServiceDefaults.Logging;
using Microsoft.AspNetCore.Http;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that logs HTTP requests and responses using source-generated logging.
/// Uses <see cref="RequestLoggingMessages"/> for high-performance, allocation-free logging.
/// </summary>
/// <remarks>
/// <para>
/// This middleware relies on <see cref="TenantEnrichmentMiddleware"/> to establish the logging scope
/// with correlation IDs and tenant context. The logging scope is NOT created here - all context
/// is inherited from the upstream middleware.
/// </para>
/// <para>
/// Recommended middleware order:
/// <list type="number">
///   <item><see cref="CorrelationMiddleware"/> - Sets CorrelationId and RequestId</item>
///   <item><see cref="TenantEnrichmentMiddleware"/> - Adds all context to logging scope</item>
///   <item><see cref="RequestLoggingMiddleware"/> - Logs requests with full context (this middleware)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestLoggingMessages _requestLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLoggingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="requestLogger">The source-generated request logging messages.</param>
    public RequestLoggingMiddleware(RequestDelegate next, RequestLoggingMessages requestLogger)
    {
        _next = next;
        _requestLogger = requestLogger;
    }

    /// <summary>
    /// Invokes the middleware, logging the request and response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
            stopwatch.Stop();

            _requestLogger.LogRequestCompleted(
                method,
                path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _requestLogger.LogRequestFailed(
                method,
                path,
                stopwatch.ElapsedMilliseconds,
                ex);
            throw;
        }
    }
}
