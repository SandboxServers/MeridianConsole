using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dhadgar.Nodes.Endpoints;

public static class ReservationEndpoints
{
    public static void Map(WebApplication app)
    {
        // Organization-scoped endpoints (user-facing)
        var orgGroup = app.MapGroup("/api/v1/organizations/{organizationId:guid}/nodes/{nodeId:guid}/reservations")
            .WithTags("Capacity Reservations")
            .RequireAuthorization("TenantScoped");

        orgGroup.MapPost("", CreateReservation)
            .WithName("CreateReservation")
            .WithDescription("Create a capacity reservation on a node")
            .Produces<ReservationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(404);

        orgGroup.MapGet("", ListReservations)
            .WithName("ListNodeReservations")
            .WithDescription("List active reservations for a node")
            .Produces<IReadOnlyList<ReservationSummary>>();

        orgGroup.MapGet("/capacity", GetAvailableCapacity)
            .WithName("GetAvailableCapacity")
            .WithDescription("Get available capacity on a node accounting for reservations")
            .Produces<AvailableCapacityResponse>()
            .ProducesProblem(404);

        // Token-based endpoints (service-to-service)
        var tokenGroup = app.MapGroup("/api/v1/reservations/{token:guid}")
            .WithTags("Capacity Reservations")
            .RequireAuthorization();

        tokenGroup.MapGet("", GetReservation)
            .WithName("GetReservation")
            .WithDescription("Get reservation details by token")
            .Produces<ReservationResponse>()
            .ProducesProblem(404);

        tokenGroup.MapPost("/claim", ClaimReservation)
            .WithName("ClaimReservation")
            .WithDescription("Claim a reservation with a server ID")
            .Produces<ReservationResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        tokenGroup.MapDelete("", ReleaseReservation)
            .WithName("ReleaseReservation")
            .WithDescription("Release a reservation")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
    }

    private static async Task<IResult> CreateReservation(
        Guid organizationId,
        Guid nodeId,
        CreateReservationRequest request,
        ICapacityReservationService reservationService,
        INodeService nodeService,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        // Verify the node belongs to this organization
        var nodeResult = await nodeService.GetNodeAsync(nodeId, ct);
        if (!nodeResult.Success || nodeResult.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        // Get correlation ID from request context
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault();

        var result = await reservationService.ReserveAsync(
            nodeId,
            request.MemoryMb,
            request.DiskMb,
            request.CpuMillicores,
            request.RequestedBy,
            request.TtlMinutes,
            correlationId,
            ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "node_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "node_unavailable" => ProblemDetailsHelper.BadRequest(result.Error,
                    "Node is not available for reservations (offline, maintenance, or decommissioned)"),
                "capacity_data_missing" => ProblemDetailsHelper.BadRequest(result.Error,
                    "Node does not have capacity data configured"),
                "insufficient_memory" => ProblemDetailsHelper.BadRequest(result.Error,
                    "Insufficient memory available on node"),
                "insufficient_disk" => ProblemDetailsHelper.BadRequest(result.Error,
                    "Insufficient disk space available on node"),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "reservation_failed")
            };
        }

        return Results.Created(
            $"/api/v1/reservations/{result.Value!.ReservationToken}",
            result.Value);
    }

    private static async Task<IResult> ListReservations(
        Guid organizationId,
        Guid nodeId,
        ICapacityReservationService reservationService,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // Verify the node belongs to this organization
        var nodeResult = await nodeService.GetNodeAsync(nodeId, ct);
        if (!nodeResult.Success || nodeResult.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var reservations = await reservationService.GetActiveReservationsAsync(nodeId, ct);
        return Results.Ok(reservations);
    }

    private static async Task<IResult> GetAvailableCapacity(
        Guid organizationId,
        Guid nodeId,
        ICapacityReservationService reservationService,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // Verify the node belongs to this organization
        var nodeResult = await nodeService.GetNodeAsync(nodeId, ct);
        if (!nodeResult.Success || nodeResult.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await reservationService.GetAvailableCapacityAsync(nodeId, ct);
        if (!result.Success)
        {
            return result.Error switch
            {
                "node_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "capacity_data_missing" => ProblemDetailsHelper.BadRequest(result.Error,
                    "Node does not have capacity data configured"),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "capacity_check_failed")
            };
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetReservation(
        Guid token,
        ICapacityReservationService reservationService,
        CancellationToken ct = default)
    {
        var result = await reservationService.GetByTokenAsync(token, ct);
        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound("reservation_not_found");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ClaimReservation(
        Guid token,
        ClaimReservationRequest request,
        ICapacityReservationService reservationService,
        CancellationToken ct = default)
    {
        var result = await reservationService.ClaimAsync(token, request.ServerId, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "reservation_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "reservation_expired" => ProblemDetailsHelper.BadRequest(result.Error,
                    "The reservation has expired"),
                "reservation_claimed" => ProblemDetailsHelper.BadRequest(result.Error,
                    "The reservation has already been claimed"),
                "reservation_released" => ProblemDetailsHelper.BadRequest(result.Error,
                    "The reservation has been released"),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "claim_failed")
            };
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ReleaseReservation(
        Guid token,
        ICapacityReservationService reservationService,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        var result = await reservationService.ReleaseAsync(token, reason, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "reservation_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "reservation_already_released" => ProblemDetailsHelper.BadRequest(result.Error,
                    "The reservation has already been released or expired"),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "release_failed")
            };
        }

        return Results.NoContent();
    }
}
