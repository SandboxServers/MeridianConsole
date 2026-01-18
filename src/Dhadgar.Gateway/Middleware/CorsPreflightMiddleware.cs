namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Handles CORS preflight (OPTIONS) requests explicitly.
/// This ensures preflight requests are handled correctly regardless of
/// downstream middleware or reverse proxy behavior.
/// </summary>
public class CorsPreflightMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CorsPreflightMiddleware> _logger;
    private readonly HashSet<string> _allowedOrigins;

    public CorsPreflightMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<CorsPreflightMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
        _allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only handle OPTIONS preflight requests
        if (!HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        var requestMethod = context.Request.Headers["Access-Control-Request-Method"].ToString();

        // Check if this is a CORS preflight request
        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(requestMethod))
        {
            await _next(context);
            return;
        }

        _logger.LogDebug("CORS preflight from {Origin} for {Method} {Path}",
            origin, requestMethod, context.Request.Path);

        // Check if origin is allowed
        if (!_allowedOrigins.Contains(origin))
        {
            _logger.LogWarning("CORS preflight rejected: origin {Origin} not in allowed list", origin);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Respond to preflight
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
        context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Correlation-Id, X-Request-Id");
        context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
        context.Response.Headers.Append("Access-Control-Max-Age", "86400"); // 24 hours
        context.Response.Headers.Append("Access-Control-Expose-Headers", "X-Correlation-Id, X-Request-Id, X-Trace-Id");

        _logger.LogDebug("CORS preflight approved for {Origin}", origin);
    }
}
