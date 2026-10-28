using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Faults;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The blast-radius rules at the tool layer: every mutating tool that takes a target implements <see cref="IFaultTool"/>, and
/// every fault tool refuses a target the guard must never let through.
/// </summary>
[Collection("LabHome")]
public sealed class BlastRadiusTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly LabCallContext Context = new("call-blast-radius", null, "test");
    private static readonly string[] TargetProperties = ["target", "proxy", "member", "database", "file"];

    [Fact]
    public async Task EveryLeasedToolWithATarget_ImplementsIFaultTool()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);

            List<string> missing = [];
            foreach (ILabTool tool in daemon.Services.GetServices<ILabTool>())
            {
                bool leased = ToolPolicy.GateFor(tool.Name) == LabGate.Leased;
                if (leased && HasTargetProperty(tool.InputSchema) && tool is not IFaultTool)
                {
                    missing.Add(tool.Name);
                }
            }

            Assert.Empty(missing);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("c:not-registered")]
    [InlineData("p:not-a-proxy")]
    [InlineData("d:postgres")]
    [InlineData("m:host")]
    public async Task EveryFaultTool_RefusesAnUnsafeTarget(string targetId)
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);

            IFaultTool[] tools = daemon.Services.GetServices<ILabTool>().OfType<IFaultTool>().ToArray();
            Assert.NotEmpty(tools);

            foreach (IFaultTool tool in tools)
            {
                var labTool = (ILabTool)tool;
                Result<JsonElement> result = await labTool.InvokeAsync(tool.SampleArgs(targetId), Context, CancellationToken.None);

                Assert.True(result.IsFailure, $"{labTool.Name} succeeded on {targetId}");
                Assert.True(
                    result.Error.StartsWith("[Lab.BlastRadius]", StringComparison.Ordinal)
                        || result.Error.StartsWith("[Lab.InvalidArgument]", StringComparison.Ordinal),
                    $"{labTool.Name} on {targetId} returned {result.Error}");
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Partition_OnAnEdgeWithNoProxy_IsInvalidArgument()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool tool = daemon.Services.GetServices<ILabTool>().Single(t => t.Name == "fault_partition");

            Result<JsonElement> result = await tool.InvokeAsync(
                JsonSerializer.SerializeToElement(new { from = "nodes", to = "identity" }),
                Context,
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("[Lab.InvalidArgument] no proxy on that edge", result.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FaultsActive_OnAFreshDaemon_IsEmpty()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool tool = daemon.Services.GetServices<ILabTool>().Single(t => t.Name == "faults_active");

            Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(0, result.Value.GetProperty("faults").GetArrayLength());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool HasTargetProperty(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out JsonElement properties))
        {
            return false;
        }

        return TargetProperties.Any(name => properties.TryGetProperty(name, out _));
    }

    private static string NewRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "lab-l19-" + Guid.NewGuid().ToString("N"));
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
}
