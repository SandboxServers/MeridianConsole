using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// The fingerprint hash is stable under key order, ignores secret values and changes with a lock file.
/// </summary>
public sealed class FingerprintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "l48-fp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void SameInputs_SameHash()
    {
        LabFingerprint first = FingerprintBuilder.Build(Input(secret: "value-one-123"));
        LabFingerprint second = FingerprintBuilder.Build(Input(secret: "value-one-123"));

        Assert.Equal(FingerprintHasher.Sha256(first), FingerprintHasher.Sha256(second));
        Assert.Matches("^[0-9a-f]{64}$", FingerprintHasher.Sha256(first));
    }

    [Fact]
    public void KeyOrder_DoesNotChangeHash()
    {
        var forward = new Dictionary<string, string> { ["gateway"] = "sha-a", ["identity"] = "sha-b" };
        var reverse = new Dictionary<string, string> { ["identity"] = "sha-b", ["gateway"] = "sha-a" };

        LabFingerprint a = FingerprintBuilder.Build(Input(digests: forward));
        LabFingerprint b = FingerprintBuilder.Build(Input(digests: reverse));

        Assert.Equal(FingerprintHasher.Sha256(a), FingerprintHasher.Sha256(b));
    }

    [Fact]
    public void SecretValueChange_DoesNotChangeHash()
    {
        LabFingerprint first = FingerprintBuilder.Build(Input(secret: "value-one-123"));
        LabFingerprint second = FingerprintBuilder.Build(Input(secret: "value-two-456"));

        Assert.Equal(FingerprintHasher.Sha256(first), FingerprintHasher.Sha256(second));
    }

    [Fact]
    public void LockFileChange_ChangesHash()
    {
        string tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "src", "Svc"));
        string lockFile = Path.Combine(tree, "src", "Svc", "packages.lock.json");
        File.WriteAllText(lockFile, "{\"version\":1}");
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);

        Result<string> before = FingerprintBuilder.LockFilesSha256(tree, validator, [_root]);
        File.WriteAllText(lockFile, "{\"version\":2}");
        Result<string> after = FingerprintBuilder.LockFilesSha256(tree, validator, [_root]);

        Assert.True(before.IsSuccess);
        Assert.True(after.IsSuccess);
        Assert.NotEqual(before.Value, after.Value);
    }

    private static FingerprintInput Input(string secret = "unused-secret", IReadOnlyDictionary<string, string>? digests = null)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DB_PASSWORD"] = secret,
            ["LOG_LEVEL"] = "info",
        };

        return new FingerprintInput(
            Commit: "abc123",
            Dirty: false,
            BuildId: "build-1",
            ImageDigests: digests ?? new Dictionary<string, string> { ["gateway"] = "sha-a" },
            LockFilesSha256: "lock-sha",
            ServiceEnvironments: new Dictionary<string, IReadOnlyDictionary<string, string>> { ["identity"] = env },
            SecretKeys: new HashSet<string>(StringComparer.Ordinal) { "DB_PASSWORD" },
            Specs: [new SpecHash("specs/smoke.toml", "spec-sha")],
            DaemonVersion: "1.0.0",
            ToxiproxyVersion: "2.9.0",
            Seed: 7,
            Os: "windows",
            EngineVersions: new Dictionary<string, string> { ["default"] = "27.0" });
    }
}
