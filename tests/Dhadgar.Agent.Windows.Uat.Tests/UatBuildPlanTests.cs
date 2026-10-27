using System.Text.Json;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Checks the build plan that Build-AgentWindowsUat.ps1 prints with -ListSteps (P32, D-S241). The script runs no
/// dotnet call in that mode, so the plan is the only thing read. Windows with pwsh only; Linux CI skips these.
/// </summary>
public sealed class UatBuildPlanTests
{
    private static readonly string AgentOut = Path.Combine(Path.GetTempPath(), "p32-plan", "agent");

    private sealed record PlanStep(string Kind, IReadOnlyList<string> Arguments, string? Path);

    [SkippableTheory]
    [InlineData("preflight", null)]
    [InlineData("cleanup", null)]
    [InlineData("stack-only", null)]
    [InlineData("run", "Process")]
    [InlineData("run", "Service")]
    [InlineData("run", "Msi")]
    public async Task BuildPlan_EveryVerbAndMode_RestoresLockedWithoutRidBeforeEachBuild(string verb, string? mode)
    {
        var pwsh = RequirePwsh();

        var (exitCode, stdOut, stdErr) = await RunPlanAsync(pwsh, verb, mode, IsAgentOutMode(verb, mode) ? AgentOut : null);

        Assert.True(exitCode == 0, $"exit {exitCode}: {stdErr}");
        var plan = ParsePlan(stdOut);
        AssertLockSafe(plan);
        Assert.Contains(plan, s => s.Kind == "dotnet");
    }

