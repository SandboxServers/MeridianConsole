using System.Formats.Tar;
using System.Text;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// <c>lab_build {mutant}</c> (L54, A3): exact argument lists through a fake child runner, the stale verdict, and the tar
/// extraction guard. Nothing here starts git, dotnet or docker.
/// </summary>
public sealed class MutantBuilderTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Build8 = "01234567";
    private const string MutantId = "M1-token-no-concurrency";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l54-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly LabPaths _paths;
    private readonly MutantSpec _spec;

    public MutantBuilderTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repo);
        _paths = LabPaths.FromRoot(Path.Combine(_root, "lab"), _repo);
        string folder = Path.Combine(_root, "mutants", MutantId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, MutantSpecLoader.PatchFileName),
            "--- a/src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs\n+++ b/src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs\n");
        _spec = new MutantSpec(
            MutantId,
            MutantKind.Product,
            Commit,
            "Drop the UsedAt concurrency token",
            ["src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs"],
            ["nodes"],
            new MutantDetector("history_run", null, null, "^hist:enrollment"),
            folder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file in a temp folder is left for the OS to reap.
        }
    }

    [Fact]
    public async Task Build_UsesExactArgumentLists_AndTheMutantLabel()
    {
        var runner = new FakeRunner();
        var docker = new RecordingDocker();

        Result<MutantBuildDto> result = await MutantBuilder.BuildAsync(
            _spec, [LabBuilder.ResolveProject("nodes").Value!], _paths, docker, Validator(), new FakeTimeProvider(), runner.RunAsync, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(MutantBuilder.BuiltVerdict, result.Value!.Verdict);
        Assert.Equal("mutant-" + MutantId + "-" + Build8, result.Value.BuildId);
        string buildId = result.Value.BuildId;
        string outputRoot = Path.Combine(_paths.Builds, buildId);
        string tar = Path.Combine(outputRoot, MutantBuilder.ArchiveFileName);
        string source = Path.Combine(outputRoot, MutantBuilder.SourceFolderName);

        Assert.Equal(["-C", _repo, "rev-parse", "HEAD"], runner.Find("rev-parse").Args);
        Assert.Equal(
            ["-C", _repo, "archive", "--format=tar", "--output=" + tar, "HEAD"],
            runner.Find("archive").Args);
        Assert.Equal(source, runner.Find("--check").WorkingDirectory);
        Assert.Equal(
            ["apply", "--check", Path.Combine(_spec.Folder, MutantSpecLoader.PatchFileName)],
            runner.Find("--check").Args);

        CallRecord restore = runner.Find("restore");
        Assert.Contains("--locked-mode", restore.Args);
        Assert.DoesNotContain("-r", restore.Args);
        CallRecord publish = runner.Find("publish");
        Assert.Contains("--no-restore", publish.Args);
        Assert.DoesNotContain("-r", publish.Args);
        Assert.False(File.Exists(tar), "the archive is deleted after extraction");

        Assert.Single(docker.Calls);
        IReadOnlyList<string> dockerArgs = docker.Calls[0];
        Assert.Contains("meridian-lab-mutant=" + MutantId, dockerArgs);
        Assert.Contains("meridian-lab=1", dockerArgs);
        Assert.Contains("meridian-lab/nodes:" + buildId, dockerArgs);
        Assert.Equal(["meridian-lab/nodes:" + buildId], result.Value.Images);
    }

    [Fact]
    public async Task Build_PatchDoesNotApply_IsStale_AndNothingIsBuilt()
    {
        var runner = new FakeRunner { FailCheck = true };
        var docker = new RecordingDocker();

        Result<MutantBuildDto> result = await MutantBuilder.BuildAsync(
            _spec, [LabBuilder.ResolveProject("nodes").Value!], _paths, docker, Validator(), new FakeTimeProvider(), runner.RunAsync, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(MutantBuilder.StaleVerdict, result.Value!.Verdict);
        Assert.NotNull(result.Value.Reason);
        Assert.Empty(result.Value.Images);
        Assert.Empty(docker.Calls);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet");
    }

    [Fact]
    public async Task Build_LabMutant_IsRefusedBeforeAnyProcessRuns()
    {
        var runner = new FakeRunner();
        MutantSpec lab = _spec with { Kind = MutantKind.Lab };

        Result<MutantBuildDto> result = await MutantBuilder.BuildAsync(
            lab, [LabBuilder.ResolveProject("nodes").Value!], _paths, new RecordingDocker(), Validator(), new FakeTimeProvider(), runner.RunAsync, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ExtractArchive_EntryWithDotDot_IsRefused_AndNothingEscapes()
    {
        string tar = Path.Combine(_root, "dotdot.tar");
        WriteTar(tar, new PaxTarEntry(TarEntryType.RegularFile, "../escape.txt") { DataStream = Bytes("x") });
        string destination = Path.Combine(_root, "builds", "mutant-x", MutantBuilder.SourceFolderName);

        Result result = MutantBuilder.ExtractArchive(tar, destination, Validator());

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "builds", "mutant-x", "escape.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "builds", "escape.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void ExtractArchive_RootedEntry_IsRefused()
    {
        string tar = Path.Combine(_root, "rooted.tar");
        WriteTar(tar, new PaxTarEntry(TarEntryType.RegularFile, "/escape.txt") { DataStream = Bytes("x") });

        Result result = MutantBuilder.ExtractArchive(tar, Path.Combine(_root, "dest"), Validator());

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractArchive_LinkEntry_IsRefused()
    {
        string tar = Path.Combine(_root, "link.tar");
        WriteTar(tar, new PaxTarEntry(TarEntryType.SymbolicLink, "src/link") { LinkName = "/etc" });

        Result result = MutantBuilder.ExtractArchive(tar, Path.Combine(_root, "dest"), Validator());

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractArchive_NormalEntries_AreWrittenUnderDestination()
    {
        string tar = Path.Combine(_root, "normal.tar");
        WriteTar(
            tar,
            new PaxTarEntry(TarEntryType.Directory, "src/Dhadgar.Nodes/"),
            new PaxTarEntry(TarEntryType.RegularFile, "src/Dhadgar.Nodes/Dhadgar.Nodes.csproj") { DataStream = Bytes("<Project />") });
        string destination = Path.Combine(_root, "dest");

        Result result = MutantBuilder.ExtractArchive(tar, destination, Validator());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("<Project />", File.ReadAllText(Path.Combine(destination, "src", "Dhadgar.Nodes", "Dhadgar.Nodes.csproj")));
    }

    [Fact]
    public void BuildId_IsMutantIdAndBuild8()
    {
        Assert.Equal("mutant-M5-token-logged-01234567", MutantBuilder.BuildId("M5-token-logged", Commit));
    }

    private static PathValidator Validator() => new(NullLogger<PathValidator>.Instance);

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static void WriteTar(string path, params PaxTarEntry[] entries)
    {
        using FileStream stream = File.Create(path);
        using var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false);
        foreach (PaxTarEntry entry in entries)
        {
            writer.WriteEntry(entry);
        }
    }

    /// <summary>One recorded child process: the file, its exact argument list and its working directory.</summary>
    private sealed record CallRecord(string FileName, IReadOnlyList<string> Args, string WorkingDirectory);

    /// <summary>Answers git, dotnet and the archive without running them. Writes a real tar for <c>git archive</c>.</summary>
    private sealed class FakeRunner
    {
        public List<CallRecord> Calls { get; } = [];

        public bool FailCheck { get; init; }

        public Task<Result<string>> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct)
        {
            Calls.Add(new CallRecord(fileName, args.ToArray(), workingDirectory));
            if (args.Contains("archive"))
            {
                string output = args.Single(arg => arg.StartsWith("--output=", StringComparison.Ordinal));
                Directory.CreateDirectory(Path.GetDirectoryName(output["--output=".Length..])!);
                WriteTar(output["--output=".Length..], new PaxTarEntry(TarEntryType.RegularFile, "README.md") { DataStream = Bytes("readme") });
            }

            if (args.Contains("rev-parse"))
            {
                return Task.FromResult(Result<string>.Success(Commit + "\n"));
            }

            if (FailCheck && args.Contains("--check"))
            {
                return Task.FromResult(Result<string>.Failure("[Lab.ProcessFailed] git exited with code 1"));
            }

            return Task.FromResult(Result<string>.Success(string.Empty));
        }

        /// <summary>The first call whose argument list contains every given token.</summary>
        public CallRecord Find(params string[] tokens) =>
            Calls.First(call => tokens.All(token => call.Args.Contains(token)));
    }

    private sealed class RecordingDocker : IDockerCli
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(0, string.Empty, string.Empty)));
        }
    }
}
