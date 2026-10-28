using Dhadgar.Lab.Faults;

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
    public void Find_UnknownName_ReturnsNull()
    {
        Assert.Null(ProxyCatalog.Find("no-such-proxy"));
    }
}