    [SkippableFact]
    public async Task BuildPlan_RunProcess_PublishesTheAgentForWinX64IntoAgentOut()
    {
        var pwsh = RequirePwsh();

        var (exitCode, stdOut, stdErr) = await RunPlanAsync(pwsh, "run", "Process", AgentOut);

        Assert.True(exitCode == 0, $"exit {exitCode}: {stdErr}");
        var plan = ParsePlan(stdOut);
        var publish = Assert.Single(plan, s => s.Kind == "dotnet" && s.Arguments[0] == "publish");
        Assert.EndsWith("Dhadgar.Agent.Windows.csproj", publish.Arguments[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("win-x64", ValueAfter(publish.Arguments, "-r"));
        Assert.Equal(AgentOut, ValueAfter(publish.Arguments, "-o"));
        Assert.Contains("--no-restore", publish.Arguments);
    }

    [SkippableFact]
    public async Task BuildPlan_RunMsi_DeletesStaleOutputBeforePublishAndBuildsInstallerLast()
    {
        var pwsh = RequirePwsh();

        var (exitCode, stdOut, stdErr) = await RunPlanAsync(pwsh, "run", "Msi", null);

        Assert.True(exitCode == 0, $"exit {exitCode}: {stdErr}");
        var plan = ParsePlan(stdOut);
        var publishIndex = plan.ToList().FindIndex(s => s.Kind == "dotnet" && s.Arguments[0] == "publish");
        Assert.True(publishIndex >= 0, "the MSI plan must publish the agent");

        var removeIndexes = Enumerable.Range(0, plan.Count).Where(i => plan[i].Kind == "remove").ToList();
        Assert.Equal(2, removeIndexes.Count);
        foreach (var index in removeIndexes)
        {
            Assert.True(index < publishIndex, "stale output must be deleted before the publish");
        }

        Assert.EndsWith(@"bin\Release\net10.0\win-x64\publish", plan[removeIndexes[0]].Path ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"Installer\bin\Release", plan[removeIndexes[1]].Path ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("-o", plan[publishIndex].Arguments);

        var last = plan[^1];
        Assert.Equal("build", last.Arguments[0]);
        Assert.EndsWith("Dhadgar.Agent.Windows.Installer.wixproj", last.Arguments[1], StringComparison.OrdinalIgnoreCase);
    }

    [SkippableTheory]
    [InlineData("preflight")]
    [InlineData("cleanup")]
    public async Task BuildPlan_PreflightAndCleanup_BuildOnlyTheDriver(string verb)
    {
        var pwsh = RequirePwsh();

        var (exitCode, stdOut, stdErr) = await RunPlanAsync(pwsh, verb, null, null);

        Assert.True(exitCode == 0, $"exit {exitCode}: {stdErr}");
        var plan = ParsePlan(stdOut);
        var dotnetSteps = plan.Where(s => s.Kind == "dotnet").ToList();
        Assert.Equal(2, dotnetSteps.Count);
        Assert.Equal("restore", dotnetSteps[0].Arguments[0]);
        Assert.Equal("build", dotnetSteps[1].Arguments[0]);
        foreach (var step in dotnetSteps)
        {
            Assert.EndsWith("Dhadgar.Agent.Windows.Uat.csproj", step.Arguments[1], StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(plan, s => s.Kind == "remove");
    }

    [SkippableFact]
    public async Task BuildPlan_RunProcessWithoutAgentOut_Fails()
    {
        var pwsh = RequirePwsh();

        var (exitCode, stdOut, _) = await RunPlanAsync(pwsh, "run", "Process", null);

        Assert.NotEqual(0, exitCode);
        Assert.False(stdOut.TrimStart().StartsWith('['), "a refused plan must print no JSON array");
    }

    private static string RequirePwsh()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the UAT build plan is checked on Windows only.");
        var pwsh = PwshScript.ResolvePwsh();
        Skip.If(pwsh is null, "pwsh is not on PATH.");
        return pwsh!;
    }

    private static bool IsAgentOutMode(string verb, string? mode) => verb == "run" && mode != "Msi";

    private static Task<(int ExitCode, string StdOut, string StdErr)> RunPlanAsync(string pwsh, string verb, string? mode, string? agentOut)
    {
        var arguments = new List<string> { "-Verb", verb };
        if (mode is not null)
        {
            arguments.Add("-Mode");
            arguments.Add(mode);
        }

        if (agentOut is not null)
        {
            arguments.Add("-AgentOut");
            arguments.Add(agentOut);
        }

        arguments.Add("-ListSteps");

        var script = Path.Combine(PwshScript.FindRepoRoot(), "scripts", "uat", "Build-AgentWindowsUat.ps1");
        return PwshScript.RunAsync(pwsh, script, arguments, TimeSpan.FromMinutes(2));
    }

    private static IReadOnlyList<PlanStep> ParsePlan(string stdOut)
    {
        using var doc = JsonDocument.Parse(stdOut);
        var steps = new List<PlanStep>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var kind = element.GetProperty("Kind").GetString() ?? string.Empty;

            var arguments = new List<string>();
            var argumentsElement = element.GetProperty("Arguments");
            if (argumentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var argument in argumentsElement.EnumerateArray())
                {
                    arguments.Add(argument.GetString() ?? string.Empty);
                }
            }

            var pathElement = element.GetProperty("Path");
            var path = pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString() : null;

            steps.Add(new PlanStep(kind, arguments, path));
        }

        return steps;
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[i + 1];
            }
        }

        Assert.Fail($"'{flag}' has no value in the dotnet arguments");
        return string.Empty;
    }

    private static void AssertLockSafe(IReadOnlyList<PlanStep> plan)
    {
        var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in plan.Where(s => s.Kind == "dotnet"))
        {
            var verb = step.Arguments[0];
            var project = step.Arguments[1];
            switch (verb)
            {
                case "restore":
                    Assert.Contains("--locked-mode", step.Arguments);
                    Assert.DoesNotContain(step.Arguments, a => a is "-r" or "--runtime"
                        || a.Contains("RuntimeIdentifier", StringComparison.OrdinalIgnoreCase));
                    restored.Add(project);
                    break;
                case "build":
                case "publish":
                    Assert.Contains("--no-restore", step.Arguments);
                    Assert.True(restored.Contains(project), $"{verb} {project} has no locked restore before it");
                    break;
                default:
                    Assert.Fail($"unexpected dotnet verb '{verb}'");
                    break;
            }
        }
    }
}
