using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Endpoints;

public static class AgentEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/agents")
            .WithTags("Agents");

        // Enrollment endpoint - no auth required (uses token in body)
        group.MapPost("/enroll", Enroll)
            .WithName("EnrollAgent")
            .WithDescription("Enroll a new agent with the platform")
            .Produces<EnrollNodeResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .AllowAnonymous();

        // Heartbeat endpoint - requires agent authentication
        group.MapPost("/{nodeId:guid}/heartbeat", Heartbeat)
            .WithName("AgentHeartbeat")
            .WithDescription("Report agent health and status")
            .Produces<HeartbeatResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .RequireAuthorization("AgentPolicy");

        // Certificate renewal endpoint - requires agent authentication
        group.MapPost("/{nodeId:guid}/certificates/renew", RenewCertificate)
            .WithName("RenewAgentCertificate")
            .WithDescription("Renew an agent's mTLS certificate")
            .Produces<RenewCertificateResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .RequireAuthorization("AgentPolicy");

        // CA certificate endpoint - no auth required (agents need this to trust the CA)
        group.MapGet("/ca-certificate", GetCaCertificate)
            .WithName("GetCaCertificate")
            .WithDescription("Get the CA certificate for agents to add to their trust store")
            .Produces<CaCertificateResponse>(200, "text/plain")
            .AllowAnonymous();
    }

    private static async Task<IResult> Enroll(
        EnrollNodeRequest request,
        IEnrollmentService enrollmentService,
        CancellationToken ct = default)
    {
        var result = await enrollmentService.EnrollAsync(request, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "invalid_token" => ProblemDetailsHelper.Unauthorized(result.Error),
                "invalid_platform" => ProblemDetailsHelper.BadRequest(result.Error),
                "certificate_generation_failed" => ProblemDetailsHelper.BadRequest(result.Error),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "enrollment_failed")
            };
        }

        return Results.Created($"/api/v1/agents/{result.Value!.NodeId}", result.Value);
    }

    private static async Task<IResult> Heartbeat(
        Guid nodeId,
        HeartbeatRequest request,
        HttpContext context,
        IHeartbeatService heartbeatService,
        TimeProvider timeProvider,
        ILogger<Program> logger,
        CancellationToken ct = default)
    {
        // Verify the node ID from the certificate matches the URL
        var nodeIdFromCert = context.User.FindFirst("node_id")?.Value;

        if (nodeIdFromCert is not null)
        {
            if (!Guid.TryParse(nodeIdFromCert, out var certNodeId))
            {
                logger.LogWarning("Invalid node_id claim format in certificate: {NodeIdClaim}", nodeIdFromCert);
                return Results.Forbid();
            }

            if (certNodeId != nodeId)
            {
                logger.LogWarning(
                    "Node ID mismatch: certificate has {CertNodeId}, request is for {RequestNodeId}",
                    certNodeId, nodeId);
                return Results.Forbid();
            }
        }

        var result = await heartbeatService.ProcessHeartbeatAsync(nodeId, request, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "node_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "node_decommissioned" => ProblemDetailsHelper.BadRequest(result.Error),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "heartbeat_failed")
            };
        }

        var response = new HeartbeatResponse(
            Acknowledged: true,
            ServerTime: timeProvider.GetUtcNow().UtcDateTime);

        return Results.Ok(response);
    }

    private static async Task<IResult> RenewCertificate(
        Guid nodeId,
        RenewCertificateRequest request,
        HttpContext context,
        NodesDbContext dbContext,
        ICertificateAuthorityService caService,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        ILogger<Program> logger,
        CancellationToken ct = default)
    {
        // Verify the node ID from the certificate matches the URL
        var nodeIdFromCert = context.User.FindFirst("node_id")?.Value;
        if (nodeIdFromCert is not null)
        {
            if (!Guid.TryParse(nodeIdFromCert, out var certNodeId) || certNodeId != nodeId)
            {
                return Results.Forbid();
            }
        }

        // Find the node
        var node = await dbContext.Nodes.FindAsync([nodeId], ct);
        if (node is null)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        // Find the current certificate and validate the thumbprint
        var currentCert = await dbContext.AgentCertificates
            .Where(c => c.NodeId == nodeId && !c.IsRevoked)
            .OrderByDescending(c => c.IssuedAt)
            .FirstOrDefaultAsync(ct);

        if (currentCert is null)
        {
            return ProblemDetailsHelper.NotFound("certificate_not_found",
                "No active certificate found for this node.");
        }

        if (!string.Equals(currentCert.Thumbprint, request.CurrentThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Certificate renewal failed for node {NodeId}: thumbprint mismatch. " +
                "Expected: {Expected}, Provided: {Provided}",
                nodeId, currentCert.Thumbprint, request.CurrentThumbprint);
            return ProblemDetailsHelper.Unauthorized("thumbprint_mismatch",
                "The provided certificate thumbprint does not match the current certificate.");
        }

        // Issue new certificate
        var certResult = await caService.RenewCertificateAsync(nodeId, request.CurrentThumbprint, ct);
        if (!certResult.Success)
        {
            logger.LogError("Failed to renew certificate for node {NodeId}: {Error}", nodeId, certResult.Error);
            return ProblemDetailsHelper.BadRequest("certificate_renewal_failed", certResult.Error);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Create new certificate record
        var newCert = new Data.Entities.AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Thumbprint = certResult.Thumbprint!,
            SerialNumber = certResult.SerialNumber!,
            NotBefore = certResult.NotBefore!.Value,
            NotAfter = certResult.NotAfter!.Value,
            IsRevoked = false,
            IssuedAt = now
        };

        dbContext.AgentCertificates.Add(newCert);

        // Revoke old certificate
        currentCert.IsRevoked = true;
        currentCert.RevokedAt = now;
        currentCert.RevocationReason = "Renewed - superseded by new certificate";

        await dbContext.SaveChangesAsync(ct);

        // Publish events
        await publishEndpoint.Publish(
            new AgentCertificateRenewed(nodeId, currentCert.Thumbprint, certResult.Thumbprint!),
            ct);

        await publishEndpoint.Publish(
            new AgentCertificateRevoked(nodeId, currentCert.Thumbprint, "Certificate renewed"),
            ct);

        await publishEndpoint.Publish(
            new AgentCertificateIssued(nodeId, certResult.Thumbprint!, certResult.NotAfter!.Value),
            ct);

        logger.LogInformation(
            "Certificate renewed for node {NodeId}. Old: {OldThumbprint}, New: {NewThumbprint}",
            nodeId, currentCert.Thumbprint, certResult.Thumbprint);

        return Results.Ok(new RenewCertificateResponse(
            certResult.Thumbprint!,
            certResult.CertificatePem!,
            certResult.Pkcs12Base64!,
            certResult.Pkcs12Password!,
            certResult.NotBefore!.Value,
            certResult.NotAfter!.Value));
    }

    private static async Task<IResult> GetCaCertificate(
        ICertificateAuthorityService caService,
        CancellationToken ct = default)
    {
        try
        {
            var caPem = await caService.GetCaCertificatePemAsync(ct);
            return Results.Text(caPem, "application/x-pem-file");
        }
        catch (InvalidOperationException ex)
        {
            return ProblemDetailsHelper.BadRequest("ca_not_initialized", ex.Message);
        }
    }
}

/// <summary>
/// Response containing the CA certificate.
/// </summary>
public sealed record CaCertificateResponse(string Certificate);
