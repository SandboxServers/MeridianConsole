using Dhadgar.Contracts.Agents;
using Xunit;

namespace Dhadgar.Contracts.Tests.Agents;

public class AgentCommandTypesTests
{
    [Theory]
    [InlineData("Ping")]
    [InlineData("StartServer")]
    [InlineData("StopServer")]
    [InlineData("ServerStatus")]
    public void IsKnown_ReturnsTrue_ForKnownTypes(string commandType)
    {
        Assert.True(AgentCommandTypes.IsKnown(commandType));
    }

    [Fact]
    public void IsKnown_IsCaseSensitive()
    {
        Assert.False(AgentCommandTypes.IsKnown("ping"));
    }

    [Fact]
    public void IsKnown_ReturnsFalse_ForNull()
    {
        Assert.False(AgentCommandTypes.IsKnown(null));
    }

    [Fact]
    public void IsKnown_ReturnsFalse_ForGetStatus()
    {
        Assert.False(AgentCommandTypes.IsKnown("GetStatus"));
    }
}
