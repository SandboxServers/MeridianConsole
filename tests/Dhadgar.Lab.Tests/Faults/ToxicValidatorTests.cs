using Dhadgar.Lab.Faults;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The toxic allowlist: the only attributes per type, their ranges, the stream and the toxicity.
/// </summary>
public sealed class ToxicValidatorTests
{
    [Theory]
    [InlineData("latency", "latency", 60000)]
    [InlineData("latency", "jitter", 0)]
    [InlineData("bandwidth", "rate", 1000000)]
    [InlineData("timeout", "timeout", 900000)]
    [InlineData("reset_peer", "timeout", 0)]
    [InlineData("slow_close", "delay", 60000)]
    [InlineData("slicer", "average_size", 1)]
    [InlineData("slicer", "size_variation", 65536)]
    [InlineData("slicer", "delay", 1000000)]
    [InlineData("limit_data", "bytes", 1073741824)]
    public void Validate_AllowedAttributeAtItsBounds_Succeeds(string type, string attribute, long value)
    {
        var result = ToxicValidator.Validate(type, "downstream", 1.0, new Dictionary<string, long> { [attribute] = value });

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("latency", "latency", 60001)]
    [InlineData("latency", "latency", -1)]
    [InlineData("bandwidth", "rate", 1000001)]
    [InlineData("slicer", "average_size", 0)]
    [InlineData("limit_data", "bytes", 1073741825)]
    public void Validate_AttributeOutsideItsRange_IsInvalidArgument(string type, string attribute, long value)
    {
        var result = ToxicValidator.Validate(type, "upstream", 0.5, new Dictionary<string, long> { [attribute] = value });

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("latency", "rate")]
    [InlineData("bandwidth", "latency")]
    [InlineData("timeout", "jitter")]
    [InlineData("slicer", "bytes")]
    public void Validate_AttributeNotAllowedForType_IsInvalidArgument(string type, string attribute)
    {
        var result = ToxicValidator.Validate(type, "upstream", 0.5, new Dictionary<string, long> { [attribute] = 1 });

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("LATENCY")]
    public void Validate_UnknownType_IsInvalidArgument(string type)
    {
        var result = ToxicValidator.Validate(type, "upstream", 0.5, new Dictionary<string, long>());

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("")]
    [InlineData("Upstream")]
    public void Validate_StreamOtherThanUpstreamOrDownstream_IsInvalidArgument(string stream)
    {
        var result = ToxicValidator.Validate("latency", stream, 0.5, new Dictionary<string, long>());

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_ToxicityOutsideZeroToOne_IsInvalidArgument(double toxicity)
    {
        var result = ToxicValidator.Validate("latency", "upstream", toxicity, new Dictionary<string, long>());

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Validate_ToxicityAtTheEdges_Succeeds(double toxicity)
    {
        var result = ToxicValidator.Validate("latency", "upstream", toxicity, new Dictionary<string, long>());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NoAttributes_UsesToxiproxyDefaults()
    {
        var result = ToxicValidator.Validate("slicer", "downstream", 1.0, new Dictionary<string, long>());

        Assert.True(result.IsSuccess);
    }
}
