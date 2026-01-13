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
                member.Email,
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
        var pattern = $"%{term}%";

        var members = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == organizationId && uo.LeftAt == null)
            .Where(uo =>
                EF.Functions.Like(uo.User.Email, pattern) ||
                (uo.User.DisplayName != null && EF.Functions.Like(uo.User.DisplayName, pattern)) ||
                (uo.User.UserName != null && EF.Functions.Like(uo.User.UserName, pattern)) ||
                _dbContext.LinkedAccounts.Any(la =>
                    la.UserId == uo.UserId &&
                    (EF.Functions.Like(la.Provider, pattern) ||
                     EF.Functions.Like(la.ProviderAccountId, pattern))))
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
                    member.Email,
                    member.DisplayName,
                    member.Role,
                    member.IsActive,
                    member.JoinedAt,
                    providersByUser.TryGetValue(member.UserId, out var providers) ? providers : Array.Empty<string>()))
            .ToArray();
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

        if (user.DeletedAt is null)
        {
            user.DeletedAt = now;
        }

        user.UpdatedAt = now;

        var memberships = await _dbContext.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.LeftAt == null)
            .ToListAsync(ct);

        foreach (var entry in memberships)
        {
            entry.LeftAt = now;
            entry.IsActive = false;
        }

        await _dbContext.SaveChangesAsync(ct);
        return ServiceResult.Ok(true);
    }
}
