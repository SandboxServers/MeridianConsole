using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Services;

namespace Dhadgar.Servers.Endpoints;

public static class ServerTemplatesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/templates")
            .WithTags("Server Templates")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", ListTemplates)
            .WithName("ListServerTemplates")
            .WithDescription("List server templates for an organization")
            .WithSummary("List templates")
            .Produces<PagedResponse<ServerTemplateListItem>>();

        group.MapGet("/{templateId:guid}", GetTemplate)
            .WithName("GetServerTemplate")
            .WithDescription("Get a server template by ID")
            .WithSummary("Get template")
            .Produces<ServerTemplateDetail>()
            .ProducesProblem(404);

        group.MapPost("", CreateTemplate)
            .WithName("CreateServerTemplate")
            .WithDescription("Create a new server template")
            .WithSummary("Create template")
            .Produces<ServerTemplateDetail>(201)
            .ProducesProblem(400);

        group.MapPatch("/{templateId:guid}", UpdateTemplate)
            .WithName("UpdateServerTemplate")
            .WithDescription("Update a server template")
            .WithSummary("Update template")
            .Produces<ServerTemplateDetail>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapDelete("/{templateId:guid}", DeleteTemplate)
            .WithName("DeleteServerTemplate")
            .WithDescription("Delete a server template")
            .WithSummary("Delete template")
            .Produces(204)
            .ProducesProblem(404);

        // Public templates endpoint (no org required)
        var publicGroup = app.MapGroup("/templates")
            .WithTags("Server Templates");

        publicGroup.MapGet("/public", ListPublicTemplates)
            .WithName("ListPublicServerTemplates")
            .WithDescription("List public server templates")
            .WithSummary("List public templates")
            .Produces<PagedResponse<ServerTemplateListItem>>();
    }

    private static async Task<IResult> ListTemplates(
        Guid organizationId,
        IServerTemplateService templateService,
        bool includePublic = true,
        string? gameType = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Clamp page and pageSize to valid ranges
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await templateService.GetTemplatesAsync(
            organizationId, includePublic, gameType, page, pageSize, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListPublicTemplates(
        IServerTemplateService templateService,
        string? gameType = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Clamp page and pageSize to valid ranges
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await templateService.GetTemplatesAsync(
            null, true, gameType, page, pageSize, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTemplate(
        Guid organizationId,
        Guid templateId,
        IServerTemplateService templateService,
        CancellationToken ct = default)
    {
        var result = await templateService.GetTemplateAsync(templateId, organizationId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "template_not_found");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateTemplate(
        Guid organizationId,
        CreateServerTemplateRequest request,
        IServerTemplateService templateService,
        CancellationToken ct = default)
    {
        var result = await templateService.CreateTemplateAsync(organizationId, request, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.BadRequest(result.Error ?? "create_failed");
        }

        return Results.Created($"/organizations/{organizationId}/templates/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> UpdateTemplate(
        Guid organizationId,
        Guid templateId,
        UpdateServerTemplateRequest request,
        IServerTemplateService templateService,
        CancellationToken ct = default)
    {
        var result = await templateService.UpdateTemplateAsync(organizationId, templateId, request, ct);

        if (!result.Success)
        {
            return result.Error == "template_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteTemplate(
        Guid organizationId,
        Guid templateId,
        IServerTemplateService templateService,
        CancellationToken ct = default)
    {
        var result = await templateService.DeleteTemplateAsync(organizationId, templateId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "template_not_found");
        }

        return Results.NoContent();
    }
}
