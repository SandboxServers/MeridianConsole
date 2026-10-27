using Dhadgar.Agent.Windows.Installation;
using Dhadgar.Agent.Windows.Windows;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>Pins the driver's copies of the agent constants. A rename on either side fails here.</summary>
public class AgentWindowsFactsTests
{
    [Fact]
    public void ServiceName_EqualsAgentProgramServiceName()
    {
        Assert.Equal(Program.ServiceName, AgentWindowsFacts.ServiceName);
    }

    [Fact]
    public void EventLogSourceName_EqualsAgentProgramEventLogSourceName()
    {
        Assert.Equal(Program.EventLogSourceName, AgentWindowsFacts.EventLogSourceName);
    }

    [Fact]
    public void CaStoreName_EqualsWindowsCertificateStoreCaStoreName()
    {
        Assert.Equal(WindowsCertificateStore.CaStoreName, AgentWindowsFacts.CaStoreName);
    }

    [Fact]
    public void RegistryKeyPath_EqualsAgentRegistryKeyPath()
    {
        Assert.Equal(AgentRegistry.KeyPath, AgentWindowsFacts.RegistryKeyPath);
    }

    [Fact]
    public void EnrollmentTokenValueName_EqualsAgentRegistryValueName()
    {
        Assert.Equal(AgentRegistry.EnrollmentTokenValueName, AgentWindowsFacts.EnrollmentTokenValueName);
    }
}
