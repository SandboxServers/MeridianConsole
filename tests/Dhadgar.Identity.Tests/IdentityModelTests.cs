using System.Linq;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Dhadgar.Identity.Tests;

public class IdentityModelTests
{
    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_identity_test;Username=dhadgar;Password=dhadgar")
            .Options;

        return new IdentityDbContext(options);
    }

    [Fact]
    public void Model_contains_expected_entities()
    {
        using var context = CreateContext();
        var entityTypes = context.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();

        Assert.Contains(typeof(User), entityTypes);
        Assert.Contains(typeof(Organization), entityTypes);
        Assert.Contains(typeof(UserOrganization), entityTypes);
        Assert.Contains(typeof(UserOrganizationClaim), entityTypes);
        Assert.Contains(typeof(LinkedAccount), entityTypes);
        Assert.Contains(typeof(RefreshToken), entityTypes);
        Assert.Contains(typeof(ClaimDefinition), entityTypes);
    }

    [Fact]
    public void User_has_soft_delete_filter_and_unique_external_auth_id_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(User));

        Assert.NotNull(entity);
        Assert.NotEmpty(entity!.GetDeclaredQueryFilters());

        var externalAuthIndex = entity.GetIndexes()
            .Single(index => index.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(User.ExternalAuthId) }));

        Assert.True(externalAuthIndex.IsUnique);
    }

    [Fact]
    public void Organization_has_owned_settings()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Organization));

        Assert.NotNull(entity);

        var settingsNavigation = entity!.FindNavigation(nameof(Organization.Settings));

        Assert.NotNull(settingsNavigation);
        Assert.True(settingsNavigation!.TargetEntityType.IsOwned());
    }

    [Fact]
    public void RefreshToken_has_unique_token_hash_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(RefreshToken));

        Assert.NotNull(entity);

        var tokenHashIndex = entity!.GetIndexes()
            .Single(index => index.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(RefreshToken.TokenHash) }));

        Assert.True(tokenHashIndex.IsUnique);
    }

    [Fact]
    public void Claim_definitions_are_seeded()
    {
        using var context = CreateContext();
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var entity = designTimeModel.FindEntityType(typeof(ClaimDefinition));

        Assert.NotNull(entity);

        var seedData = entity!.GetSeedData().ToArray();

        Assert.Equal(22, seedData.Length);
        Assert.Contains(seedData, seed =>
            seed.TryGetValue(nameof(ClaimDefinition.Name), out var value) &&
            value is string name &&
            name == "org:read");
    }

    [Fact]
    public void User_version_is_concurrency_token()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(User));

        Assert.NotNull(entity);

        var versionProperty = entity!.FindProperty(nameof(User.Version));

        Assert.NotNull(versionProperty);
        Assert.True(versionProperty!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versionProperty.ValueGenerated);
    }
}
