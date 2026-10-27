using Dhadgar.Agent.Core.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

public sealed class ConfigurationEnrollmentTokenSourceTests
{
    private static ConfigurationEnrollmentTokenSource Create(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigurationEnrollmentTokenSource.ConfigurationKey] = value })
            .Build();
        return new ConfigurationEnrollmentTokenSource(configuration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_ReturnsEmptyNotFailure(string? value)
    {
        var result = Create(value).GetToken();

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void Value_IsTrimmed()
    {
        var result = Create("  enroll-token-123 \t").GetToken();

        Assert.True(result.IsSuccess);
        Assert.Equal("enroll-token-123", result.Value);
    }
}