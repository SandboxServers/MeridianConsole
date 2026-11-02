using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Authentication scheme names for Nodes. Agents authenticate with mTLS on agent paths; users authenticate with a JWT
/// everywhere else. The default policy scheme picks one by path so neither credential is evaluated on the other's paths (D-35).
/// </summary>
public static class NodesAuthenticationSchemes
{
    /// <summary>Policy scheme that picks agent mTLS or user JWT by path (D-35).</summary>
    public const string Default = "NodesAuth";

    /// <summary>
    /// Selects the agent mTLS scheme for <c>AgentEndpointPrefix</c> and <c>HubPathPrefix</c> (whole segments, case-insensitive),
    /// and the JWT bearer scheme for every other path.
    /// </summary>
    public static string SelectScheme(HttpContext context)
    {
        var mtls = context.RequestServices.GetRequiredService<IOptions<MtlsOptions>>().Value;
        var path = context.Request.Path;

        var isAgentPath = path.StartsWithSegments(Prefix(mtls.AgentEndpointPrefix), StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(Prefix(mtls.HubPathPrefix), StringComparison.OrdinalIgnoreCase);

        return isAgentPath
            ? AgentMtlsAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    }

    private static PathString Prefix(string prefix) => new(MtlsMiddleware.NormalizePrefix(prefix));
}
