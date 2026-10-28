using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

public sealed class LedgerRendererTests
{
    [Fact]
    public void Render_IsDeterministic_AndNamesEveryRow()
    {
        var at = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        IReadOnlyList<LedgerRow> rows =
        [
            new LedgerRow("plan-check", "read-key", "Key readable", "present", RowGrade.Pass, at, Array.Empty<LedgerClause>()),
        ];

        string first = LedgerRenderer.Render("run1", "linux", "nobuild", rows);
        string second = LedgerRenderer.Render("run1", "linux", "nobuild", rows);

        Assert.Equal(first, second);
        Assert.Contains("run1", first, StringComparison.Ordinal);
        Assert.Contains("read-key", first, StringComparison.Ordinal);
        Assert.Contains("Key readable", first, StringComparison.Ordinal);
    }
}

public sealed class SecretScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-scan-" + Guid.NewGuid().ToString("N"));

    public SecretScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ScanFolder_PlantedVaultValue_RemovesFileAndFailsScan()
    {
        using var vault = new SecretVault();
        _ = vault.Add(SecretKind.Password, "planted-secret-value-l16");
        string leak = Path.Combine(_root, "leak.txt");
        File.WriteAllText(leak, "the log says planted-secret-value-l16 in plain text");

        SecretScanResult result = new SecretScanner(vault).ScanFolder(_root);

        Assert.False(result.Passed);
        Assert.Single(result.RemovedFiles);
        Assert.False(File.Exists(leak));
    }

    [Fact]
    public void ScanFolder_CleanFolder_Passes()
    {
        using var vault = new SecretVault();
        _ = vault.Add(SecretKind.Password, "planted-secret-value-l16");
        File.WriteAllText(Path.Combine(_root, "clean.txt"), "nothing to see");

        SecretScanResult result = new SecretScanner(vault).ScanFolder(_root);

        Assert.True(result.Passed);
        Assert.Empty(result.RemovedFiles);
        Assert.True(File.Exists(Path.Combine(_root, "clean.txt")));
    }
}

[Collection("LabHome")]
public sealed class BundleReadTests : IDisposable
{
    private readonly string _root;
    private readonly LabPaths _paths;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public BundleReadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-read-" + Guid.NewGuid().ToString("N"));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lab:RepoRoot"] = _root })
            .Build();
        _paths = LabPaths.Create(config);
        _ = Directory.CreateDirectory(Path.Combine(_paths.UatRuns, "20260101-nobuild-run1"));
        _ = Directory.CreateDirectory(Path.Combine(_paths.UatRuns, "20260101-nobuild-run2"));
        File.WriteAllText(Path.Combine(_paths.UatRuns, "20260101-nobuild-run2", "run.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ResolveFile_OtherRunsRunJson_IsRefused()
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, "run1", _validator);
        Assert.True(folder.IsSuccess);

        Result<string> escaped = BundleFolders.ResolveFile(folder.Value!, "..\\20260101-nobuild-run2\\run.json", _validator);

        Assert.True(escaped.IsFailure);
    }

    [Fact]
    public void Find_InvalidRunId_IsRefused()
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, "..", _validator);

        Assert.True(folder.IsFailure);
    }
}

[Collection("LabHome")]
public sealed class ReproTests : IDisposable
{
    private const string SpecText = """
        schema = 1

        [section]
        id = "plan-check"
        title = "Plan check"

        [[row]]
        id = "read-key"
        title = "Key readable"
        expected = "present"

          [[row.step]]
          id = "read"
          tool = "registry_read"
          args = {}

          [[row.expect]]
          id = "key-row"
          text = "key read"
          source = "step"
          step = "read"
          op = "truthy"
          required = true
        """;

    private readonly string _root;
    private readonly LabPaths _paths;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly ServiceProvider _services;

    public ReproTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-repro-" + Guid.NewGuid().ToString("N"));
        string specs = Path.Combine(_root, "docs", "guides", "uat-specs");
        _ = Directory.CreateDirectory(specs);
        File.WriteAllText(Path.Combine(specs, "a.toml"), SpecText);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lab:RepoRoot"] = _root })
            .Build();
        _paths = LabPaths.Create(config);
        var registry = new LabToolRegistry(new ILabTool[] { new RegistryReadTool() });
        _services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Parse_WrittenReproToml_ReturnsSeedSectionRowsAndSpec()
    {
        const string text = "seed = 42\nbuild_id = \"nobuild\"\nsection = \"plan-check\"\nrows = [\"read-key\"]\nlabel = \"\"\n\n[[specs]]\nfile = \"a.toml\"\nsha256 = \"ABC\"\n";

        Result<ReproSettings> repro = ReproReader.Parse(text);

        Assert.True(repro.IsSuccess);
        Assert.Equal(42UL, repro.Value!.Seed);
        Assert.Equal("plan-check", repro.Value.Section);
        Assert.Equal(new[] { "read-key" }, repro.Value.Rows);
        Assert.Equal("a.toml", repro.Value.SpecFile);
        Assert.Equal("ABC", repro.Value.SpecSha256);
    }

    [Fact]
    public void Parse_MissingSha_IsRefused()
    {
        Result<ReproSettings> repro = ReproReader.Parse("seed = 1\nsection = \"plan-check\"\n[[specs]]\nfile = \"a.toml\"\n");

        Assert.True(repro.IsFailure);
    }

    [Fact]
    public async Task Replay_SpecChanged_IsRefused()
    {
        string runFolder = Path.Combine(_paths.UatRuns, "20260101-nobuild-run1", "repro");
        _ = Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(
            Path.Combine(runFolder, "repro.toml"),
            "seed = 42\nbuild_id = \"nobuild\"\nsection = \"plan-check\"\nrows = [\"read-key\"]\nlabel = \"\"\n\n[[specs]]\nfile = \"a.toml\"\nsha256 = \"NOT-THE-HASH\"\n",
            CancellationToken.None);

        var tool = new UatRunTool(_paths, _validator, _services);
        JsonElement args = JsonSerializer.SerializeToElement(new { spec = "a.toml", replay = "run1\\repro\\repro.toml" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-l16", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("spec changed since the run", result.Error, StringComparison.Ordinal);
    }
}
