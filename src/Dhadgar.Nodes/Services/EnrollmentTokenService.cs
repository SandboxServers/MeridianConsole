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
        var maxValidity = TimeSpan.FromMinutes(_options.MaxEnrollmentTokenExpiryMinutes);
        var requestedValidity = validity ?? defaultValidity;
        var effectiveValidity = requestedValidity > maxValidity ? maxValidity : requestedValidity;

        if (requestedValidity > maxValidity)
        {
            _logger.LogWarning(
                "Requested enrollment token validity {RequestedMinutes} minutes exceeds maximum {MaxMinutes} minutes; clamped to maximum",
                requestedValidity.TotalMinutes, maxValidity.TotalMinutes);
        }

        var expiresAt = now.Add(effectiveValidity);

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

    public void MarkTokenUsed(EnrollmentToken token, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(token);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Validate token state before marking as used
        if (token.UsedAt is not null)
        {
            _logger.LogWarning(
                "Attempted to mark already-used token {TokenId} as used by node {NodeId}; token was previously used at {UsedAt}",
                token.Id, nodeId, token.UsedAt);
            return;
        }

        if (token.IsRevoked)
        {
            _logger.LogWarning(
                "Attempted to mark revoked token {TokenId} as used by node {NodeId}",
                token.Id, nodeId);
            return;
        }

        if (token.ExpiresAt <= now)
        {
            _logger.LogWarning(
                "Attempted to mark expired token {TokenId} as used by node {NodeId}; token expired at {ExpiresAt}",
                token.Id, nodeId, token.ExpiresAt);
            return;
        }

        token.UsedAt = now;
        token.UsedByNodeId = nodeId;

        _logger.LogInformation(
            "Enrollment token {TokenId} marked as used by node {NodeId}",
            token.Id, nodeId);
    }

    public async Task<bool> RevokeTokenAsync(
        Guid organizationId,
        Guid tokenId,
        CancellationToken ct = default)
    {
        var token = await _dbContext.EnrollmentTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.OrganizationId == organizationId, ct);

        if (token is null)
        {
            _logger.LogWarning(
                "Token {TokenId} not found or does not belong to organization {OrganizationId}",
                tokenId, organizationId);
            return false;
        }

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

        _logger.LogInformation(
            "Enrollment token {TokenId} revoked for organization {OrganizationId}",
            tokenId, organizationId);
        return true;
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
