using Xunit;
using System.Reflection;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Tests.Communication;

/// <summary>
/// Guards D-42: <see cref="ControlPlaneHubTransport"/> is the only way tests change the agent's hub transport. The
/// <c>IHubConnectionConfigurator</c> hook is gone, so <see cref="ControlPlaneClient"/> has its original single
/// constructor and no type of that name exists in the assembly.
/// </summary>
public sealed class ControlPlaneClientTransportHookTests
{
    [Fact]
    public void ControlPlaneClient_HasOnlyTheTransportHook()
    {
        var constructors = typeof(ControlPlaneClient).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var constructor = Assert.Single(constructors);

        Type[] expected =
        [
            typeof(IOptions<AgentOptions>),
            typeof(ICertificateStore),
            typeof(AgentMeter),
            typeof(AgentActivitySource),
            typeof(ILogger<ControlPlaneClient>),
            typeof(IAgentIdentityProvider),
            typeof(ControlPlaneHubTransport)
        ];

        var actual = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal(expected, actual);

        var hookTypes = typeof(ControlPlaneClient).Assembly.GetTypes()
            .Where(t => t.Name == "IHubConnectionConfigurator")
            .ToArray();
        Assert.Empty(hookTypes);
    }
}
