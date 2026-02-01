using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dhadgar.ServiceDefaults.Extensions;

/// <summary>
/// Extension methods for mapping standard service info endpoints.
/// </summary>
public static class ServiceInfoEndpointExtensions
{
    /// <summary>
    /// Maps standard service info endpoints (/, /hello).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="serviceName">The name of the service (e.g., "Dhadgar.Identity").</param>
    /// <param name="helloMessage">The hello message to return (defaults to "Hello from {serviceName}").</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method maps two standard endpoints:
    /// <list type="bullet">
    ///   <item><c>GET /</c> - Returns JSON with service name and hello message</item>
    ///   <item><c>GET /hello</c> - Returns plain text hello message</item>
    /// </list>
    /// </para>
    /// <para>
    /// Both endpoints are tagged with "Health" for OpenAPI grouping and are marked as anonymous.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapServiceInfoEndpoints(
        this IEndpointRouteBuilder endpoints,
        string serviceName,
        string? helloMessage = null)
    {
        helloMessage ??= $"Hello from {serviceName}";

        endpoints.MapGet("/", () => Results.Ok(new { service = serviceName, message = helloMessage }))
            .WithTags("Health")
            .WithName($"{serviceName}ServiceInfo")
            .WithDescription("Get service information")
            .AllowAnonymous();

        endpoints.MapGet("/hello", () => Results.Text(helloMessage))
            .WithTags("Health")
            .WithName($"{serviceName}Hello")
            .WithDescription("Simple hello endpoint")
            .AllowAnonymous();

        return endpoints;
    }
}
