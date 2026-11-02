namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// Pure checks on the in-network probe arguments (D-ML149). No Docker: the argument list is built, never run.
/// </summary>
public sealed class ProxyListenerProbeTests
{
    [Fact]
    public void Args_NodesPostgres_TargetsToxiproxyInsideTheLabNetwork()
    {
        IReadOnlyList<string> args = ProxyListenerProbe.Args("nodes-postgres");
        string[] expected = ["exec", "meridian-lab-postgres", "pg_isready", "-h", "meridian-lab-toxiproxy", "-p", "20020", "-U", "postgres", "-t", "2"];

        Assert.Equal(expected, args);
        Assert.DoesNotContain(args, arg => arg.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Args_NonPostgresUpstream_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ProxyListenerProbe.Args("nodes-rabbitmq"));
    }
}
