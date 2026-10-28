using System.Text;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

public sealed class BundleStagingTests : IDisposable
{
    private static readonly string Row = Path.Combine("rows", "plan-check", "key-held.json");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16g-staging-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public BundleStagingTests()
    {
        _folder = Path.Combine(_root, "20260101-nobuild-stage1");
        _ = Directory.CreateDirectory(Path.Combine(_folder, "rows", "plan-check"));
        File.WriteAllText(Path.Combine(_folder, "run.json"), "old run");
        File.WriteAllText(Path.Combine(_folder, "ledger.md"), "old ledger");
        File.WriteAllText(Path.Combine(_folder, Row), "old row");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Commit_WritesEveryFile()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var texts = new List<(string Relative, string Text)>
        {
            ("run.json", "new run"),
            (Row, "new row"),
            ("ledger.md", "new ledger"),
        };

        Result result = BundleStaging.Commit(_folder, texts, redactor, _validator);

        Assert.False(result.IsFailure, result.Error);
        Assert.Equal("new run", File.ReadAllText(Path.Combine(_folder, "run.json")));
        Assert.Equal("new row", File.ReadAllText(Path.Combine(_folder, Row)));
        Assert.Equal("new ledger", File.ReadAllText(Path.Combine(_folder, "ledger.md")));
        Assert.False(Directory.Exists(Path.Combine(_folder, BundleStaging.FolderName)));
    }

    [Fact]
    public void Recover_MarkerPresent_RollsForward()
    {
        string staging = Path.Combine(_folder, BundleStaging.FolderName);
        _ = Directory.CreateDirectory(Path.Combine(staging, "rows", "plan-check"));
        File.WriteAllText(Path.Combine(staging, "run.json"), "staged run");
        File.WriteAllText(Path.Combine(staging, Row), "staged row");
        File.WriteAllText(Path.Combine(staging, BundleStaging.CommitMarker), string.Empty);

        Result result = BundleStaging.Recover(_folder, _validator);

        Assert.False(result.IsFailure, result.Error);
        Assert.Equal("staged run", File.ReadAllText(Path.Combine(_folder, "run.json")));
        Assert.Equal("staged row", File.ReadAllText(Path.Combine(_folder, Row)));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Recover_NoMarker_RollsBack()
    {
        string staging = Path.Combine(_folder, BundleStaging.FolderName);
        _ = Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "run.json"), "staged run");

        Result result = BundleStaging.Recover(_folder, _validator);

        Assert.False(result.IsFailure, result.Error);
        Assert.Equal("old run", File.ReadAllText(Path.Combine(_folder, "run.json")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Recover_NoStagingFolder_Succeeds()
    {
        Result result = BundleStaging.Recover(_folder, _validator);

        Assert.False(result.IsFailure, result.Error);
        Assert.Equal("old run", File.ReadAllText(Path.Combine(_folder, "run.json")));
    }

    [Fact]
    public void Commit_RecoversAnEarlierInterruptedCommitFirst()
    {
        string staging = Path.Combine(_folder, BundleStaging.FolderName);
        _ = Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "ledger.md"), "stale staged ledger");

        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        Result result = BundleStaging.Commit(_folder, [("run.json", "new run")], redactor, _validator);

        Assert.False(result.IsFailure, result.Error);
        Assert.Equal("new run", File.ReadAllText(Path.Combine(_folder, "run.json")));
        Assert.Equal("old ledger", File.ReadAllText(Path.Combine(_folder, "ledger.md")));
        Assert.False(Directory.Exists(staging));
    }
}
