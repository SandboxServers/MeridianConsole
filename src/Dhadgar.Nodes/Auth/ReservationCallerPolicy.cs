using Microsoft.AspNetCore.Authorization;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Authorization policy for the service-to-service reservation token endpoints.
/// Only a user principal with an <c>org_id</c> claim passes; agent principals
/// (which carry a <c>node_id</c> claim) are refused.
/// </summary>
public static class ReservationCallerPolicy
{
    public const string Name = "ReservationCaller";

    public static void Configure(AuthorizationPolicyBuilder policy)
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("org_id");
        policy.AddRequirements(new ReservationCallerRequirement());
    }
}

/// <summary>
/// Requirement that rejects any principal carrying a <c>node_id</c> claim.
/// </summary>
public sealed class ReservationCallerRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Fails any principal that is an agent identity (has a <c>node_id</c> claim).
/// </summary>
public sealed class ReservationCallerHandler : AuthorizationHandler<ReservationCallerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ReservationCallerRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == "node_id"))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
