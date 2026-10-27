using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Alias local models to avoid ambiguity with Contracts types
using LocalEnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using LocalEnrollNodeResponse = Dhadgar.Nodes.Models.EnrollNodeResponse;

namespace Dhadgar.Nodes.Endpoints;

public static class AgentEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/agents")
            .WithTags("Agents");

        // Enrollment endpoint - no auth required (uses token in body)
        group.MapPost("/enroll", Enroll)
            .WithName("EnrollAgent")
            .WithDescription("Enroll a new agent with the platform")
            .Produces<Dhadgar.Contracts.Nodes.EnrollNodeResponse>(201)
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
            .Produces<Dhadgar.Contracts.Nodes.RenewCertificateResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .RequireAuthorization("AgentPolicy");

        // CA certificate endpoint - no auth required (agents need this to trust the CA)
        group.MapGet("/ca-certificate", GetCaCertificate)
            .WithName("GetCaCertificate")
            .WithDescription("Get the CA certificate for agents to add to their trust store")
            .Produces<string>(200, "application/x-pem-file")
            .AllowAnonymous();
    }

    private static async Task<IResult> Enroll(
        LocalEnrollNodeRequest request,
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
                "invalid_public_key" => ProblemDetailsHelper.BadRequest(result.Error),
                "certificate_generation_failed" => ProblemDetailsHelper.BadRequest(result.Error),
                _ => ProblemDetailsHelper.BadRequest(result.Error ?? "enrollment_failed")
            };
        }

        // Map local model to Contracts DTO for API response
        var localResponse = result.Value!;
        var contractsResponse = new Dhadgar.Contracts.Nodes.EnrollNodeResponse(
            NodeId: localResponse.NodeId,
            OrganizationId: localResponse.OrganizationId,
            CertificateThumbprint: localResponse.CertificateThumbprint,
            Certificate: localResponse.Certificate,
            CaCertificate: localResponse.CaCertificate,
            NotBefore: new DateTimeOffset(localResponse.NotBefore, TimeSpan.Zero),
            NotAfter: new DateTimeOffset(localResponse.NotAfter, TimeSpan.Zero));

        return Results.Created($"/agents/{localResponse.NodeId}", contractsResponse);
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
        var nodeIdClaim = context.User.FindFirst("node_id");
        if (nodeIdClaim is null)
        {
            logger.LogWarning("Missing node_id claim in certificate for heartbeat request to node {NodeId}", nodeId);
            return Results.Forbid();
        }

        var nodeIdFromCert = nodeIdClaim.Value;
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
        Dhadgar.Contracts.Nodes.RenewCertificateRequest request,
        HttpContext context,
        NodesDbContext dbContext,
        ICertificateAuthorityService caService,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        IOptions<MtlsOptions> mtlsOptions,
        ILogger<Program> logger,
        CancellationToken ct = default)
    {
        // Verify the node ID from the certificate matches the URL
        var nodeIdClaim = context.User.FindFirst("node_id");
        if (nodeIdClaim is null)
        {
            logger.LogWarning("Missing node_id claim in certificate for certificate renewal request to node {NodeId}", nodeId);
            return ProblemDetailsHelper.Forbidden("forbidden");
        }

        var nodeIdFromCert = nodeIdClaim.Value;
        if (!Guid.TryParse(nodeIdFromCert, out var certNodeId))
        {
            logger.LogWarning("Invalid node_id claim format in certificate: {NodeIdClaim}", nodeIdFromCert);
            return ProblemDetailsHelper.Forbidden("forbidden");
        }

        if (certNodeId != nodeId)
        {
            logger.LogWarning(
                "Node ID mismatch: certificate has {CertNodeId}, request is for {RequestNodeId}",
                certNodeId, nodeId);
            return ProblemDetailsHelper.Forbidden("forbidden");
        }

        // Find the node
        var node = await dbContext.Nodes.FindAsync([nodeId], ct);
        if (node is null)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        // The current thumbprint comes only from the mTLS client certificate, never from the body.
        var presented = context.User.FindFirst("certificate_thumbprint")?.Value;
        if (string.IsNullOrEmpty(presented))
        {
            logger.LogWarning("Missing certificate_thumbprint claim for certificate renewal request to node {NodeId}", nodeId);
            return ProblemDetailsHelper.Unauthorized("certificate_required");
        }

        // The agent's new key: a bounded, non-blank SubjectPublicKeyInfo. Parsing happens in the CA.
        if (string.IsNullOrWhiteSpace(request.PublicKey) || request.PublicKey.Length > 4096)
        {
            return ProblemDetailsHelper.BadRequest("invalid_public_key");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Load the node's active rows and the rows still in renewal grace (D-S212), so an agent whose renewal
        // response was lost can renew again with the certificate it still holds.
        var nodeRows = await dbContext.AgentCertificates
            .Where(c => c.NodeId == nodeId && (!c.IsRevoked || c.GraceUntil != null))
            .ToListAsync(ct);

        if (nodeRows.Count == 0)
        {
            return ProblemDetailsHelper.NotFound("certificate_not_found",
                "No active certificate found for this node.");
        }

        // The presented certificate must be usable: the one grace-aware predicate (D-S212, wave1-ruling-1).
        var currentCert = nodeRows.FirstOrDefault(c =>
            string.Equals(c.Thumbprint, presented, StringComparison.OrdinalIgnoreCase)
            && AgentCertificateState.IsUsable(c, now));

        if (currentCert is null)
        {
            logger.LogWarning(
                "Certificate renewal failed for node {NodeId}: presented thumbprint {Provided} is not a usable certificate of this node.",
                nodeId, presented);
            return ProblemDetailsHelper.Unauthorized("thumbprint_mismatch",
                "The provided certificate thumbprint does not match a usable certificate.");
        }

        // Issue new certificate bound to the agent-held public key
        var certResult = await caService.IssueCertificateForPublicKeyAsync(nodeId, request.PublicKey, ct);
        if (!certResult.Success)
        {
            logger.LogError("Failed to renew certificate for node {NodeId}: {Error}", nodeId, certResult.Error);
            return certResult.Error == "invalid_public_key"
                ? ProblemDetailsHelper.BadRequest("invalid_public_key")
                : ProblemDetailsHelper.BadRequest("certificate_renewal_failed");
        }

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

        // Presented certificate: superseded now, usable for the grace window (D-S212). A certificate already in grace
        // keeps its GraceUntil, because grace is never extended.
        var grace = TimeSpan.FromMinutes(mtlsOptions.Value.RenewalGraceMinutes);
        var presentedWasInGrace = currentCert.IsRevoked;
        if (!presentedWasInGrace)
        {
            currentCert.IsRevoked = true;
            currentCert.RevokedAt = now;
            currentCert.RevocationReason = "Renewed - superseded by new certificate";
            currentCert.GraceUntil = grace > TimeSpan.Zero ? now + grace : null;
        }

        // Every other row of the node is hard-revoked: no grace.
        var hardRevokedCount = 0;
        foreach (var other in nodeRows.Where(r => r.Id != currentCert.Id))
        {
            AgentCertificateState.RevokeWithoutGrace(other, now, "Superseded by a later renewal");
            hardRevokedCount++;
        }

        // Publish events before SaveChangesAsync to use MassTransit outbox for atomic persistence
        await publishEndpoint.Publish(
            new AgentCertificateRenewed(nodeId, currentCert.Thumbprint, certResult.Thumbprint!),
            ct);

        await publishEndpoint.Publish(
            new AgentCertificateRevoked(nodeId, currentCert.Thumbprint, "Certificate renewed"),
            ct);

        await publishEndpoint.Publish(
            new AgentCertificateIssued(nodeId, certResult.Thumbprint!, certResult.NotAfter!.Value),
            ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another renewal, a revoke or a decommission changed the current certificate after it was read.
            // Nothing from this request was persisted, so the issued certificate is discarded.
            logger.LogWarning(
                "Certificate renewal for node {NodeId} lost a concurrency race on thumbprint {Thumbprint}.",
                nodeId, currentCert.Thumbprint);
            return ProblemDetailsHelper.Unauthorized("thumbprint_mismatch",
                "The certificate was renewed or revoked by a concurrent request.");
        }

        logger.LogInformation(
            "Certificate renewed for node {NodeId}. Old: {OldThumbprint}, New: {NewThumbprint}, " +
            "PresentedWasInGrace: {PresentedWasInGrace}, HardRevoked: {HardRevoked}",
            nodeId, currentCert.Thumbprint, certResult.Thumbprint, presentedWasInGrace, hardRevokedCount);

        // Map to Contracts DTO for API response (DateTime -> DateTimeOffset)
        var contractsResponse = new Dhadgar.Contracts.Nodes.RenewCertificateResponse(
            CertificateThumbprint: certResult.Thumbprint!,
            Certificate: certResult.CertificatePem!,
            CaCertificate: await caService.GetCaCertificatePemAsync(ct),
            NotBefore: new DateTimeOffset(certResult.NotBefore!.Value, TimeSpan.Zero),
            NotAfter: new DateTimeOffset(certResult.NotAfter!.Value, TimeSpan.Zero));

        return Results.Ok(contractsResponse);
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
