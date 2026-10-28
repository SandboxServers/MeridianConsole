using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Spec;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class SpecValidatorTests
{
    private const string BaseRow = """
        schema = 1

        [section]
        id = "lab-selftest"
        title = "Self-test"

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

    private static readonly LabToolRegistry Registry = new(new ILabTool[] { new RegistryReadTool() });

    public static TheoryData<string, string, string, string> Mutations => new()
    {
        // (name, text to find in the valid base row, replacement, message fragment expected in a problem)
        { "fault tool used as a step", "tool = \"registry_read\"", "tool = \"fault_kill\"", "cannot be a step" },
        { "unknown tool step", "tool = \"registry_read\"", "tool = \"no_such_tool\"", "unknown tool" },
        { "lease_id in args", "args = {}", "args = { lease_id = \"x\" }", "lease_id" },
        { "unknown var", "args = {}", "args = { label = \"${nope}\" }", "unknown var" },
        { "secret outside http body", "args = {}", "args = { label = \"${secret:tok}\" }", "only in http bodies" },
        { "member without requires_pool", "args = {}", "args = { member = \"${member}\" }", "requires_pool" },
        { "since fault_heal with no fault", "op = \"truthy\"", "op = \"truthy\"\n  since = \"fault_heal:x\"", "names no fault" },
        { "since step with no step", "op = \"truthy\"", "op = \"truthy\"\n  since = \"step:nope\"", "names no step" },
        { "depends_on unknown row", "expected = \"present\"", "expected = \"present\"\ndepends_on = [\"nope\"]", "depends_on" },
        { "known_issue malformed", "expected = \"present\"", "expected = \"present\"\nknown_issue = \"ki-bad\"", "known_issue" },
        { "expect_error malformed", "tool = \"registry_read\"", "tool = \"registry_read\"\n  expect_error = \"bogus\"", "expect_error" },
        { "timeout over 300000", "op = \"truthy\"", "op = \"truthy\"\n  timeout_ms = 400000", "timeout_ms" },
        { "approx needs two numbers", "op = \"truthy\"", "op = \"approx\"\n  value = [1]", "wrong shape" },
        { "no required clause", "required = true", "required = false", "required clause" },
        { "source step names no step", "step = \"read\"", "step = \"nope\"", "no step of this row" },
        { "unknown source", "source = \"step\"", "source = \"bogus\"", "unknown source" },
        { "step with neither tool nor wait", "tool = \"registry_read\"", "tool = \"registry_read\"\n  wait_ms = 10", "either a tool step or a pause step" },
        { "fault target without C10 form", "  [[row.step]]", "  [[row.fault]]\n  id = \"f1\"\n  inject = { tool = \"fault_kill\", args = { target = \"zz:1\" } }\n  start = \"before_steps\"\n  heal = \"after_steps\"\n\n  [[row.step]]", "C10 id form" },
        { "fault tool not a C4 fault", "  [[row.step]]", "  [[row.fault]]\n  id = \"f1\"\n  inject = { tool = \"registry_read\", args = {} }\n  start = \"before_steps\"\n  heal = \"after_steps\"\n\n  [[row.step]]", "not a C4 fault tool" },
        { "fault start to unknown step", "  [[row.step]]", "  [[row.fault]]\n  id = \"f1\"\n  inject = { tool = \"fault_kill\", args = { target = \"c:x\" } }\n  start = \"after_step:nope\"\n  heal = \"after_steps\"\n\n  [[row.step]]", "fault start" },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Validate_BrokenRule_ReportsProblem(string name, string find, string replace, string fragment)
    {
        Assert.Contains(find, BaseRow, StringComparison.Ordinal);
        string text = BaseRow.Replace(find, replace, StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("case.toml", text);
        if (loaded.Spec is null)
        {
            // A structural load problem also counts as a refusal; it must still name the file and a line.
            Assert.All(loaded.Problems, p => Assert.True(p.Line >= 1));
            return;
        }

        IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec, text, Registry);
        Assert.True(problems.Count > 0, name + " should be refused");
        Assert.Contains(problems, p => p.Message.Contains(fragment, StringComparison.Ordinal));
        Assert.All(problems, p => Assert.Equal("case.toml", p.File));
    }

    [Fact]
    public void Validate_ValidBaseRow_HasNoProblems()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("base.toml", BaseRow);

        Assert.NotNull(loaded.Spec);
        Assert.Empty(SpecValidator.Validate(loaded.Spec!, BaseRow, Registry));
    }

    [Fact]
    public void Validate_FaultFormsAndCapturedVar_AreAccepted()
    {
        string text = BaseRow
            .Replace("  [[row.step]]", "  [[row.fault]]\n  id = \"f1\"\n  inject = { tool = \"fault_kill\", args = { target = \"c:x\" } }\n  start = \"before_steps\"\n  heal = \"after_steps\"\n\n  [[row.step]]", StringComparison.Ordinal)
            .Replace("op = \"truthy\"", "op = \"truthy\"\n  since = \"fault_heal:f1\"", StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("faults.toml", text);

        Assert.NotNull(loaded.Spec);
        Assert.Empty(SpecValidator.Validate(loaded.Spec!, text, Registry));
    }

    [Fact]
    public void Validate_DbSourceWithDatabase_IsAccepted()
    {
        string text = BaseRow
            .Replace("source = \"step\"", "source = \"db\"", StringComparison.Ordinal)
            .Replace("step = \"read\"", "database = \"nodes\"\n  sql = \"select 1\"", StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("db.toml", text);

        Assert.NotNull(loaded.Spec);
        Assert.Empty(SpecValidator.Validate(loaded.Spec!, text, Registry));
    }

    [Fact]
    public void Validate_DbSourceWithoutDatabase_IsRefused()
    {
        string text = BaseRow
            .Replace("source = \"step\"", "source = \"db\"", StringComparison.Ordinal)
            .Replace("step = \"read\"", "sql = \"select 1\"", StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("db.toml", text);

        Assert.NotNull(loaded.Spec);
        IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec!, text, Registry);
        Assert.Contains(problems, p => p.Message.Contains("needs field database", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("certstore", "store = \"My\"\n  member = \"m1\"")]
    [InlineData("registry", "key = \"x\"\n  member = \"m1\"")]
    [InlineData("state_file", "file = \"x\"\n  member = \"m1\"")]
    public void Validate_WindowsSourceWithMember_IsAccepted(string source, string fields)
    {
        string text = BaseRow
            .Replace("source = \"step\"", "source = \"" + source + "\"", StringComparison.Ordinal)
            .Replace("step = \"read\"", fields, StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("windows.toml", text);

        Assert.NotNull(loaded.Spec);
        Assert.Empty(SpecValidator.Validate(loaded.Spec!, text, Registry));
    }

    [Fact]
    public void Validate_DbSourceWithUnknownField_IsRefused()
    {
        string text = BaseRow
            .Replace("source = \"step\"", "source = \"db\"", StringComparison.Ordinal)
            .Replace("step = \"read\"", "database = \"nodes\"\n  sql = \"select 1\"\n  schema_x = \"y\"", StringComparison.Ordinal);

        SpecLoadOutcome loaded = SpecLoader.LoadText("db.toml", text);

        Assert.NotNull(loaded.Spec);
        IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec!, text, Registry);
        Assert.Contains(problems, p => p.Message.Contains("has no field schema_x", StringComparison.Ordinal));
    }
}
