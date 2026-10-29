using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.SelfCheck;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The crash-recovery mode of <c>selfcheck --recovery</c> (L55). Needs Docker and the user's lab daemon stopped (D-ML90):
/// the child daemon owns the one lab stack and its mutex.
/// </summary>
public sealed class SelfCheckRecoveryTests
{
    [Fact]
    [Trait("RequiresDocker", "true")]
    public async Task Recovery_RestartShowsNoToxicLeaseOrI5Orphan()
    {
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
