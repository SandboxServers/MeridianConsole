using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;

namespace Dhadgar.Nodes.Endpoints;

public static class EnrollmentEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/enrollment")
            .WithTags("Enrollment")
            .RequireAuthorization("TenantScoped");

        group.MapPost("/tokens", CreateToken)
            .WithName("CreateEnrollmentToken")
            .WithDescription("Create a new enrollment token")
            .Produces<CreateEnrollmentTokenResponse>(201)
            .Produces(400);

        group.MapGet("/tokens", ListTokens)
            .WithName("ListEnrollmentTokens")
            .WithDescription("List active enrollment tokens")
            .Produces<IReadOnlyList<EnrollmentTokenSummary>>();

        group.MapDelete("/tokens/{tokenId:guid}", RevokeToken)
            .WithName("RevokeEnrollmentToken")
            .WithDescription("Revoke an enrollment token")
            .Produces(204)
            .Produces(404);
    }

    private static async Task<IResult> CreateToken(
        Guid organizationId,
        CreateEnrollmentTokenRequest request,
        HttpContext context,
        IEnrollmentTokenService tokenService,
        CancellationToken ct = default)
    {
        // Get user ID from claims (placeholder - actual implementation depends on auth setup)
        var userId = context.User.FindFirst("sub")?.Value ?? "system";

        TimeSpan? validity = request.ExpiresInMinutes.HasValue
            ? TimeSpan.FromMinutes(request.ExpiresInMinutes.Value)
            : null;

        var (token, plainTextToken) = await tokenService.CreateTokenAsync(
            organizationId,
            userId,
            request.Label,
            validity,
            ct);

        var response = new CreateEnrollmentTokenResponse(
            token.Id,
            plainTextToken,
            token.ExpiresAt);

        return Results.Created($"/api/v1/organizations/{organizationId}/enrollment/tokens/{token.Id}", response);
    }

    private static async Task<IResult> ListTokens(
        Guid organizationId,
        IEnrollmentTokenService tokenService,
        CancellationToken ct = default)
    {
        var tokens = await tokenService.GetActiveTokensAsync(organizationId, ct);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> RevokeToken(
        Guid organizationId,
        Guid tokenId,
        IEnrollmentTokenService tokenService,
        CancellationToken ct = default)
    {
        var revoked = await tokenService.RevokeTokenAsync(organizationId, tokenId, ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }
}
