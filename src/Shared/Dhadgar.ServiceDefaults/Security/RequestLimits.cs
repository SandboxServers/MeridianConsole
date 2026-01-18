using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Security;

/// <summary>
/// Configuration options for request size limits.
/// </summary>
public sealed class RequestLimitsOptions
{
    /// <summary>
    /// Default maximum request body size in bytes. Default: 1MB.
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 1_048_576; // 1 MB

    /// <summary>
    /// Maximum request header total size in bytes. Default: 32KB.
    /// </summary>
    public int MaxRequestHeadersTotalSize { get; set; } = 32_768; // 32 KB

    /// <summary>
    /// Maximum request line size in bytes. Default: 8KB.
    /// </summary>
    public int MaxRequestLineSize { get; set; } = 8_192; // 8 KB

    /// <summary>
    /// Maximum request body size for file upload endpoints in bytes. Default: 50MB.
    /// </summary>
    public long MaxFileUploadSize { get; set; } = 52_428_800; // 50 MB
}

/// <summary>
/// Extension methods for configuring request size limits.
/// </summary>
public static class RequestLimitsExtensions
{
    /// <summary>
    /// Configures Kestrel request size limits.
    /// Call this in ConfigureWebHostDefaults or CreateBuilder.
    /// </summary>
    public static WebApplicationBuilder ConfigureRequestLimits(
        this WebApplicationBuilder builder,
        Action<RequestLimitsOptions>? configure = null)
    {
        var options = new RequestLimitsOptions();
        configure?.Invoke(options);

        // Configure Kestrel limits
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Limits.MaxRequestBodySize = options.MaxRequestBodySize;
            kestrelOptions.Limits.MaxRequestHeadersTotalSize = options.MaxRequestHeadersTotalSize;
            kestrelOptions.Limits.MaxRequestLineSize = options.MaxRequestLineSize;
        });

        return builder;
    }

    /// <summary>
    /// Disables request size limit for a specific endpoint (e.g., file uploads).
    /// </summary>
    public static RouteHandlerBuilder DisableRequestSizeLimit(this RouteHandlerBuilder builder)
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new DisableRequestSizeLimitAttribute());
        });
        return builder;
    }

    /// <summary>
    /// Sets a custom request size limit for a specific endpoint.
    /// </summary>
    public static RouteHandlerBuilder WithRequestSizeLimit(this RouteHandlerBuilder builder, long bytes)
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new RequestSizeLimitAttribute(bytes));
        });
        return builder;
    }
}

/// <summary>
/// Middleware that enforces request body size limits with proper error responses.
/// </summary>
public sealed class RequestLimitsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLimitsMiddleware> _logger;

    public RequestLimitsMiddleware(RequestDelegate next, ILogger<RequestLimitsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            _logger.LogWarning(
                "Request body too large from {ClientIp} to {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "request_too_large",
                message = "Request body exceeds maximum allowed size"
            });
        }
    }
}

/// <summary>
/// Extension to use the request limits middleware.
/// </summary>
public static class RequestLimitsMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware that converts request size limit exceptions to proper JSON responses.
    /// </summary>
    public static IApplicationBuilder UseRequestLimitsMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLimitsMiddleware>();
    }
}
