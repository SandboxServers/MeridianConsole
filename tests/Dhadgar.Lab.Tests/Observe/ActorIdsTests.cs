using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// Logical actor ids follow the C18 pattern.
/// </summary>
public sealed class ActorIdsTests
{
    [Fact]
    public void Validate_Uppercase_Fails()
    {
        var result = ActorIds.Validate("Client:1");

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    [Fact]
    public void Validate_ClientWithIndex_Ok()
    {
        var result = ActorIds.Validate(ActorIds.Client(3));

        Assert.True(result.IsSuccess);
        Assert.Equal("client:3", result.Value);
    }

    [Fact]
    public void Agent_IsZeroPaddedToFourDigits()
    {
        Assert.Equal("agent:0007", ActorIds.Agent(7));
        Assert.True(ActorIds.Validate(ActorIds.Agent(7)).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nobody")]
    [InlineData("client:")]
    [InlineData("tenant:A")]
    [InlineData("client:1/2")]
    public void Validate_UnknownOrMalformed_Fails(string actor)
    {
        Assert.True(ActorIds.Validate(actor).IsFailure);
    }
}
