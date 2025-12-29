using System.Text;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

public sealed record OrganizationSummary(
    Guid Id,
    string Name,
    string Slug,
    string Role,
    bool IsActive,
    Guid OwnerId);

public sealed record OrganizationDetail(
    Guid Id,
    string Name,
    string Slug,
    Guid OwnerId,
    OrganizationSettings Settings,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt);

public sealed record OrganizationCreateRequest(
    string Name,
    string? Slug);

public sealed record OrganizationUpdateRequest(
    string? Name,
    string? Slug,
    OrganizationSettings? Settings);

public sealed class OrganizationService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OrganizationService(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var memberships = await _dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.UserId == userId && uo.LeftAt == null)
            .Join(_dbContext.Organizations.AsNoTracking(),
                membership => membership.OrganizationId,
                org => org.Id,
                (membership, org) => new OrganizationSummary(
                    org.Id,
                    org.Name,
                    org.Slug,
                    membership.Role,
                    membership.IsActive,
                    org.OwnerId))
            .ToListAsync(ct);

        return memberships;
    }

    public async Task<ServiceResult<OrganizationDetail>> GetAsync(Guid organizationId, CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (org is null)
        {
            return ServiceResult<OrganizationDetail>.Fail("org_not_found");
        }

        var detail = new OrganizationDetail(
            org.Id,
            org.Name,
            org.Slug,
            org.OwnerId,
            org.Settings,
            org.CreatedAt,
            org.UpdatedAt,
            org.DeletedAt);

        return ServiceResult<OrganizationDetail>.Ok(detail);
    }

    public async Task<ServiceResult<Organization>> CreateAsync(
        Guid userId,
        OrganizationCreateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<Organization>.Fail("name_required");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return ServiceResult<Organization>.Fail("user_not_found");
        }

        var slugSeed = string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug!;
        var normalizedSlug = NormalizeSlug(slugSeed);
        normalizedSlug = await EnsureUniqueSlugAsync(normalizedSlug, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var org = new Organization
        {
            Name = request.Name.Trim(),
            Slug = normalizedSlug,
            OwnerId = userId,
            CreatedAt = now
        };

        var membership = new UserOrganization
        {
            UserId = userId,
            Organization = org,
            Role = "owner",
            IsActive = true,
            JoinedAt = now,
            InvitationAcceptedAt = now
        };

        _dbContext.Organizations.Add(org);
        _dbContext.UserOrganizations.Add(membership);

        if (!user.PreferredOrganizationId.HasValue)
        {
            user.PreferredOrganizationId = org.Id;
        }

        await _dbContext.SaveChangesAsync(ct);

        return ServiceResult<Organization>.Ok(org);
    }

    public async Task<ServiceResult<Organization>> UpdateAsync(
        Guid organizationId,
        OrganizationUpdateRequest request,
        CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null)
        {
            return ServiceResult<Organization>.Fail("org_not_found");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            org.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var normalizedSlug = NormalizeSlug(request.Slug);
            if (!string.Equals(org.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
            {
                org.Slug = await EnsureUniqueSlugAsync(normalizedSlug, ct);
            }
        }

        if (request.Settings is not null)
        {
            org.Settings = request.Settings;
        }

        org.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _dbContext.SaveChangesAsync(ct);
        return ServiceResult<Organization>.Ok(org);
    }

    public async Task<ServiceResult<bool>> SoftDeleteAsync(Guid organizationId, CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null)
        {
            return ServiceResult<bool>.Fail("org_not_found");
        }

        if (org.DeletedAt is not null)
        {
            return ServiceResult<bool>.Ok(true);
        }

        org.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;
        org.UpdatedAt = org.DeletedAt;

        await _dbContext.SaveChangesAsync(ct);
        return ServiceResult<bool>.Ok(true);
    }

    private async Task<string> EnsureUniqueSlugAsync(string slug, CancellationToken ct)
    {
        var normalized = slug;
        var index = 1;

        while (await _dbContext.Organizations.AnyAsync(o => o.Slug == normalized, ct))
        {
            index++;
            normalized = $"{slug}-{index}";
        }

        return normalized;
    }

    private static string NormalizeSlug(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "org";
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousDash = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "org" : slug;
    }
}
