using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The <c>msiProduct</c> cleanup entry (P23): the engine's allowlist and undo, and the msiexec loop behind it, with
/// fakes for the installer database and for <c>msiexec</c>. No test installs or removes a product.
/// </summary>
public class MsiProductCleanupTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-msi-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private const string LogPath = @"C:\run-1\logs\msi-cleanup.log";

    public MsiProductCleanupTests()
    {
        _paths = new RunPaths(_repo, "run-1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private string MsiPath => Path.Combine(_paths.RunDir, "msi", "MeridianConsoleAgent.msi");

    [Fact]
    public async Task UndoAsync_MsiProductEntry_UninstallsOnceAndIsIdempotent()
    {
        var entries = new List<CleanupEntry> { new MsiProductEntry(AgentWindowsFacts.MsiUpgradeCode, MsiPath) };
        var actions = new RecordingActions();

        var first = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);
        var second = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(first.Failed);
        Assert.Empty(second.Failed);
        Assert.Equal(
            [$"UninstallMsiProduct:{AgentWindowsFacts.MsiUpgradeCode}", $"UninstallMsiProduct:{AgentWindowsFacts.MsiUpgradeCode}"],
            actions.Calls);
    }

    [Fact]
    public async Task UndoAsync_MsiProductWithAnotherUpgradeCode_IsRefusedBeforeAnyCall()
    {
        var entries = new List<CleanupEntry> { new MsiProductEntry("00000000-0000-0000-0000-000000000000", MsiPath) };
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Single(report.Failed);
        Assert.Equal("msiProduct", report.Failed[0].Kind);
    }

    [Fact]
    public async Task UndoAsync_MsiProductOutsideTheRunFolder_IsRefusedBeforeAnyCall()
    {
        var outside = Path.Combine(_repo, "elsewhere", "MeridianConsoleAgent.msi");
        var entries = new List<CleanupEntry> { new MsiProductEntry(AgentWindowsFacts.MsiUpgradeCode, outside) };
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Single(report.Failed);
    }

    [Fact]
    public void UninstallRelatedProducts_RunsMsiexecOncePerProduct_AndTreatsRebootAndUnknownAsSuccess()
    {
        var products = new FakeMsiProducts(["{11111111-1111-1111-1111-111111111111}", "{22222222-2222-2222-2222-222222222222}"]);
        var exitCodes = new Queue<int>([3010, 1605]);
        var calls = new List<IReadOnlyList<string>>();

        var result = WindowsMachineActions.UninstallRelatedProducts(products, AgentWindowsFacts.MsiUpgradeCode, args =>
        {
            calls.Add(args);
            return Result<CommandOutput>.Success(new CommandOutput(exitCodes.Dequeue(), string.Empty, string.Empty));
        }, LogPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["/x", "{11111111-1111-1111-1111-111111111111}", "/qn", "/norestart", "/l*v", LogPath], calls[0]);
        Assert.Equal(["/x", "{22222222-2222-2222-2222-222222222222}", "/qn", "/norestart", "/l*v", LogPath], calls[1]);
    }

    [Fact]
    public void UninstallRelatedProducts_NoLogPath_PassesNoLogSwitch()
    {
        var products = new FakeMsiProducts(["{11111111-1111-1111-1111-111111111111}"]);
        var calls = new List<IReadOnlyList<string>>();

        var result = WindowsMachineActions.UninstallRelatedProducts(products, AgentWindowsFacts.MsiUpgradeCode, args =>
        {
            calls.Add(args);
            return Result<CommandOutput>.Success(new CommandOutput(0, string.Empty, string.Empty));
        }, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(["/x", "{11111111-1111-1111-1111-111111111111}", "/qn", "/norestart"], calls[0]);
    }

    [Fact]
    public void UninstallRelatedProducts_EnumerationFails_IsAFailureAndRunsNoMsiexec()
    {
        // The installer could not list the products, so absence is unproven: a success here would let cleanup count
        // msiProduct as undone while the product is still installed.
        var products = new FakeMsiProducts([], "MsiEnumRelatedProductsW returned 5");
        var calls = 0;

        var result = WindowsMachineActions.UninstallRelatedProducts(products, AgentWindowsFacts.MsiUpgradeCode, _ =>
        {
            calls++;
            return Result<CommandOutput>.Success(new CommandOutput(0, string.Empty, string.Empty));
        }, LogPath);

        Assert.True(result.IsFailure);
        Assert.Contains("unreadable", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void UninstallRelatedProducts_NoProductInstalled_CallsNothing()
    {
        var calls = 0;

        var result = WindowsMachineActions.UninstallRelatedProducts(new FakeMsiProducts([]), AgentWindowsFacts.MsiUpgradeCode, _ =>
        {
            calls++;
            return Result<CommandOutput>.Success(new CommandOutput(0, string.Empty, string.Empty));
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void UninstallRelatedProducts_FatalExitCode_IsAFailureCarryingTheCode()
    {
        var products = new FakeMsiProducts(["{11111111-1111-1111-1111-111111111111}"]);

        var result = WindowsMachineActions.UninstallRelatedProducts(products, AgentWindowsFacts.MsiUpgradeCode, _ =>
            Result<CommandOutput>.Success(new CommandOutput(1603, string.Empty, string.Empty)));

        Assert.True(result.IsFailure);
        Assert.Contains("1603", result.Error, StringComparison.Ordinal);
    }

    /// <summary>Fake installer database: a fixed list of related products, or an enumeration failure.</summary>
    private sealed class FakeMsiProducts(IReadOnlyList<string> related, string? enumerationError = null) : IMsiProducts
    {
        public Result<IReadOnlyList<string>> RelatedProducts(string upgradeCode) =>
            enumerationError is null
                ? Result<IReadOnlyList<string>>.Success(related)
                : Result<IReadOnlyList<string>>.Failure(enumerationError);

        public Result<string> ProductVersion(string productCode) => Result<string>.Success("1.0.0");
    }
}
