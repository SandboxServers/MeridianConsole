using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Dhadgar.Identity.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task Calculates_permissions_with_grants_and_denies()
    {
        using var context = CreateContext();
        var user = new User { ExternalAuthId = "external-1", Email = "user@example.com" };
        var org = new Organization { Name = "Test Org", Slug = "test-org", Owner = user };
        var membership = new UserOrganization
        {
            User = user,
            Organization = org,
            Role = "operator",
            JoinedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Organizations.Add(org);
        context.UserOrganizations.Add(membership);

        context.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganization = membership,
            ClaimType = ClaimType.Grant,
            ClaimValue = "servers:delete",
            GrantedBy = user,
            GrantedByUserId = user.Id,
            GrantedAt = DateTime.UtcNow
        });

        context.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganization = membership,
            ClaimType = ClaimType.Deny,
            ClaimValue = "files:write",
            GrantedBy = user,
            GrantedByUserId = user.Id,
            GrantedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new PermissionService(context, TimeProvider.System);
        var permissions = await service.CalculatePermissionsAsync(user.Id, org.Id);

        Assert.Contains("servers:read", permissions);
        Assert.Contains("servers:delete", permissions);
        Assert.DoesNotContain("files:write", permissions);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new IdentityDbContext(options);
    }
}
