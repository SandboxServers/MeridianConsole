using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Logging;

/// <summary>
/// Source-generated log messages for HTTP request/response logging.
/// Uses [LoggerMessage] for high-performance, allocation-free logging.
/// </summary>
/// <remarks>
/// <para>
/// EventId range: 9100-9199 (Infrastructure/HTTP subset of InfraEvents).
/// </para>
/// <para>
/// This class follows the pattern established by <see cref="Dhadgar.ServiceDefaults.Security.SecurityEventLogger"/>:
/// public wrapper methods that handle conditional logic, calling private partial methods
/// decorated with [LoggerMessage] for source generation.
/// </para>
/// <para>
/// Usage:
/// </para>
/// <code>
/// public class RequestLoggingMiddleware
/// {
///     private readonly RequestLoggingMessages _requestLogger;
///
///     public RequestLoggingMiddleware(RequestDelegate next, RequestLoggingMessages requestLogger)
///     {
///         _requestLogger = requestLogger;
///     }
///
///     public async Task InvokeAsync(HttpContext context)
///     {
///         _requestLogger.LogRequestStarted(context.Request.Method, context.Request.Path);
///         // ... execute request ...
///         _requestLogger.LogRequestCompleted(context.Request.Method, context.Request.Path, statusCode, elapsedMs);
///     }
/// }
/// </code>
/// </remarks>
public sealed partial class RequestLoggingMessages
{
    private readonly ILogger<RequestLoggingMessages> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLoggingMessages"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public RequestLoggingMessages(ILogger<RequestLoggingMessages> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs that an HTTP request has started.
    /// </summary>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="path">The request path.</param>
    public void LogRequestStarted(string method, string path)
    {
        HttpRequestStarted(method, path);
    }

    /// <summary>
    /// Logs that an HTTP request has completed, selecting the appropriate log level
    /// based on the status code.
    /// </summary>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="path">The request path.</param>
    /// <param name="statusCode">The HTTP status code returned.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
    /// <remarks>
    /// <para>Log level selection:</para>
    /// <list type="bullet">
    ///   <item>Status code &gt;= 500: Error level (server errors)</item>
    ///   <item>Status code &gt;= 400 and &lt; 500: Warning level (client errors)</item>
    ///   <item>Status code &lt; 400: Information level (success)</item>
    /// </list>
    /// </remarks>
    public void LogRequestCompleted(string method, string path, int statusCode, long elapsedMs)
    {
        if (statusCode >= 500)
        {
            HttpRequestCompletedError(method, path, statusCode, elapsedMs);
        }
        else if (statusCode >= 400)
        {
            HttpRequestCompletedWithWarning(method, path, statusCode, elapsedMs);
        }
        else
        {
            HttpRequestCompleted(method, path, statusCode, elapsedMs);
        }
    }

    /// <summary>
    /// Logs that an HTTP request failed with an exception.
    /// </summary>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="path">The request path.</param>
    /// <param name="elapsedMs">The elapsed time in milliseconds before the failure.</param>
    /// <param name="exception">The exception that occurred.</param>
    public void LogRequestFailed(string method, string path, long elapsedMs, Exception exception)
    {
        HttpRequestFailed(exception, method, path, elapsedMs);
    }

    // Source-generated logging methods using EventId range 9100-9199 (Infrastructure/HTTP)

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Debug,
        Message = "HTTP {Method} {Path} started")]
    private partial void HttpRequestStarted(string method, string path);

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms")]
    private partial void HttpRequestCompleted(string method, string path, int statusCode, long elapsedMs);

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Warning,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms")]
    private partial void HttpRequestCompletedWithWarning(string method, string path, int statusCode, long elapsedMs);

    [LoggerMessage(
        EventId = 9104,
        Level = LogLevel.Error,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms")]
    private partial void HttpRequestCompletedError(string method, string path, int statusCode, long elapsedMs);

    [LoggerMessage(
        EventId = 9105,
        Level = LogLevel.Error,
        Message = "HTTP {Method} {Path} failed after {ElapsedMs}ms")]
    private partial void HttpRequestFailed(Exception exception, string method, string path, long elapsedMs);
}
