using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

public sealed class EnrollmentService : IEnrollmentService
{
    private readonly NodesDbContext _dbContext;
    private readonly IEnrollmentTokenService _tokenService;
    private readonly ICertificateAuthorityService _caService;
    private readonly IAuditService _auditService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnrollmentService> _logger;
    private readonly NodesOptions _options;

    public EnrollmentService(
        NodesDbContext dbContext,
        IEnrollmentTokenService tokenService,
        ICertificateAuthorityService caService,
        IAuditService auditService,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        IOptions<NodesOptions> options,
        ILogger<EnrollmentService> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _caService = caService;
        _auditService = auditService;
        _publishEndpoint = publishEndpoint;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<EnrollNodeResponse>> EnrollAsync(
        EnrollNodeRequest request,
        CancellationToken ct = default)
    {
        // Validate platform first (before calling ToLowerInvariant)
        if (string.IsNullOrWhiteSpace(request.Platform) || !IsValidPlatform(request.Platform))
        {
            var platformForMetrics = string.IsNullOrWhiteSpace(request.Platform) ? "unknown" : request.Platform.ToLowerInvariant();
            NodesMetrics.RecordEnrollmentAttempt(platformForMetrics);
            NodesMetrics.RecordEnrollmentFailure(platformForMetrics, "invalid_platform");

            await _auditService.LogAsync(
                AuditActions.EnrollmentFailed,
                ResourceTypes.Node,
                null,
                AuditOutcome.Failure,
                new { Platform = request.Platform, Hostname = request.Hardware?.Hostname },
                failureReason: "invalid_platform",
                ct: ct);

            return ServiceResult.Fail<EnrollNodeResponse>("invalid_platform");
        }

        // Validate hardware
        if (request.Hardware is null || string.IsNullOrWhiteSpace(request.Hardware.Hostname))
        {
            var platformForMetrics = request.Platform.ToLowerInvariant();
            NodesMetrics.RecordEnrollmentAttempt(platformForMetrics);
            NodesMetrics.RecordEnrollmentFailure(platformForMetrics, "invalid_hardware");

            await _auditService.LogAsync(
                AuditActions.EnrollmentFailed,
                ResourceTypes.Node,
                null,
                AuditOutcome.Failure,
                new { Platform = request.Platform, Hostname = (string?)null },
                failureReason: "invalid_hardware",
                ct: ct);

            return ServiceResult.Fail<EnrollNodeResponse>("invalid_hardware");
        }

        var platform = request.Platform.ToLowerInvariant();
        NodesMetrics.RecordEnrollmentAttempt(platform);

        // Validate token
        var token = await _tokenService.ValidateTokenAsync(request.Token, ct);
        if (token is null)
        {
            _logger.LogWarning("Enrollment failed: invalid or expired token");
            NodesMetrics.RecordEnrollmentFailure(platform, "invalid_token");

            // Audit the failed enrollment attempt
            await _auditService.LogAsync(
                AuditActions.EnrollmentFailed,
                ResourceTypes.Node,
                null,
                AuditOutcome.Failure,
                new { Platform = platform, Hostname = request.Hardware.Hostname },
                failureReason: "invalid_token",
                ct: ct);

            return ServiceResult.Fail<EnrollNodeResponse>("invalid_token");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Generate unique node name
        var nodeName = await GenerateUniqueNodeNameAsync(token.OrganizationId, request.Hardware.Hostname, ct);

        // Create node
        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = token.OrganizationId,
            Name = nodeName,
            DisplayName = request.Hardware.Hostname,
            Status = NodeStatus.Enrolling,
            Platform = request.Platform.ToLowerInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        };

        // Create hardware inventory
        var hardware = new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Hostname = request.Hardware.Hostname,
            OsVersion = request.Hardware.OsVersion,
            CpuCores = request.Hardware.CpuCores,
            MemoryBytes = request.Hardware.MemoryBytes,
            DiskBytes = request.Hardware.DiskBytes,
            NetworkInterfaces = request.Hardware.NetworkInterfaces is not null
                ? JsonSerializer.Serialize(request.Hardware.NetworkInterfaces)
                : null,
            CollectedAt = now
        };

        // Create initial capacity record
        var capacity = new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = CalculateMaxGameServers(request.Hardware),
            CurrentGameServers = 0,
            AvailableMemoryBytes = request.Hardware.MemoryBytes,
            AvailableDiskBytes = request.Hardware.DiskBytes,
            UpdatedAt = now
        };

        // Generate real X.509 certificate using the CA service
        var certResult = await _caService.IssueCertificateAsync(node.Id, ct);
        if (!certResult.Success)
        {
            _logger.LogError("Failed to issue certificate for node {NodeId}: {Error}", node.Id, certResult.Error);
            NodesMetrics.RecordEnrollmentFailure(platform, "certificate_generation_failed");

            // Audit the failed enrollment attempt
            await _auditService.LogAsync(
                AuditActions.EnrollmentFailed,
                ResourceTypes.Node,
                node.Id,
                AuditOutcome.Failure,
                new { Platform = platform, Hostname = request.Hardware.Hostname },
                resourceName: nodeName,
                organizationId: token.OrganizationId,
                failureReason: "certificate_generation_failed",
                ct: ct);

            return ServiceResult.Fail<EnrollNodeResponse>("certificate_generation_failed");
        }

