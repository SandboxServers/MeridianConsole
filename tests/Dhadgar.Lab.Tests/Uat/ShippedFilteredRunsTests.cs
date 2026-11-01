using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Chaos;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// Every shipped mutant detector and chaos workload that runs <c>uat_run</c> with a row filter passes the filter check, and keeps no
/// row that stands up a stack (D-ML123, F110). A detector or workload runs against a stack someone else started.
/// </summary>
public sealed class ShippedFilteredRunsTests
{
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    [Fact]
    public void Mutant_UatRunDetectors_PassTheRowFilterCheck()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string mutantsRoot = Path.Combine(root, "tools", "lab", "mutants");

        Result<IReadOnlyList<MutantSpec>> loaded = MutantSpecLoader.LoadAll(mutantsRoot, root, _validator, existsAtBase: _ => true);
        Assert.True(loaded.IsSuccess, loaded.Error);

        List<MutantSpec> detectors = loaded.Value!.Where(spec => spec.Detector.Tool == "uat_run").ToList();
        Assert.NotEmpty(detectors);

        foreach (MutantSpec mutant in detectors)
        {
            JsonObject args = mutant.Detector.Args ?? throw new InvalidOperationException(mutant.Id + " has no detector args");
            string specName = args["spec"]!.GetValue<string>();
            IReadOnlyList<string> rows = args["rows"] is JsonArray array
                ? array.Select(node => node!.GetValue<string>()).ToArray()
                : [];

            AssertFilterPasses(root, Path.Combine("docs", "guides", "uat-specs", specName), rows, mutant.Id);
        }
    }

    [Fact]
    public void Chaos_UatRunWorkloads_PassTheRowFilterCheck()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string chaosRoot = Path.Combine(root, "docs", "guides", "uat-specs", "chaos");
        string[] plans = Directory.GetFiles(chaosRoot, "*.toml");
        Assert.NotEmpty(plans);

        foreach (string planPath in plans)
        {
            Result<ChaosPlan> plan = ChaosPlanLoader.LoadFile(planPath, chaosRoot, _validator);
            Assert.True(plan.IsSuccess, plan.Error);

            AssertFilterPasses(root, plan.Value!.Workload.Spec, plan.Value.Workload.Rows, plan.Value.Id);
        }
    }

    private static void AssertFilterPasses(string root, string specPath, IReadOnlyList<string> rows, string owner)
    {
        string text = File.ReadAllText(Path.Combine(root, specPath));
        SpecLoadOutcome outcome = SpecLoader.LoadText(Path.GetFileName(specPath), text);
        Assert.True(outcome.Spec is not null, owner + ": " + specPath + " does not load");

        UatSpec spec = outcome.Spec!;
        Result check = RowFilterCheck.Check(spec, rows);
        Assert.True(check.IsSuccess, owner + ": " + check.Error);

        var kept = new HashSet<string>(rows, StringComparer.Ordinal);
        foreach (UatRow row in spec.Rows.Where(row => kept.Contains(row.Id)))
        {
            foreach (UatStep step in row.Steps)
            {
                string tool = step.Tool ?? string.Empty;
                Assert.False(
                    tool == "stack_up" || tool.StartsWith("svc_", StringComparison.Ordinal),
                    owner + ": kept row " + row.Id + " in " + specPath + " has a stack step " + tool);
            }
        }
    }
}
