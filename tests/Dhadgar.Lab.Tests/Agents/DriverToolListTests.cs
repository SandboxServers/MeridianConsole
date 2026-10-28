using System.Reflection;
using System.Runtime.CompilerServices;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;

namespace Dhadgar.Lab.Tests.Agents;

/// <summary>
/// S-L5: the <c>uat-driver</c> agent's frontmatter <c>tools</c> list is the whole tool surface it may call (D-ML18).
/// </summary>
public sealed class DriverToolListTests
{
    private const string McpPrefix = "mcp__meridian-lab__";

    private static readonly string[] NonMcpTools = { "Read", "Edit", "Write", "Bash", "PowerShell", "Agent" };

    private static readonly string[] ForbiddenTools =
    {
        "uat_attest",
        "triage_draft_issue",
        "triage_mark",
        "results_known_issue_set",
        "fault_reset",
    };

    [Fact]
    public void Frontmatter_HasTheAgentFields()
    {
        (IReadOnlyDictionary<string, string> fields, _) = ReadAgent();

        Assert.Equal("uat-driver", fields["name"]);
        Assert.Equal("haiku", fields["model"]);
        Assert.Equal("true", fields["omitClaudeMd"]);
        Assert.Equal("low", fields["effort"]);
        Assert.Equal("50", fields["maxTurns"]);
    }

    [Fact]
    public void Tools_AreMcpToolsOnly()
    {
        string[] tools = ReadTools();

        Assert.NotEmpty(tools);
        Assert.All(tools, tool => Assert.StartsWith(McpPrefix, tool, StringComparison.Ordinal));
        Assert.All(tools, tool => Assert.DoesNotContain(tool, NonMcpTools));
    }

    [Fact]
    public void Tools_ExistInTheRegistry()
    {
        LabToolRegistry registry = BuildRegistry();

        foreach (string tool in ReadTools())
        {
            string name = tool[McpPrefix.Length..];
            Assert.NotNull(registry.Find(name));
        }
    }

    [Fact]
    public void Tools_HaveNoMachineGate()
    {
        foreach (string tool in ReadTools())
        {
            string name = tool[McpPrefix.Length..];
            Assert.NotEqual(LabGate.Machine, ToolPolicy.GateFor(name));
        }
    }

    [Fact]
    public void Tools_ExcludeTheForbiddenSet()
    {
        string[] tools = ReadTools();

        foreach (string name in ForbiddenTools)
        {
            Assert.DoesNotContain(McpPrefix + name, tools);
        }
    }

    [Fact]
    public void Driver_HasNoFaultReset()
    {
        Assert.DoesNotContain(McpPrefix + "fault_reset", ReadTools());
    }

    [Fact]
    public void Body_ForbidsForceOnALease()
    {
        (_, string body) = ReadAgent();

        Assert.Contains("Never use `force` on a lease", body, StringComparison.Ordinal);
    }

    private static string AgentPath =>
        Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), ".claude", "agents", "uat-driver.md");

    private static (IReadOnlyDictionary<string, string> Fields, string Body) ReadAgent()
    {
        string[] lines = File.ReadAllLines(AgentPath);
        Assert.Equal("---", lines[0]);

        int close = Array.IndexOf(lines, "---", 1);
        Assert.True(close > 0, "The frontmatter is not closed.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines[1..close])
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                fields[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"');
            }
        }

        return (fields, string.Join("\n", lines[(close + 1)..]));
    }

    private static string[] ReadTools()
    {
        (IReadOnlyDictionary<string, string> fields, _) = ReadAgent();

        return fields["tools"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The registry the daemon builds, from every concrete lab tool in the assembly. Each tool's <c>Name</c> is a
    /// constant override, so an uninitialised instance reads it without running a constructor.
    /// </summary>
    private static LabToolRegistry BuildRegistry()
    {
        Assembly lab = typeof(LabToolRegistry).Assembly;
        var tools = new List<ILabTool>();

        foreach (Type type in lab.GetTypes())
        {
            if (type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition && typeof(ILabTool).IsAssignableFrom(type))
            {
                tools.Add((ILabTool)RuntimeHelpers.GetUninitializedObject(type));
            }
        }

        return new LabToolRegistry(tools);
    }
}
