using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using Dhadgar.Shared.Data;
using Dhadgar.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Servers.Services;

public sealed class ServerTemplateService : IServerTemplateService
{
    private readonly ServersDbContext _db;
    private readonly ILogger<ServerTemplateService> _logger;
    private readonly TimeProvider _timeProvider;

    public ServerTemplateService(
        ServersDbContext db,
        ILogger<ServerTemplateService> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResponse<ServerTemplateListItem>> GetTemplatesAsync(
        Guid? organizationId,
        bool includePublic,
        string? gameType,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Clamp pagination to valid ranges
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.ServerTemplates.Where(t => !t.IsArchived && t.DeletedAt == null);

        if (organizationId.HasValue)
        {
            if (includePublic)
            {
                query = query.Where(t => t.OrganizationId == organizationId.Value || t.IsPublic);
            }
            else
            {
                query = query.Where(t => t.OrganizationId == organizationId.Value);
            }
        }
        else if (includePublic)
        {
            query = query.Where(t => t.IsPublic);
        }
        else
        {
            // No org and not including public — return nothing to prevent data leak
            query = query.Where(t => false);
        }

        if (!string.IsNullOrEmpty(gameType))
        {
            query = query.Where(t => t.GameType == gameType);
        }

        var totalCount = await query.CountAsync(ct);

        var templates = await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ServerTemplateListItem(
                t.Id,
                t.Name,
                t.Description,
                t.GameType,
                t.IsPublic,
                t.IsArchived,
                t.UsageCount,
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<ServerTemplateListItem>
        {
            Items = templates,
            Page = page,
            PageSize = pageSize,
            Total = totalCount
        };
    }

    public async Task<Result<ServerTemplateDetail>> GetTemplateAsync(
        Guid templateId,
        Guid? organizationId,
        CancellationToken ct = default)
    {
        var template = await _db.ServerTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && !t.IsArchived && t.DeletedAt == null, ct);

        if (template is null)
        {
            return Result<ServerTemplateDetail>.Failure("template_not_found");
        }

        // Check access: non-public templates require a matching org.
        // Guard against null == null: if the caller has no org, deny access to non-public templates.
        if (!template.IsPublic && (!organizationId.HasValue || template.OrganizationId != organizationId))
        {
            return Result<ServerTemplateDetail>.Failure("template_not_found");
        }

        return Result<ServerTemplateDetail>.Success(MapToDetail(template));
    }

    public async Task<Result<ServerTemplateDetail>> CreateTemplateAsync(
        Guid organizationId,
        CreateServerTemplateRequest request,
        CancellationToken ct = default)
    {
        // Check for duplicate name (exclude soft-deleted templates)
        var exists = await _db.ServerTemplates.AnyAsync(
            t => t.OrganizationId == organizationId && t.Name == request.Name && !t.IsArchived && t.DeletedAt == null, ct);

        if (exists)
        {
            return Result<ServerTemplateDetail>.Failure("template_name_exists");
        }

        var template = new ServerTemplate
        {
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            GameType = request.GameType,
            IsPublic = request.IsPublic,
            DefaultCpuLimitMillicores = request.DefaultCpuLimitMillicores,
            DefaultMemoryLimitMb = request.DefaultMemoryLimitMb,
            DefaultDiskLimitMb = request.DefaultDiskLimitMb,
            DefaultStartupCommand = request.DefaultStartupCommand,
            DefaultJavaFlags = request.DefaultJavaFlags
        };

        _db.ServerTemplates.Add(template);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseHelpers.IsUniqueConstraintViolation(ex))
        {
            return Result<ServerTemplateDetail>.Failure("template_name_exists");
        }

        _logger.LogInformation("Created template {TemplateId} '{TemplateName}' for org {OrgId}",
            template.Id, template.Name, organizationId);

        return Result<ServerTemplateDetail>.Success(MapToDetail(template));
    }

    public async Task<Result<ServerTemplateDetail>> UpdateTemplateAsync(
        Guid organizationId,
        Guid templateId,
        UpdateServerTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await _db.ServerTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == organizationId && !t.IsArchived && t.DeletedAt == null, ct);

        if (template is null)
        {
            return Result<ServerTemplateDetail>.Failure("template_not_found");
        }

        if (request.Name != null && request.Name != template.Name)
        {
            var exists = await _db.ServerTemplates.AnyAsync(
                t => t.OrganizationId == organizationId && t.Name == request.Name && t.Id != templateId && !t.IsArchived && t.DeletedAt == null, ct);

            if (exists)
            {
                return Result<ServerTemplateDetail>.Failure("template_name_exists");
            }

            template.Name = request.Name;
        }

        if (request.Description != null) template.Description = request.Description;
        if (request.IsPublic.HasValue) template.IsPublic = request.IsPublic.Value;
        if (request.IsArchived.HasValue) template.IsArchived = request.IsArchived.Value;
        if (request.DefaultCpuLimitMillicores.HasValue) template.DefaultCpuLimitMillicores = request.DefaultCpuLimitMillicores.Value;
        if (request.DefaultMemoryLimitMb.HasValue) template.DefaultMemoryLimitMb = request.DefaultMemoryLimitMb.Value;
        if (request.DefaultDiskLimitMb.HasValue) template.DefaultDiskLimitMb = request.DefaultDiskLimitMb.Value;
        if (request.DefaultStartupCommand != null) template.DefaultStartupCommand = request.DefaultStartupCommand;
        if (request.DefaultJavaFlags != null) template.DefaultJavaFlags = request.DefaultJavaFlags;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseHelpers.IsUniqueConstraintViolation(ex))
        {
            return Result<ServerTemplateDetail>.Failure("template_name_exists");
        }

        _logger.LogInformation("Updated template {TemplateId} for org {OrgId}", templateId, organizationId);

        return Result<ServerTemplateDetail>.Success(MapToDetail(template));
    }

    public async Task<Result<bool>> DeleteTemplateAsync(
        Guid organizationId,
        Guid templateId,
        CancellationToken ct = default)
    {
        var template = await _db.ServerTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == organizationId && t.DeletedAt == null, ct);

        if (template is null)
        {
            return Result<bool>.Failure("template_not_found");
        }

        template.IsArchived = true;
        template.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted template {TemplateId} for org {OrgId}", templateId, organizationId);

        return Result<bool>.Success(true);
    }

    private static ServerTemplateDetail MapToDetail(ServerTemplate template)
    {
        return new ServerTemplateDetail(
            template.Id,
            template.OrganizationId,
            template.Name,
            template.Description,
            template.GameType,
            template.IsPublic,
            template.IsArchived,
            template.DefaultCpuLimitMillicores,
            template.DefaultMemoryLimitMb,
            template.DefaultDiskLimitMb,
            template.DefaultStartupCommand,
            null, // DefaultGameSettings would need JSON parsing
            null, // DefaultEnvironmentVariables would need JSON parsing
            template.DefaultJavaFlags,
            null, // DefaultPorts would need JSON parsing
            template.UsageCount,
            template.CreatedAt,
            template.UpdatedAt);
    }
}
