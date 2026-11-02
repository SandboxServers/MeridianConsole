using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Stack;

namespace Dhadgar.Lab.Tests.Stack;

public sealed class ProxyCatalogTests
{
    [Fact]
    public void All_Names_AreUnique()
    {
        List<string> names = ProxyCatalog.All.Select(p => p.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_ListenPorts_AreUnique()
    {
        List<int> ports = ProxyCatalog.All.Select(p => p.ListenPort).ToList();

        Assert.Equal(ports.Count, ports.Distinct().Count());
    }

    [Fact]
    public void All_Packets_AreLabWaveIdentifiers()
    {
        Assert.All(ProxyCatalog.All, p => Assert.Matches(@"^L\d{2}$", p.Packet));
    }

    [Fact]
    public void ForPacket_L08_ReturnsEveryProxyOwnedByL08()
    {
        int l08 = ProxyCatalog.ForPacket("L08").Count();

        Assert.Equal(ProxyCatalog.All.Count(p => p.Packet == "L08"), l08);
        Assert.True(l08 > 0);
    }

    [Fact]
    public void StackProxies_ExcludesOnlyL41()
    {
        int l41 = ProxyCatalog.All.Count(p => p.Packet == "L41");

        Assert.True(l41 > 0);
        Assert.Equal(ProxyCatalog.All.Count - l41, ProxyCatalog.StackProxies.Count());
        Assert.DoesNotContain(ProxyCatalog.StackProxies, p => p.Packet == "L41");
    }

    [Fact]
    public void StackProxies_CoverEveryAdapterProxy()
    {
        // Guard for F129: a proxy an adapter names must be created by stack_up, or its p:<name> target does not exist.
        HashSet<string> stackNames = ProxyCatalog.StackProxies.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        List<ILabServiceAdapter> adapters = typeof(LabStack).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ILabServiceAdapter).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (ILabServiceAdapter)Activator.CreateInstance(t)!)
            .ToList();

        Assert.NotEmpty(adapters);
        foreach (ILabServiceAdapter adapter in adapters)
        {
            foreach (string name in adapter.Proxies)
            {
                Assert.True(stackNames.Contains(name), $"Adapter {adapter.Name} names proxy {name}, which stack_up does not create.");
            }
        }
    }

    [Fact]
    public void Find_UnknownName_ReturnsNull()
    {
        Assert.Null(ProxyCatalog.Find("no-such-proxy"));
    }
}
