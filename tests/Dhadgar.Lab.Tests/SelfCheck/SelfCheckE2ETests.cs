using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.SelfCheck;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The end-to-end self-check of one lab mutant (L55). Needs network for the restore inside the temporary copy.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class SelfCheckE2ETests
{
    [Fact]
    [Trait("RequiresNetwork", "true")]
    public async Task SelfCheck_Lm1_IsCaught()
    {
        string? repoRoot = SelfCheckRunner.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);
        LabPaths paths = LabPaths.Create(new ConfigurationBuilder().Build());
        var options = new SelfCheckOptions("LM1-journal-after-inject", Keep: false, Recovery: false);

        Result<IReadOnlyList<SelfCheckRow>> result = await SelfCheckRunner.RunAsync(
            options,
            repoRoot!,
            paths,
            validator,
            TimeProvider.System,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        SelfCheckRow row = Assert.Single(result.Value!);
        Assert.Equal(SelfCheckJudge.Caught, row.Verdict);
    }
}
