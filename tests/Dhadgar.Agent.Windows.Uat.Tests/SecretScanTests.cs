using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>U9 secret scan: a planted enrollment token fails the step and is rewritten away; a password hit only warns.</summary>
public sealed class SecretScanTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private const string Token = "uat-enroll-token-0123456789abcdef";
    private const string Password = "uat-db-password-fedcba9876543210";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-scan-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public SecretScanTests()
    {
        _paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(_paths.Logs);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private static CleanupSummary CleanCleanup() => new("clean", 1, [], [], [], []);

    [Fact]
    public void PlantedTokenInAgentLogFailsU9AndTheFileIsRewritten()
    {
        var logPath = Path.Combine(_paths.Logs, "agent.log");
        File.WriteAllText(logPath, $"starting\nenroll token={Token}\ndone\n");
        var redactor = new Redactor();
        redactor.AddSecret(Token, SecretKind.EnrollmentToken);
        var warnings = new List<string>();

        var step = UatRunner.CleanupAndScan(_paths, CleanCleanup(), redactor, warnings);

        Assert.Equal("fail", step.Status);
        Assert.Contains("agent.log line 2", step.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, step.Message ?? string.Empty, StringComparison.Ordinal);
        var rewritten = File.ReadAllText(logPath);
        Assert.DoesNotContain(Token, rewritten, StringComparison.Ordinal);
        Assert.Contains("***EnrollmentToken***", rewritten, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public void PasswordHitOnlyWarnsAndTheFileIsRewritten()
    {
        var logPath = Path.Combine(_paths.Logs, "nodes.log");
        File.WriteAllText(logPath, $"connection Password={Password}\n");
        var redactor = new Redactor();
        redactor.AddSecret(Password, SecretKind.Password);
        var warnings = new List<string>();

        var step = UatRunner.CleanupAndScan(_paths, CleanCleanup(), redactor, warnings);

        Assert.Equal("pass", step.Status);
        Assert.Null(step.Message);
        var warning = Assert.Single(warnings);
        Assert.Contains("nodes.log line 1", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, File.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public void CleanRunFolderPassesU9()
    {
        File.WriteAllText(Path.Combine(_paths.Logs, "agent.log"), "nothing secret here\n");
        var redactor = new Redactor();
        redactor.AddSecret(Token, SecretKind.EnrollmentToken);
        var warnings = new List<string>();

        var step = UatRunner.CleanupAndScan(_paths, CleanCleanup(), redactor, warnings);

        Assert.Equal("pass", step.Status);
        Assert.Empty(warnings);
    }

    [Fact]
    public void UndoFailureFailsU9()
    {
        var redactor = new Redactor();
        var failed = new CleanupSummary(
            "footprint-left", 0, [new CleanupFailure("service", "DhadgarAgent", "access denied")], [], [], []);

        var step = UatRunner.CleanupAndScan(_paths, failed, redactor, new List<string>());

        Assert.Equal("fail", step.Status);
        Assert.Contains("manifest undo had 1 failure", step.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentStateFolderIsDeletedAndTheRootFolderStays()
    {
        Directory.CreateDirectory(_paths.AgentState);
        File.WriteAllText(Path.Combine(_paths.AgentState, "agent-state.json"), "{}");

        UatRunner.CleanupAndScan(_paths, CleanCleanup(), new Redactor(), new List<string>());

        Assert.False(Directory.Exists(_paths.AgentState));
        Assert.True(Directory.Exists(_paths.Logs));
    }
}
