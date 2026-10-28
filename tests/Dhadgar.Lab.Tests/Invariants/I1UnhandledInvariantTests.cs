using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I1 over fake log rings and a fixed fault history: which 5xx lines are violations and which a fault excuses (D-ML46).
/// </summary>
public sealed class I1UnhandledInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0.AddMinutes(-5), T0.AddMinutes(5), "s", "r");
    private const string Category = "Microsoft.AspNetCore.Hosting.Diagnostics";

    [Fact]
    public async Task I1_503OnNodes_FaultOnNodesPostgres_Excused()
    {
        var check = Build("nodes", Finished(T0, "Information", "GET", "http://127.0.0.1:15040/api/v1/agents/enroll", 503),
            Fault("nodes-postgres", T0.AddSeconds(-10), null));

        Assert.Empty(await RunAsync(check));
    }

    [Fact]
    public async Task I1_503OnNodes_FaultOnDiscordWebhook_Counts()
    {
        var check = Build("nodes", Finished(T0, "Information", "GET", "http://127.0.0.1:15040/api/v1/agents/enroll", 503),
            Fault("discord-webhook", T0.AddSeconds(-10), null));

        Assert.Single(await RunAsync(check));
    }

    [Fact]
    public async Task I1_503OnGateway_NodesProxyDown_Excused()
    {
        var check = Build("gateway", Finished(T0, "Information", "POST", "https://localhost/api/v1/agents/enroll", 503),
            Fault("gateway-nodes", T0.AddSeconds(-10), null));

        Assert.Empty(await RunAsync(check));
    }

    [Fact]
    public async Task I1_503OnGateway_UnroutedPath_FaultOnNodes_Counts()
    {
        var check = Build("gateway", Finished(T0, "Information", "GET", "http://localhost/api/v1/unrouted/thing", 503),
            Fault("nodes-postgres", T0.AddSeconds(-10), null));

        Assert.Single(await RunAsync(check));
    }

    [Fact]
    public async Task I1_503_FaultHealed15sBefore_Counts()
    {
        var check = Build("nodes", Finished(T0, "Information", "GET", "http://127.0.0.1:15040/api/v1/agents/enroll", 503),
            Fault("nodes-postgres", T0.AddSeconds(-60), T0.AddSeconds(-15)));

        Assert.Single(await RunAsync(check));
    }

    [Fact]
    public async Task I1_500DuringFault_Counts()
    {
        var check = Build("nodes", Finished(T0, "Error", "GET", "http://127.0.0.1:15040/api/v1/agents/enroll", 500),
            Fault("nodes-postgres", T0.AddSeconds(-10), null));

        InvariantViolation violation = Assert.Single(await RunAsync(check));
        Assert.Equal("I1", violation.InvariantId);
        Assert.Equal("unexplained-500", violation.CheckId);
    }

    [Fact]
    public async Task UnhandledException_Line_Counts()
    {
        var ring = new LogRing();
        ring.Add(new LogLine(T0, "Error", "Dhadgar.ServiceDefaults.GlobalExceptionHandler", null, "Unhandled exception"));
        var check = new I1UnhandledInvariant(["nodes"], name => name == "nodes" ? ring : null, new FixedHistory());

        Assert.Single(await RunAsync(check));
    }

    private static I1UnhandledInvariant Build(string service, LogLine line, FaultHistoryEntry fault)
    {
        var ring = new LogRing();
        ring.Add(line);
        return new I1UnhandledInvariant([service], name => name == service ? ring : null, new FixedHistory(fault));
    }

    private static LogLine Finished(DateTimeOffset at, string level, string method, string url, int status) =>
        new(at, level, Category, null, $"Request finished HTTP/1.1 {method} {url} - {status} 0 null 12.3ms");

    private static FaultHistoryEntry Fault(string target, DateTimeOffset started, DateTimeOffset? healed) =>
        new("f-" + target, "fault_disable", target, started, healed);

    private static async Task<IReadOnlyList<InvariantViolation>> RunAsync(IInvariant check)
    {
        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FixedHistory(params FaultHistoryEntry[] entries) : IFaultHistory
    {
        public IReadOnlyList<FaultHistoryEntry> Snapshot() => entries;
    }
}
