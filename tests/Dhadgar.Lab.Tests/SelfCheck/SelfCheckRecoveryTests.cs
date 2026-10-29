using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.SelfCheck;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The crash-recovery mode of <c>selfcheck --recovery</c> (L55). Needs Docker and the user's lab daemon stopped (D-ML90):
/// the child daemon owns the one lab stack and its mutex.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class SelfCheckRecoveryTests
{
    [SkippableFact]
    [Trait("RequiresDocker", "true")]
    public async Task Recovery_RestartShowsNoToxicLeaseOrI5Orphan()
    {
        // D-ML102: the recovery child is a real daemon and refuses to start elevated. A skip is UNVERIFIED, never a pass.
        Skip.If(Dhadgar.Lab.Daemon.ElevationCheck.IsElevated(), "selfcheck --recovery needs a non-elevated session (D-ML102)");

        string? repoRoot = SelfCheckRunner.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);
        Result<IReadOnlyList<string>> result = await SelfCheckRecovery.RunAsync(
            repoRoot!,
            validator,
            TimeProvider.System,
            keep: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!);
    }
}
