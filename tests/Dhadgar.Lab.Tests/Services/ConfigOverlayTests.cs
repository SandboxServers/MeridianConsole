using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Services;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Services;

public sealed class ConfigOverlayTests
{
    [Fact]
    public void Read_SkipsEntriesOutsideTheAllowlistAndWithControlCharacters()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-overlay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = LabPaths.FromRoot(root);
            _ = Directory.CreateDirectory(paths.Config);
            var adapter = new NodesAdapter();

            // A hand-edited overlay: one key outside the C4 allowlist, one allowlisted key whose value carries a newline
            // (it would inject a second variable into the env file).
            File.WriteAllText(
                Path.Combine(paths.Config, $"{adapter.Name}.json"),
                "{\"Evil__Key\":\"x\",\"Logging:LogLevel:Default\":\"a\\nB=c\"}");

            var overlay = new ConfigOverlay(paths, new PathValidator(NullLogger<PathValidator>.Instance));

            Result<IReadOnlyDictionary<string, string>> result = overlay.Read(adapter);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
            Assert.Empty(result.Value!);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the temp root is disposable.
            }
        }
    }
}
