using Dhadgar.Agent.Core.Identity;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Identity;

public sealed class AgentIdentityProviderTests
{
    private static readonly DateTimeOffset EnrolledAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task WaitForIdentity_CompletesOnSet()
    {
        var sut = new AgentIdentityProvider();
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);

        var waiting = sut.WaitForIdentityAsync(CancellationToken.None);
        var set = sut.Set(identity);
        var completed = await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(set.IsSuccess, set.Error);
        Assert.Equal(identity, completed);
        Assert.Equal(identity, sut.Current);
    }

    [Fact]
    public async Task WaitForIdentity_AfterSet_CompletesImmediately()
    {
        var sut = new AgentIdentityProvider();
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);
        sut.Set(identity);

        var completed = await sut.WaitForIdentityAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(identity, completed);
    }

    [Fact]
    public void Current_BeforeSet_IsNull()
    {
        var sut = new AgentIdentityProvider();

        Assert.Null(sut.Current);
    }

    [Fact]
    public void Set_SameNodeTwice_Succeeds()
    {
        var sut = new AgentIdentityProvider();
        var nodeId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var first = new AgentIdentity(nodeId, orgId, EnrolledAt);

        var firstResult = sut.Set(first);
        var secondResult = sut.Set(new AgentIdentity(nodeId, orgId, EnrolledAt.AddMinutes(1)));

        Assert.True(firstResult.IsSuccess, firstResult.Error);
        Assert.True(secondResult.IsSuccess, secondResult.Error);
        Assert.Equal(first, sut.Current);
    }

    [Fact]
    public void Set_DifferentNode_Fails()
    {
        var sut = new AgentIdentityProvider();
        var first = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);
        sut.Set(first);

        var result = sut.Set(new AgentIdentity(Guid.NewGuid(), first.OrganizationId, EnrolledAt));

        Assert.True(result.IsFailure);
        Assert.Contains("[AgentIdentity.Changed]", result.Error, StringComparison.Ordinal);
        Assert.Equal(first, sut.Current);
    }

    [Fact]
    public void Set_EmptyNodeId_Fails()
    {
        var sut = new AgentIdentityProvider();

        var result = sut.Set(new AgentIdentity(Guid.Empty, Guid.NewGuid(), EnrolledAt));

        Assert.True(result.IsFailure);
        Assert.Contains("[AgentIdentity.Invalid]", result.Error, StringComparison.Ordinal);
        Assert.Null(sut.Current);
    }

    [Fact]
    public async Task WaitForIdentity_HonoursCancellation()
    {
        var sut = new AgentIdentityProvider();
        using var cts = new CancellationTokenSource();

        var waiting = sut.WaitForIdentityAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(sut.Current);
    }
}
