using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class ResultsWriterTests
{
    private const string Secret = "s3cr3t-value";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uat-results-" + Guid.NewGuid().ToString("N"));

    public ResultsWriterTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void WriteCleanup_SecretInFailureMessage_IsRedacted()
    {
        var redactor = new Redactor();
        redactor.AddSecret(Secret, SecretKind.Password);
        var cleanup = new CleanupSummary(
            "footprint-left",
            0,
            [new CleanupFailure("x", "y", $"boom {Secret}")],
            [],
            [],
            []);

        try
        {
            var result = ResultsWriter.WriteCleanup(cleanup, _dir, redactor);

            Assert.True(result.IsSuccess);
            var text = File.ReadAllText(Path.Combine(_dir, "cleanup-results.json"));
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            Assert.Contains("***Password***", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Write_SecretInStepEvidence_IsRedactedInJsonAndMarkdown()
    {
        var redactor = new Redactor();
        redactor.AddSecret(Secret, SecretKind.Password);
        var evidence = new Dictionary<string, object?> { ["echo"] = $"value {Secret}" };

        try
        {
            var result = ResultsWriter.Write(Results(new StepResult("s1", "Step", "fail", evidence, "msg", 5), []), _dir, redactor);

            Assert.True(result.IsSuccess);
            AssertNoSecret(Path.Combine(_dir, "results.json"));
            AssertNoSecret(Path.Combine(_dir, "results.md"));
            Assert.Contains("***Password***", File.ReadAllText(Path.Combine(_dir, "results.md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Write_SecretInWarnings_IsRedacted()
    {
        var redactor = new Redactor();
        redactor.AddSecret(Secret, SecretKind.Password);
        var step = new StepResult("s1", "Step", "pass", new Dictionary<string, object?>(), null, 5);

        try
        {
            var result = ResultsWriter.Write(Results(step, [$"warning {Secret}"]), _dir, redactor);

            Assert.True(result.IsSuccess);
            AssertNoSecret(Path.Combine(_dir, "results.json"));
            AssertNoSecret(Path.Combine(_dir, "results.md"));
            Assert.Contains("***Password***", File.ReadAllText(Path.Combine(_dir, "results.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static UatResults Results(StepResult step, IReadOnlyList<string> warnings) => new(
        1,
        "20261027-142233-9f3a",
        "run",
        null,
        DateTime.UtcNow,
        DateTime.UtcNow,
        "abc123",
        false,
        22631,
        "10.0.0",
        "person",
        true,
        "fail",
        1,
        [step],
        CleanupSummary.NotRun(),
        warnings);

    private static void AssertNoSecret(string path)
    {
        var text = File.ReadAllText(path);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
    }
}
