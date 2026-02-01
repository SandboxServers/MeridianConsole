using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

public sealed record LinkedAccountSummary(
    Guid Id,
    string Provider,
    string ProviderAccountId,
    LinkedAccountMetadata? Metadata,
    DateTime LinkedAt,
    DateTime? LastUsedAt);

public sealed record UserSummary(
    Guid Id,
    string Email,
    string? DisplayName,
    string Role,
    bool IsActive,
    DateTime JoinedAt,
    IReadOnlyCollection<string> LinkedProviders);

public sealed record UserDetail(
    Guid Id,
    string Email,
    string? DisplayName,
    bool EmailVerified,
    bool HasPasskeysRegistered,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastAuthenticatedAt,
    Guid OrganizationId,
    string Role,
    bool IsActive,
    DateTime JoinedAt,
    IReadOnlyCollection<LinkedAccountSummary> LinkedAccounts);

public sealed record UserCreateRequest(
    string Email,
    string? DisplayName);

public sealed record UserUpdateRequest(
    string? Email,
    string? DisplayName);

public sealed class UserService
{
    private const int MaxEmailLength = 320;
    private const int MaxDisplayNameLength = 200;

    private readonly IdentityDbContext _dbContext;
    private readonly ILookupNormalizer _lookupNormalizer;
    private readonly TimeProvider _timeProvider;

