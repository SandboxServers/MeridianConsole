using Dhadgar.Contracts.Agents;
using Dhadgar.Nodes.Hubs;

namespace Dhadgar.Nodes.Tests.Hubs;

public sealed class AgentTelemetryValidatorTests
{
    private static readonly DateTimeOffset ValidTimestamp = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidTelemetry_IsValid()
    {
        var telemetry = new AgentTelemetry(ValidTimestamp, new Dictionary<string, double> { ["cpu"] = 12.5 });

        Assert.True(AgentTelemetryValidator.IsValid(telemetry));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void MetricCount_IsBoundedAt500(int count, bool expected)
    {
        Assert.Equal(expected, AgentTelemetryValidator.IsValid(WithMetrics(count, i => $"m{i}")));
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void MetricNameLength_IsBoundedAt128(int length, bool expected)
    {
        var name = new string('n', length);

        Assert.Equal(expected, AgentTelemetryValidator.IsValid(WithMetrics(1, _ => name)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nname")]
    public void MetricName_RejectsEmptyAndControlCharacters(string name)
    {
        Assert.False(AgentTelemetryValidator.IsValid(WithMetrics(1, _ => name)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MetricValue_MustBeFinite(double value)
    {
        var telemetry = new AgentTelemetry(ValidTimestamp, new Dictionary<string, double> { ["cpu"] = value });

        Assert.False(AgentTelemetryValidator.IsValid(telemetry));
    }

    [Fact]
    public void NullMetrics_IsInvalid()
    {
        var telemetry = new AgentTelemetry(ValidTimestamp, null!);

        Assert.False(AgentTelemetryValidator.IsValid(telemetry));
    }

    [Fact]
    public void DefaultTimestamp_IsInvalid()
    {
        var telemetry = new AgentTelemetry(default, new Dictionary<string, double> { ["cpu"] = 1 });

        Assert.False(AgentTelemetryValidator.IsValid(telemetry));
    }

    private static AgentTelemetry WithMetrics(int count, Func<int, string> nameFor)
    {
        var metrics = new Dictionary<string, double>(count);
        for (var i = 0; i < count; i++)
        {
            metrics[nameFor(i)] = 1;
        }

        return new AgentTelemetry(ValidTimestamp, metrics);
    }
}
