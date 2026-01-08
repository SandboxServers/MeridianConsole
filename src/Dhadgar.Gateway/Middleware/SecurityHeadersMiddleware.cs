namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses.
/// Protects against XSS, clickjacking, MIME sniffing, and protocol downgrade attacks.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent XSS attacks
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers.Remove("X-XSS-Protection");

        // Content Security Policy (restrictive for API)
        headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'";

        // Referrer policy
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Permissions policy (disable unnecessary features)
        headers["Permissions-Policy"] =
            "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

        // HSTS (production only, 1 year)
        if (!_environment.IsDevelopment())
        {
            headers["Strict-Transport-Security"] =
                "max-age=31536000; includeSubDomains; preload";
        }

        // Remove server header (handled via Kestrel config in Program)
        headers.Remove("X-Powered-By");

        await _next(context);
    }
}