    public UserService(
        IdentityDbContext dbContext,
        ILookupNormalizer lookupNormalizer,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _lookupNormalizer = lookupNormalizer;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<UserSummary>> ListForOrganizationAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        var members = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == organizationId && uo.LeftAt == null)
            .Select(uo => new
            {
                uo.UserId,
                uo.Role,
                uo.IsActive,
                uo.JoinedAt,
                uo.User.Email,
                uo.User.DisplayName
            })
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return Array.Empty<UserSummary>();
        }

        var userIds = members.Select(m => m.UserId).Distinct().ToArray();
        var providerLookup = await _dbContext.LinkedAccounts
            .AsNoTracking()
            .Where(la => userIds.Contains(la.UserId))
            .Select(la => new { la.UserId, la.Provider })
            .ToListAsync(ct);

        var providersByUser = providerLookup
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group
                    .Select(item => item.Provider)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return members.Select(member =>
            new UserSummary(
                member.UserId,
                member.Email ?? "",
                member.DisplayName,
                member.Role,
                member.IsActive,
                member.JoinedAt,
                providersByUser.TryGetValue(member.UserId, out var providers) ? providers : Array.Empty<string>()))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<UserSummary>> SearchAsync(
        Guid organizationId,
        string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<UserSummary>();
        }

        var term = query.Trim();
        var pattern = $"%{EscapeLikePattern(term)}%";
        var escape = "\\";
        var linkedUserIds = _dbContext.LinkedAccounts
            .Where(la =>
                EF.Functions.Like(la.Provider, pattern, escape) ||
                EF.Functions.Like(la.ProviderAccountId, pattern, escape))
            .Select(la => la.UserId);

        var members = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == organizationId && uo.LeftAt == null)
            .Where(uo =>
                EF.Functions.Like(uo.User.Email, pattern, escape) ||
                (uo.User.DisplayName != null && EF.Functions.Like(uo.User.DisplayName, pattern, escape)) ||
                (uo.User.UserName != null && EF.Functions.Like(uo.User.UserName, pattern, escape)) ||
                linkedUserIds.Contains(uo.UserId))
            .Select(uo => new
            {
                uo.UserId,
                uo.Role,
                uo.IsActive,
                uo.JoinedAt,
                uo.User.Email,
                uo.User.DisplayName
            })
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return Array.Empty<UserSummary>();
        }

        var userIds = members.Select(m => m.UserId).Distinct().ToArray();
        var providerLookup = await _dbContext.LinkedAccounts
            .AsNoTracking()
            .Where(la => userIds.Contains(la.UserId))
            .Select(la => new { la.UserId, la.Provider })
            .ToListAsync(ct);

        var providersByUser = providerLookup
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group
                    .Select(item => item.Provider)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return members.Select(member =>
                new UserSummary(
                    member.UserId,
                    member.Email ?? "",
                    member.DisplayName,
                    member.Role,
                    member.IsActive,
                    member.JoinedAt,
                    providersByUser.TryGetValue(member.UserId, out var providers) ? providers : Array.Empty<string>()))
            .ToArray();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    public async Task<ServiceResult<UserDetail>> GetAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<UserDetail>("user_not_found");
        }

        var linkedAccounts = await _dbContext.LinkedAccounts
            .AsNoTracking()
            .Where(la => la.UserId == userId)
            .OrderBy(la => la.Provider)
            .Select(la => new LinkedAccountSummary(
                la.Id,
                la.Provider,
                la.ProviderAccountId,
                la.ProviderMetadata,
                la.LinkedAt,
                la.LastUsedAt))
            .ToListAsync(ct);

        var detail = new UserDetail(
            membership.User.Id,
            membership.User.Email ?? string.Empty,
            membership.User.DisplayName,
            membership.User.EmailVerified,
            membership.User.HasPasskeysRegistered,
            membership.User.CreatedAt,
            membership.User.UpdatedAt,
            membership.User.LastAuthenticatedAt,
            membership.OrganizationId,
            membership.Role,
            membership.IsActive,
            membership.JoinedAt,
            linkedAccounts);

        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<UserDetail>> CreateAsync(
        Guid organizationId,
        Guid actorUserId,
        UserCreateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return ServiceResult.Fail<UserDetail>("email_required");
        }

        if (request.Email.Trim().Length > MaxEmailLength)
        {
            return ServiceResult.Fail<UserDetail>("email_too_long");
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName) &&
            request.DisplayName.Trim().Length > MaxDisplayNameLength)
        {
            return ServiceResult.Fail<UserDetail>("display_name_too_long");
        }

        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null, ct);

        if (org is null)
        {
            return ServiceResult.Fail<UserDetail>("org_not_found");
        }

        var activeMembersCount = await _dbContext.UserOrganizations
            .AsNoTracking()
            .CountAsync(uo => uo.OrganizationId == organizationId && uo.LeftAt == null && uo.IsActive, ct);

        if (activeMembersCount >= org.Settings.MaxMembers)
        {
            return ServiceResult.Fail<UserDetail>("member_limit_reached");
        }

        var normalizedEmail = _lookupNormalizer.NormalizeEmail(request.Email.Trim());

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (user is null)
        {
            var userId = Guid.NewGuid();

            user = new User
            {
                Id = userId,
                ExternalAuthId = ExternalAuthIdHelper.CreateManualId(userId),
                Email = request.Email.Trim(),
                NormalizedEmail = normalizedEmail,
                UserName = request.Email.Trim(),
                NormalizedUserName = _lookupNormalizer.NormalizeName(request.Email.Trim()),
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? null
                    : request.DisplayName.Trim(),
                EmailConfirmed = false,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.Users.Add(user);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                user.DisplayName = request.DisplayName.Trim();
                user.UpdatedAt = now;
            }
        }

        var existingMembership = await _dbContext.UserOrganizations
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == user.Id &&
                uo.LeftAt == null,
                ct);

        if (existingMembership is not null)
        {
            return ServiceResult.Fail<UserDetail>("already_member");
        }

        var membership = new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            Role = "viewer",
            IsActive = true,
            JoinedAt = now,
            InvitationAcceptedAt = now,
            InvitedByUserId = actorUserId
        };

        _dbContext.UserOrganizations.Add(membership);
        await _dbContext.SaveChangesAsync(ct);

        var detail = new UserDetail(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.EmailVerified,
            user.HasPasskeysRegistered,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastAuthenticatedAt,
            organizationId,
            membership.Role,
            membership.IsActive,
            membership.JoinedAt,
            Array.Empty<LinkedAccountSummary>());

        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<UserDetail>> UpdateAsync(
        Guid organizationId,
        Guid userId,
        UserUpdateRequest request,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<UserDetail>("user_not_found");
        }

        var user = membership.User;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = _lookupNormalizer.NormalizeEmail(request.Email.Trim());
            if (request.Email.Trim().Length > MaxEmailLength)
            {
                return ServiceResult.Fail<UserDetail>("email_too_long");
            }

            if (!string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            {
                var emailExists = await _dbContext.Users
                    .AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != userId, ct);

                if (emailExists)
                {
                    return ServiceResult.Fail<UserDetail>("email_in_use");
                }

                user.Email = request.Email.Trim();
                user.NormalizedEmail = normalizedEmail;
                user.UserName = request.Email.Trim();
                user.NormalizedUserName = _lookupNormalizer.NormalizeName(request.Email.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            if (request.DisplayName.Trim().Length > MaxDisplayNameLength)
            {
                return ServiceResult.Fail<UserDetail>("display_name_too_long");
            }

            user.DisplayName = request.DisplayName.Trim();
        }

        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        var linkedAccounts = await _dbContext.LinkedAccounts
            .AsNoTracking()
            .Where(la => la.UserId == userId)
            .OrderBy(la => la.Provider)
            .Select(la => new LinkedAccountSummary(
                la.Id,
                la.Provider,
                la.ProviderAccountId,
                la.ProviderMetadata,
                la.LinkedAt,
                la.LastUsedAt))
            .ToListAsync(ct);

        var detail = new UserDetail(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.EmailVerified,
            user.HasPasskeysRegistered,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastAuthenticatedAt,
            organizationId,
            membership.Role,
            membership.IsActive,
            membership.JoinedAt,
            linkedAccounts);

        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<bool>> SoftDeleteAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var membership = await _dbContext.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (membership is null)
        {
            return ServiceResult.Fail<bool>("user_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = membership.User;

        membership.LeftAt = now;
        membership.IsActive = false;

        user.UpdatedAt = now;

        // Revoke all refresh tokens for this organization membership
        // This prevents the user from using existing tokens after removal
        await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.OrganizationId == organizationId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), ct);

        // Check for other active memberships, excluding the one we just deactivated
        // Need to exclude by organizationId since the database hasn't been updated yet
        var otherMembershipsExist = await _dbContext.UserOrganizations
            .AnyAsync(uo => uo.UserId == userId && uo.OrganizationId != organizationId && uo.LeftAt == null, ct);

        if (!otherMembershipsExist)
        {
            if (user.DeletedAt is null)
            {
                user.DeletedAt = now;

                // User is being fully soft-deleted, revoke ALL their remaining tokens
                await _dbContext.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), ct);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return ServiceResult.Ok(true);
    }

    /// <summary>
    /// User requests deletion of their own account.
    /// Sets a 30-day grace period before permanent deletion and immediately revokes all tokens.
    /// </summary>
    public async Task<ServiceResult<DateTime>> RequestDeletionAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);

        if (user is null)
        {
            return ServiceResult.Fail<DateTime>("user_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var scheduledDeletion = now.AddDays(30);

        // Mark user as pending deletion
        user.DeletedAt = scheduledDeletion;
        user.UpdatedAt = now;

        // Immediately revoke all refresh tokens
        await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), ct);

        // Mark all organization memberships as inactive
        await _dbContext.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.LeftAt == null)
            .ExecuteUpdateAsync(uo => uo
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.LeftAt, now), ct);

        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult.Ok(scheduledDeletion);
    }

    /// <summary>
    /// Cancels a pending account deletion request.
    /// Only works if the deletion hasn't been finalized yet.
    /// </summary>
    public async Task<ServiceResult<bool>> CancelDeletionAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Need to bypass the global query filter (DeletedAt == null) to find users pending deletion
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt != null && u.DeletedAt > now, ct);

        if (user is null)
        {
            return ServiceResult.Fail<bool>("user_not_found_or_already_deleted");
        }

        // Clear the scheduled deletion
        user.DeletedAt = null;
        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult.Ok(true);
    }
}