        var certificate = new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Thumbprint = certResult.Thumbprint!,
            SerialNumber = certResult.SerialNumber!,
            NotBefore = certResult.NotBefore!.Value,
            NotAfter = certResult.NotAfter!.Value,
            IsRevoked = false,
            IssuedAt = now
        };

        _dbContext.Nodes.Add(node);
        _dbContext.HardwareInventories.Add(hardware);
        _dbContext.NodeCapacities.Add(capacity);
        _dbContext.AgentCertificates.Add(certificate);

        // Mark token as used (entity is tracked, will be saved with the same SaveChangesAsync below)
        _tokenService.MarkTokenUsed(token, node.Id);

        // Publish events BEFORE SaveChangesAsync for transactional outbox pattern.
        // MassTransit stores messages in the outbox table, which are committed
        // atomically with entity changes when SaveChangesAsync is called.
        await _publishEndpoint.Publish(
            new NodeEnrolled(node.Id, token.OrganizationId, node.Platform, now),
            ct);

        await _publishEndpoint.Publish(
            new AgentCertificateIssued(node.Id, certResult.Thumbprint!, certResult.NotAfter!.Value),
            ct);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Node {NodeId} enrolled for organization {OrganizationId}, platform: {Platform}, cert thumbprint: {Thumbprint}",
            node.Id, token.OrganizationId, node.Platform, certResult.Thumbprint);

        NodesMetrics.RecordEnrollmentSuccess(node.Platform);

        // Audit successful enrollment
        await _auditService.LogAsync(
            AuditActions.EnrollmentCompleted,
            ResourceTypes.Node,
            node.Id,
            AuditOutcome.Success,
            new
            {
                Platform = node.Platform,
                Hostname = request.Hardware.Hostname,
                TokenId = token.Id,
                CertificateThumbprint = certResult.Thumbprint
            },
            resourceName: node.Name,
            organizationId: token.OrganizationId,
            ct: ct);

        // Audit certificate issuance
        await _auditService.LogAsync(
            AuditActions.CertificateIssued,
            ResourceTypes.Certificate,
            certificate.Id,
            AuditOutcome.Success,
            new
            {
                NodeId = node.Id,
                Thumbprint = certResult.Thumbprint,
                NotAfter = certResult.NotAfter
            },
            resourceName: certResult.Thumbprint,
            organizationId: token.OrganizationId,
            ct: ct);

        var response = new EnrollNodeResponse(
            node.Id,
            certResult.Thumbprint!,
            certResult.CertificatePem!,
            certResult.Pkcs12Base64,
            certResult.Pkcs12Password,
            certResult.NotBefore,
            certResult.NotAfter);

        return ServiceResult.Ok(response);
    }

    private static bool IsValidPlatform(string platform)
    {
        return string.Equals(platform, "linux", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GenerateUniqueNodeNameAsync(
        Guid organizationId,
        string hostname,
        CancellationToken ct)
    {
        var baseName = SanitizeNodeName(hostname);

        // Check if base name is available
        var nameExists = await _dbContext.Nodes
            .AnyAsync(n => n.OrganizationId == organizationId && n.Name == baseName, ct);

        if (!nameExists)
        {
            return baseName;
        }

        // Find next available number
        var existingNames = await _dbContext.Nodes
            .Where(n => n.OrganizationId == organizationId && n.Name.StartsWith(baseName))
            .Select(n => n.Name)
            .ToListAsync(ct);

        var maxNumber = 0;
        foreach (var name in existingNames)
        {
            if (name == baseName)
            {
                maxNumber = Math.Max(maxNumber, 1);
            }
            else if (name.StartsWith(baseName + "-") &&
                     int.TryParse(name[(baseName.Length + 1)..], out var num))
            {
                maxNumber = Math.Max(maxNumber, num);
            }
        }

        return $"{baseName}-{maxNumber + 1}";
    }

    private static string SanitizeNodeName(string hostname)
    {
        // Remove invalid characters, limit length
        var sanitized = new string(hostname
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());

        // If sanitized is empty (hostname had only disallowed chars), generate deterministic fallback
        if (string.IsNullOrEmpty(sanitized))
        {
            // Use SHA256 hash of original hostname for deterministic, unique fallback
            var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hostname));
            var hashSlug = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
            sanitized = $"node-{hashSlug}";
        }

        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }

    private static int CalculateMaxGameServers(HardwareInventoryDto hardware)
    {
        // Simple heuristic: 1 server per 4GB RAM and 2 cores, minimum 1
        var byMemory = hardware.MemoryBytes / (4L * 1024 * 1024 * 1024);
        var byCores = hardware.CpuCores / 2;
        return Math.Max(1, (int)Math.Min(byMemory, byCores));
    }

}
