using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>Requires an agent identity: a <c>node_id</c> claim established by mTLS client certificate validation (F-02).</summary>
public sealed class AgentIdentityRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Accepts only identities whose <c>node_id</c> came from the mTLS scheme. The node is taken from the certificate's
/// SPIFFE ID, never from a header, body or user JWT claim. A bearer identity passes only when mTLS is disabled,
/// which <see cref="MtlsMiddlewareExtensions.AddMtlsAuthentication"/> permits only in Development.
/// </summary>
public sealed class AgentIdentityHandler(IOptions<MtlsOptions> mtls) : AuthorizationHandler<AgentIdentityRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentIdentityRequirement requirement)
    {
        var hasNodeId = Guid.TryParse(context.User.FindFirst("node_id")?.Value, out _);
        var viaMtls = context.User.Identities.Any(
            identity => identity.IsAuthenticated && identity.AuthenticationType == MtlsMiddleware.AuthenticationType);

        if (hasNodeId && (viaMtls || !mtls.Value.Enabled))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
