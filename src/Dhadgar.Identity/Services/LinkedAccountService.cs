using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

public sealed record ExternalAccountInfo(
    string Provider,
    string ProviderAccountId,
    LinkedAccountMetadata Metadata);

public sealed record LinkedAccountResult(
    bool Success,
    string? Error,
    LinkedAccount? Account)
{
    public static LinkedAccountResult Failure(string error)
        => new(false, error, null);

    public static LinkedAccountResult SuccessResult(LinkedAccount account)
        => new(true, null, account);
}

public interface ILinkedAccountService
{
    Task<LinkedAccountResult> LinkAsync(Guid userId, ExternalAccountInfo info, CancellationToken ct = default);
}

public sealed class LinkedAccountService : ILinkedAccountService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LinkedAccountService> _logger;

    public LinkedAccountService(
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<LinkedAccountService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LinkedAccountResult> LinkAsync(Guid userId, ExternalAccountInfo info, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.Provider) || string.IsNullOrWhiteSpace(info.ProviderAccountId))
        {
            return LinkedAccountResult.Failure("invalid_provider_data");
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return LinkedAccountResult.Failure("user_not_found");
        }

        var provider = info.Provider.Trim().ToLowerInvariant();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var existing = await _dbContext.LinkedAccounts
            .SingleOrDefaultAsync(la => la.Provider == provider && la.ProviderAccountId == info.ProviderAccountId, ct);

        if (existing is not null && existing.UserId != userId)
        {
            return LinkedAccountResult.Failure("account_already_linked");
        }

        if (existing is null)
        {
            existing = new LinkedAccount
            {
                UserId = userId,
                Provider = provider,
                ProviderAccountId = info.ProviderAccountId,
                ProviderMetadata = info.Metadata,
                LinkedAt = now,
                LastUsedAt = now
            };

            _dbContext.LinkedAccounts.Add(existing);
        }
        else
        {
            existing.ProviderMetadata = info.Metadata;
            existing.LastUsedAt = now;
        }

        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Linked provider {Provider} account {AccountId} to user {UserId}",
            provider,
            info.ProviderAccountId,
            userId);

        return LinkedAccountResult.SuccessResult(existing);
    }
}
