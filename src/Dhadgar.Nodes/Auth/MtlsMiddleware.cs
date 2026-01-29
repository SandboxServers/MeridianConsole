using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Middleware for validating mTLS client certificates on agent endpoints.
/// Extracts the node ID from the certificate's SPIFFE ID and stores it in HttpContext.Items.
/// </summary>
/// <remarks>
/// This middleware:
/// - Only processes requests to agent endpoints (configurable via MtlsOptions.AgentEndpointPrefix)
/// - Allows unauthenticated access to exempt paths (enroll, ca-certificate)
/// - Validates certificates were issued by our CA
/// - Extracts node ID from SPIFFE ID in certificate SAN
/// - Stores validated node ID in HttpContext.Items["NodeId"]
/// - Creates a ClaimsPrincipal with node_id claim for authorization
/// </remarks>
public sealed class MtlsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MtlsOptions _options;
    private readonly ILogger<MtlsMiddleware> _logger;

    /// <summary>
    /// Key used to store the validated node ID in HttpContext.Items.
    /// </summary>
    public const string NodeIdItemKey = "NodeId";

    /// <summary>
    /// Key used to store the certificate thumbprint in HttpContext.Items.
    /// </summary>
    public const string CertificateThumbprintItemKey = "CertificateThumbprint";

    /// <summary>
    /// Key used to store the SPIFFE ID in HttpContext.Items.
    /// </summary>
    public const string SpiffeIdItemKey = "SpiffeId";

    public MtlsMiddleware(
        RequestDelegate next,
        IOptions<MtlsOptions> options,
        ILogger<MtlsMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICertificateValidationService validationService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Check if this is an agent endpoint
        if (!IsAgentEndpoint(path))
        {
            await _next(context);
            return;
        }

        // Check if this path is exempt from mTLS
        if (IsExemptPath(path))
        {
            _logger.LogDebug("Path {Path} is exempt from mTLS validation", path);
            await _next(context);
            return;
        }

        // If mTLS is disabled, skip validation (development mode)
        if (!_options.Enabled)
        {
            _logger.LogDebug("mTLS is disabled, skipping certificate validation for {Path}", path);
            await _next(context);
            return;
        }

        // Get the client certificate
        var clientCertificate = await context.Connection.GetClientCertificateAsync();

        if (clientCertificate is null)
        {
            if (_options.RequireClientCertificate)
            {
                _logger.LogWarning(
                    "No client certificate provided for protected endpoint {Path}",
                    path);

                await WriteUnauthorizedResponse(context, "missing_client_certificate",
                    "A client certificate is required for this endpoint");
                return;
            }

            _logger.LogDebug(
                "No client certificate provided but RequireClientCertificate=false, allowing request to {Path}",
                path);

            await _next(context);
            return;
        }

        // Validate the certificate
        var validationResult = await validationService.ValidateClientCertificateAsync(clientCertificate);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Certificate validation failed for {Path}. Error: {ErrorCode} - {ErrorMessage}",
                path, validationResult.ErrorCode, validationResult.ErrorMessage);

            await WriteUnauthorizedResponse(context, validationResult.ErrorCode!,
                validationResult.ErrorMessage!);
            return;
        }

        // Store validated information in HttpContext.Items
        context.Items[NodeIdItemKey] = validationResult.NodeId!.Value;
        context.Items[CertificateThumbprintItemKey] = validationResult.Thumbprint;
        context.Items[SpiffeIdItemKey] = validationResult.SpiffeId;

        // Create a ClaimsPrincipal with the node identity
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validationResult.NodeId!.Value.ToString()),
            new("node_id", validationResult.NodeId!.Value.ToString()),
            new("spiffe_id", validationResult.SpiffeId!),
            new("certificate_thumbprint", validationResult.Thumbprint!)
        };

        var identity = new ClaimsIdentity(claims, "mTLS");
        var principal = new ClaimsPrincipal(identity);

        // Set the user on the context (this allows [Authorize] to work)
        context.User = principal;

        _logger.LogDebug(
            "mTLS authentication successful for node {NodeId} on {Path}",
            validationResult.NodeId, path);

        await _next(context);
    }

    private bool IsAgentEndpoint(string path)
    {
        return path.StartsWith(_options.AgentEndpointPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExemptPath(string path)
    {
        // Remove the prefix to get the relative path
        var relativePath = path[_options.AgentEndpointPrefix.Length..];

        foreach (var exemptPath in _options.ExemptPaths)
        {
            // Exact match
            if (string.Equals(relativePath, exemptPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Match with or without trailing slash
            if (relativePath.StartsWith(exemptPath, StringComparison.OrdinalIgnoreCase))
            {
                // Must be exact match or followed by '/' or query string
                var remaining = relativePath[exemptPath.Length..];
                if (remaining.Length == 0 ||
                    remaining.StartsWith('/') ||
                    remaining.StartsWith('?'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task WriteUnauthorizedResponse(
        HttpContext context,
        string errorCode,
        string errorMessage)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            title = "Unauthorized",
            status = 401,
            detail = errorMessage,
            instance = context.Request.Path.Value,
            errorCode
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}

/// <summary>
/// Extension methods for adding mTLS middleware and services.
/// </summary>
public static class MtlsMiddlewareExtensions
{
    /// <summary>
    /// Adds mTLS services to the service collection.
    /// </summary>
    public static IServiceCollection AddMtlsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MtlsOptions>()
            .Bind(configuration.GetSection(MtlsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<ICertificateValidationService, CertificateValidationService>();

        return services;
    }

    /// <summary>
    /// Adds the mTLS middleware to the application pipeline.
    /// This should be called after UseRouting() but before UseAuthorization().
    /// </summary>
    public static IApplicationBuilder UseMtlsAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<MtlsMiddleware>();
    }
}
