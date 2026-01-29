using System.Security.Cryptography;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

public sealed class EnrollmentTokenService : IEnrollmentTokenService
{
    private const int TokenLengthBytes = 32; // 256-bit token

    private readonly NodesDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnrollmentTokenService> _logger;
    private readonly NodesOptions _options;

    public EnrollmentTokenService(
        NodesDbContext dbContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IOptions<NodesOptions> options,
        ILogger<EnrollmentTokenService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(EnrollmentToken Token, string PlainTextToken)> CreateTokenAsync(
        Guid organizationId,
        string createdByUserId,
        string? label,
        TimeSpan? validity = null,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var defaultValidity = TimeSpan.FromMinutes(_options.DefaultEnrollmentTokenExpiryMinutes);
        var expiresAt = now.Add(validity ?? defaultValidity);

        // Generate cryptographically secure token
        var tokenBytes = RandomNumberGenerator.GetBytes(TokenLengthBytes);
        var plainTextToken = Convert.ToBase64String(tokenBytes);

        // Store only the hash
        var tokenHash = ComputeHash(plainTextToken);

        var token = new EnrollmentToken
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TokenHash = tokenHash,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            ExpiresAt = expiresAt,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            IsRevoked = false
        };

        _dbContext.EnrollmentTokens.Add(token);
        await _dbContext.SaveChangesAsync(ct);

        NodesMetrics.TokensCreated.Add(1);

        // Audit the token creation
        await _auditService.LogAsync(
            AuditActions.EnrollmentTokenCreated,
            ResourceTypes.EnrollmentToken,
            token.Id,
            AuditOutcome.Success,
            new { Label = label, ExpiresAt = expiresAt, CreatedByUserId = createdByUserId },
            resourceName: label ?? token.Id.ToString(),
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation(
            "Created enrollment token {TokenId} for organization {OrganizationId}, expires at {ExpiresAt}",
            token.Id, organizationId, expiresAt);

        return (token, plainTextToken);
    }

    public async Task<EnrollmentToken?> ValidateTokenAsync(
        string plainTextToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken))
        {
            return null;
        }

        string tokenHash;
        try
        {
            tokenHash = ComputeHash(plainTextToken);
        }
        catch (FormatException)
        {
            // Invalid base64 token
            _logger.LogWarning("Invalid enrollment token format attempted");
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var token = await _dbContext.EnrollmentTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                !t.IsRevoked &&
                t.UsedAt == null &&
                t.ExpiresAt > now,
                ct);

        if (token is null)
        {
            _logger.LogWarning("Invalid or expired enrollment token attempted");
        }

        return token;
    }

    public async Task MarkTokenUsedAsync(
        Guid tokenId,
        Guid nodeId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var token = await _dbContext.EnrollmentTokens.FindAsync([tokenId], ct);
        if (token is not null)
        {
            token.UsedAt = now;
            token.UsedByNodeId = nodeId;
            await _dbContext.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Enrollment token {TokenId} used by node {NodeId}",
            tokenId, nodeId);
    }

    public async Task RevokeTokenAsync(
        Guid tokenId,
        CancellationToken ct = default)
    {
        var token = await _dbContext.EnrollmentTokens.FindAsync([tokenId], ct);
        if (token is not null)
        {
            token.IsRevoked = true;
            await _dbContext.SaveChangesAsync(ct);
            NodesMetrics.TokensRevoked.Add(1);

            // Audit the token revocation
            await _auditService.LogAsync(
                AuditActions.EnrollmentTokenRevoked,
                ResourceTypes.EnrollmentToken,
                tokenId,
                AuditOutcome.Success,
                new { Label = token.Label },
                resourceName: token.Label ?? tokenId.ToString(),
                organizationId: token.OrganizationId,
                ct: ct);
        }

        _logger.LogInformation("Enrollment token {TokenId} revoked", tokenId);
    }

    public async Task<IReadOnlyList<EnrollmentTokenSummary>> GetActiveTokensAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return await _dbContext.EnrollmentTokens
            .AsNoTracking()
            .Where(t =>
                t.OrganizationId == organizationId &&
                !t.IsRevoked &&
                t.UsedAt == null &&
                t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EnrollmentTokenSummary(
                t.Id,
                t.Label,
                t.ExpiresAt,
                t.CreatedAt,
                t.CreatedByUserId))
            .ToListAsync(ct);
    }

    private static string ComputeHash(string plainTextToken)
    {
        var tokenBytes = Convert.FromBase64String(plainTextToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
