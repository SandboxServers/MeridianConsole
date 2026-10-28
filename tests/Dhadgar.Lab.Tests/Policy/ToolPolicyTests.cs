using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Policy;

/// <summary>
/// The gate table, the registry it must cover, and the dispatcher's refusals in C3 order.
/// </summary>
public sealed class ToolPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EveryRegisteredTool_IsClassified()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool[] tools = daemon.Services.GetServices<ILabTool>().ToArray();

            Assert.NotEmpty(tools);
            foreach (ILabTool tool in tools)
            {
                Assert.NotNull(ToolPolicy.GateFor(tool.Name));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task PolicyTable_HasNoUnknownNames()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            HashSet<string> registered = daemon.Services.GetServices<ILabTool>()
                .Select(tool => tool.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string name in ToolPolicy.Gates.Keys)
            {
                Assert.True(
                    registered.Contains(name) || ToolPolicy.PlannedTools.Contains(name),
                    "policy entry " + name + " is neither registered nor planned");
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void PolicyTable_HasNoMachineTools()
    {
        Assert.DoesNotContain(LabGate.Machine, ToolPolicy.Gates.Values);
    }

    [Fact]
    public void GateFor_UnknownName_ReturnsNull()
    {
        Assert.Null(ToolPolicy.GateFor("not_a_tool"));
    }

    [Fact]
    public async Task Dispatcher_UnclassifiedTool_IsRefused()
    {
        using var harness = new Harness([new FakeTool("fake_unlisted")]);

        LabToolResult result = await harness.Dispatcher.InvokeAsync("fake_unlisted", Json("{}"), Context(null), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Lab.Unclassified", ErrorCode(result));
    }

    [Fact]
    public async Task Dispatcher_LeasedWithoutLease_IsRefused()
    {
        using var harness = new Harness([new FakeTool("fake_leased")], name => name == "fake_leased" ? LabGate.Leased : null);

        LabToolResult result = await harness.Dispatcher.InvokeAsync("fake_leased", Json("{}"), Context(null), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Lab.LeaseRequired", ErrorCode(result));
    }

    [Fact]
    public async Task Dispatcher_LeasedWithValidLease_RunsTheTool()
    {
        using var harness = new Harness([new FakeTool("fake_leased")], name => name == "fake_leased" ? LabGate.Leased : null);
        string leaseId = harness.Leases.Acquire("s1", "uat", null, false, null).Value.LeaseId;

        LabToolResult result = await harness.Dispatcher.InvokeAsync(
            "fake_leased", Json("{\"lease_id\":\"" + leaseId + "\"}"), Context(leaseId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Payload.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Dispatcher_MachineGate_IsReserved()
    {
        using var harness = new Harness([new FakeTool("fake_machine")], name => name == "fake_machine" ? LabGate.Machine : null);
        string leaseId = harness.Leases.Acquire("s1", "uat", null, false, null).Value.LeaseId;

        LabToolResult result = await harness.Dispatcher.InvokeAsync(
            "fake_machine", Json("{\"lease_id\":\"" + leaseId + "\"}"), Context(leaseId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Lab.MachineReserved", ErrorCode(result));
    }

    [Fact]
    public async Task LeaseEnd_RunsEveryHook_EvenWhenOneThrows()
    {
        using var harness = new Harness([]);
        var throwing = new CountingHook(throws: true);
        var recording = new CountingHook(throws: false);
        var relay = new LeaseEndRelay(harness.Leases, [throwing, recording], NullLogger<LeaseEndRelay>.Instance);
        await relay.StartAsync(CancellationToken.None);

        string leaseId = harness.Leases.Acquire("s1", "uat", null, false, null).Value.LeaseId;
        Assert.True(harness.Leases.Release(leaseId).IsSuccess);

        Assert.Equal(1, throwing.Calls);
        Assert.Equal(1, recording.Calls);
    }

    private static LabCallContext Context(string? leaseId) => new("call-1", leaseId, "mcp");

    private static JsonElement Json(string raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static string? ErrorCode(LabToolResult result) =>
        result.Payload.GetProperty("error").GetProperty("code").GetString();

    private static string NewRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "lab-l05-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<LabDaemon> StartDaemonAsync(string root)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        string token = Base64Url.EncodeToString(bytes);
        Array.Clear(bytes);

        var started = await LabDaemon.StartAsync(new LabDaemonOptions(
            Port: 0,
            Token: token,
            DataRoot: root,
            EnforceSingleInstance: false,
            RefuseElevated: false,
            Time: new FakeTimeProvider(Start)), CancellationToken.None);

        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }

    /// <summary>
    /// A lab tool with a given name and no real work: it returns <c>{ok: true}</c>.
    /// </summary>
    private sealed class FakeTool : LabTool<FakeArgs>
    {
        private readonly string _name;

        public FakeTool(string name)
        {
            _name = name;
        }

        public override string Name => _name;

        public override string Description => "test tool";

        protected override Task<Result<object>> ExecuteAsync(FakeArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { ok = true }));
    }

    private sealed record FakeArgs(string? LeaseId = null);

    /// <summary>
    /// An end-of-lease hook that counts its calls and optionally throws.
    /// </summary>
    private sealed class CountingHook(bool throws) : ILeaseEndHook
    {
        public int Calls { get; private set; }

        public Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct)
        {
            Calls++;
            if (throws)
            {
                throw new InvalidOperationException("hook failure");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A dispatcher over a fresh audit log, lease book and vault in a temporary data root.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly string _root;
        private readonly SecretVault _vault;
        private readonly AuditLog _audit;

        public Harness(IEnumerable<ILabTool> tools, Func<string, LabGate?>? gates = null)
        {
            _root = NewRoot();
            var time = new FakeTimeProvider(Start);
            _vault = new SecretVault();
            var redactor = new Redactor(_vault);
            _audit = new AuditLog(LabPaths.FromRoot(_root), new PathValidator(NullLogger<PathValidator>.Instance), redactor, time);
            Leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var registry = new LabToolRegistry(tools);

            Dispatcher = gates is null
                ? new LabToolDispatcher(registry, Leases, _audit, redactor, time)
                : new LabToolDispatcher(registry, Leases, _audit, redactor, time, gates);
        }

        public LeaseBook Leases { get; }

        public LabToolDispatcher Dispatcher { get; }

        public void Dispose()
        {
            _audit.Dispose();
            _vault.Dispose();
            DeleteRoot(_root);
        }
    }
}
