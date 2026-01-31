using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("", ListUsers)
            .WithName("ListUsers")
            .WithDescription("List all users in an organization");

        group.MapGet("/{userId:guid}", GetUser)
            .WithName("GetUser")
            .WithDescription("Get user details by ID");

        group.MapPost("", CreateUser)
            .WithName("CreateUser")
            .WithDescription("Create a new user in the organization");

        group.MapPatch("/{userId:guid}", UpdateUser)
            .WithName("UpdateUser")
            .WithDescription("Update user details");

        group.MapDelete("/{userId:guid}", DeleteUser)
            .WithName("DeleteUser")
            .WithDescription("Soft-delete a user");

        group.MapDelete("/{userId:guid}/linked-accounts/{linkedAccountId:guid}", UnlinkAccount)
            .WithName("UnlinkAccount")
            .WithDescription("Unlink an OAuth provider account from a user");
    }

    private static async Task<IResult> ListUsers(
        HttpContext context,
        Guid organizationId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            userId,
            organizationId,
            "members:read",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var users = await userService.ListForOrganizationAsync(organizationId, ct);
        return Results.Ok(users);
    }

    private static async Task<IResult> GetUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:read",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.GetAsync(organizationId, userId, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.UserNotFound, result.Error);
    }

    private static async Task<IResult> CreateUser(
        HttpContext context,
        Guid organizationId,
        UserCreateRequest request,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:invite",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.CreateAsync(organizationId, actorUserId, request, ct);
        if (result.Success)
        {
            return Results.Created($"/organizations/{organizationId}/users/{result.Value?.Id}", result.Value);
        }

        return result.Error switch
        {
            "invalid_email" => ProblemDetailsHelper.UnprocessableEntity(ErrorCodes.IdentityErrors.InvalidEmail),
            "email_already_exists" => ProblemDetailsHelper.Conflict(ErrorCodes.IdentityErrors.EmailAlreadyExists),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
    }

    private static async Task<IResult> UpdateUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserUpdateRequest request,
        UserService userService,
        IPermissionService permissionService,
        IValidator<UserUpdateRequest> validator,
        CancellationToken ct)
    {
        // 1. Auth check
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        // 2. Permission check
        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:invite",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        // 3. Validation (after auth)
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                validationResult.Errors[0].ErrorMessage);
        }

        // 4. Business logic
        var result = await userService.UpdateAsync(organizationId, userId, request, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error);
    }

    private static async Task<IResult> DeleteUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:remove",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.SoftDeleteAsync(organizationId, userId, ct);
        if (result.Success)
        {
            return Results.NoContent();
        }

        return result.Error switch
        {
            "user_not_found" or "not_found" =>
                ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.UserNotFound, result.Error),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
    }

    private static async Task<IResult> UnlinkAccount(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        Guid linkedAccountId,
        IdentityDbContext dbContext,
        ILinkedAccountService linkedAccountService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        if (actorUserId != userId)
        {
            var permissionResult = await EndpointHelpers.RequirePermissionAsync(
                actorUserId,
                organizationId,
                "members:roles",
                permissionService,
                ct);

            if (permissionResult is not null)
            {
                return permissionResult;
            }
        }

        var membershipExists = await dbContext.UserOrganizations
            .AsNoTracking()
            .AnyAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (!membershipExists)
        {
            return ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.MemberNotFound, "User is not a member of this organization.");
        }

        var result = await linkedAccountService.UnlinkAsync(userId, linkedAccountId, ct);
        return result.Success
            ? Results.NoContent()
            : ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error);
    }
}
