using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.Serialization;

/// <summary>
/// Extension methods for configuring JSON serialization.
/// </summary>
public static class JsonSerializerExtensions
{
    /// <summary>
    /// Configures strict JSON serialization for security hardening.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures the following security hardening options:
    /// <list type="bullet">
    ///   <item>
    ///     <term>AllowDuplicateProperties = false</term>
    ///     <description>
    ///     Rejects JSON payloads with duplicate property names (e.g., {"isAdmin": false, "isAdmin": true}).
    ///     This prevents property shadowing attacks where an attacker attempts to exploit deserializer
    ///     behavior by sending conflicting values for the same property.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>PropertyNamingPolicy = JsonNamingPolicy.CamelCase</term>
    ///     <description>
    ///     Uses camelCase naming for JSON properties (e.g., "userId" instead of "UserId") for
    ///     consistency with JavaScript/TypeScript clients.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Note:</strong> UnmappedMemberHandling.Disallow is intentionally NOT set globally
    /// to avoid breaking existing clients that send extra fields. Individual services or endpoints
    /// can opt into stricter validation per-endpoint if needed.
    /// </para>
    /// <para>
    /// This configuration is automatically applied when using <see cref="ServiceDefaultsExtensions.AddDhadgarServiceDefaults"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// Usage in a service:
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddStrictJsonSerialization();
    /// </code>
    /// </example>
    public static IServiceCollection AddStrictJsonSerialization(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options =>
        {
            // Security: Reject duplicate properties to prevent property shadowing attacks
            // Example attack: {"isAdmin": false, "isAdmin": true}
            // Without this check, the deserializer might accept the payload and use
            // the second value, potentially bypassing authorization checks
            options.SerializerOptions.AllowDuplicateProperties = false;

            // Use camelCase for JSON property names (e.g., "userId" instead of "UserId")
            // This provides consistency with JavaScript/TypeScript clients
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

            // Note: UnmappedMemberHandling.Disallow is intentionally not set
            // to avoid breaking clients that send extra fields. Services can
            // opt-in to stricter handling per-endpoint if needed using:
            // [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
        });

        return services;
    }
}
