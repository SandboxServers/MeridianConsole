using Dhadgar.Lab.Uat.Spec;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class SpecLoaderTests
{
    private const string FullSpec = """
        schema = 1

        [section]
        id = "pool-section"
        title = "Pool section"
        requires = ["docker"]
        requires_pool = { os = "windows", sku = "servercore", kind = "container" }

        [[row]]
        id = "guarded"
        title = "Guarded row"
        expected = "fault then step"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "${member}" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          id = "read"
          tool = "registry_read"
          args = { member = "${member}" }
          expect_error = "Lab.Unavailable"

          [[row.expect]]
          id = "row-clause"
          text = "row clause"
          source = "step"
          step = "read"
          op = "absent"
          timeout_ms = 0
          required = true
        """;

    [Fact]
    public void LoadText_FullSpec_ParsesSectionFaultPoolAndExpectError()
    {
        SpecLoadOutcome outcome = SpecLoader.LoadText("pool.toml", FullSpec);

        Assert.Empty(outcome.Problems);
        Assert.NotNull(outcome.Spec);
        UatSpec spec = outcome.Spec!;
        Assert.Equal(1, spec.Schema);
        Assert.Equal("pool-section", spec.Section.Id);
        Assert.Equal(["docker"], spec.Section.Requires);
        Assert.NotNull(spec.Section.RequiresPool);
        Assert.True(spec.Section.RequiresPool!.ContainsKey("os"));
        Assert.Single(spec.Rows);

        UatRow row = spec.Rows[0];
        Assert.Equal("guarded", row.Id);
        Assert.Single(row.Faults);
        Assert.Equal("fault_kill", row.Faults[0].Tool);
        Assert.Equal("before_steps", row.Faults[0].Start);
        Assert.Single(row.Steps);
        Assert.Equal("registry_read", row.Steps[0].Tool);
        Assert.Equal("Lab.Unavailable", row.Steps[0].ExpectError);
        Assert.Single(row.Expect);
        Assert.Equal("absent", row.Expect[0].Op);
        Assert.Equal("step", row.Expect[0].Source);
        Assert.True(row.Expect[0].Required);
    }

    [Fact]
    public void LoadText_SameBytes_HaveSameSha256()
    {
        string a = SpecLoader.LoadText("a.toml", FullSpec).Spec!.Sha256;
        string b = SpecLoader.LoadText("a.toml", FullSpec).Spec!.Sha256;

        Assert.False(string.IsNullOrEmpty(a));
        Assert.Equal(a, b);
    }

    [Fact]
    public void LoadText_InvalidToml_ReturnsProblemWithLine_AndNoSpec()
    {
        const string broken = "schema = 1\n\n[section\nid = \"x\"\n";

        SpecLoadOutcome outcome = SpecLoader.LoadText("broken.toml", broken);

        Assert.Null(outcome.Spec);
        Assert.NotEmpty(outcome.Problems);
        Assert.All(outcome.Problems, problem =>
        {
            Assert.Equal("broken.toml", problem.File);
            Assert.True(problem.Line >= 1);
            Assert.Contains("invalid TOML", problem.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LoadText_UnsupportedSchema_ReturnsProblem()
    {
        SpecLoadOutcome outcome = SpecLoader.LoadText("schema.toml", FullSpec.Replace("schema = 1", "schema = 2", StringComparison.Ordinal));

        Assert.Null(outcome.Spec);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("schema must be 1", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadText_MissingSection_ReturnsProblem()
    {
        SpecLoadOutcome outcome = SpecLoader.LoadText("nosection.toml", "schema = 1\n");

        Assert.Null(outcome.Spec);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("[section] table is missing", StringComparison.Ordinal));
    }
}
