using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// Op ids round-trip through their text form and reject malformed text.
/// </summary>
public sealed class OpIdTests
{
    [Fact]
    public void ToString_Parse_RoundTrips()
    {
        var op = new OpId("run-20260101-a1", ActorIds.Client(2), 42);

        var parsed = OpId.Parse(op.ToString());

        Assert.True(parsed.IsSuccess);
        Assert.Equal(op, parsed.Value);
        Assert.Equal("run-20260101-a1/client:2/42", op.ToString());
    }

    [Fact]
    public void Parse_MissingSeq_Fails()
    {
        var result = OpId.Parse("run-20260101-a1/client:2");

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("run/lab/0")]
    [InlineData("run/lab/x")]
    [InlineData("run/Lab/3")]
    [InlineData("/lab/3")]
    public void Parse_Malformed_Fails(string text)
    {
        Assert.True(OpId.Parse(text).IsFailure);
    }
}
