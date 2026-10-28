using System.Reflection;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Windows.Windows;
using Dhadgar.Contracts.Agents;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L3: the Windows agent and the lab read the same footprint names. Each name the agent installs under must equal
/// <see cref="AgentWindowsFootprint"/>. The CA store constant is internal, so it is read by reflection.
/// </summary>
public sealed class AgentFootprintSeamTests
{
    [Trait("Seam", "S-L3")]
    [Fact]
    public void ServiceName_MatchesFootprint()
    {
        Assert.Equal(AgentWindowsFootprint.ServiceName, Dhadgar.Agent.Windows.Program.ServiceName);
    }

    [Trait("Seam", "S-L3")]
    [Fact]
    public void CaStoreName_MatchesFootprint()
    {
        var field = typeof(WindowsCertificateStore).GetField("CaStoreName", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(AgentWindowsFootprint.CaStoreName, field.GetValue(null));
    }

    [Trait("Seam", "S-L3")]
    [Fact]
    public void EventLogSourceName_MatchesFootprint()
    {
        Assert.Equal(AgentWindowsFootprint.EventLogSourceName, Dhadgar.Agent.Windows.Windows.EventLogSetup.SourceName);
    }

    [Trait("Seam", "S-L3")]
    [Fact]
    public void StateFileName_MatchesFootprint()
    {
        Assert.Equal(AgentWindowsFootprint.StateFileName, FileAgentStateStore.FileName);
    }
}
