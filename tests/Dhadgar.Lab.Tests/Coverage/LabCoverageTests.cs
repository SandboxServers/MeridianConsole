using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dhadgar.Lab.Services;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// C15: <c>src/Tools/Dhadgar.Lab/lab-coverage.json</c> names every service project and follows the status rules.
/// The test reads files only under the repo root (R-PATH).
/// </summary>
public sealed class LabCoverageTests
{
    private const string ManifestPath = "src/Tools/Dhadgar.Lab/lab-coverage.json";
    private const string ReadmePath = "docs/analysis/meridian-lab/README.md";
    private const string Real = "real";
    private const string RealPending = "real-pending";
    private const string Stub = "stub";

    private static readonly string[] Statuses = [Real, RealPending, Stub];
    private static readonly string[] SkippedDirs = ["bin", "obj", "node_modules", ".git", ".claude"];
    private static readonly string[] ExcludedTopDirs = ["Tools", "Shared", "Dhadgar.AppHost"];

    private static readonly Regex FaultRow = new(@"^\s*\[\[row\.fault\]\]", RegexOptions.Multiline);

    [Fact]
    public void ManifestIsComplete()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        IReadOnlyList<Entry> entries = ReadManifest(root);

        var duplicates = entries
            .GroupBy(e => e.Project, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, "Projects with more than one entry: " + string.Join(", ", duplicates));

        foreach (Entry entry in entries)
        {
            Assert.True(Array.IndexOf(Statuses, entry.Status) >= 0, $"{entry.Project} has an unknown status '{entry.Status}'.");
        }

        HashSet<string> discovered = DiscoverProjects(root);
        Assert.NotEmpty(discovered);

        var covered = entries.Select(e => e.Project).ToHashSet(StringComparer.Ordinal);
        var missing = discovered.Where(p => !covered.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Projects with no coverage entry: " + string.Join(", ", missing));

        // An entry must still name a project: a folder left behind with only bin/obj (a moved or parked project) does not count (F71).
        var stale = entries
            .Where(e => !discovered.Contains(e.Project) && !HasProjectFile(ResolveUnderRoot(root, e.Project)))
            .Select(e => e.Project)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0, "Manifest entries with no project: " + string.Join(", ", stale));
    }

    [Fact]
    public void RealEntriesHaveAdapterAndFaultRow()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        HashSet<string> adapters = RegisteredAdapterNames();

        foreach (Entry entry in ReadManifest(root).Where(e => e.Status == Real))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Adapter), $"{entry.Project} is real but names no adapter.");
            Assert.True(adapters.Contains(entry.Adapter!), $"{entry.Project} names adapter '{entry.Adapter}', which is not registered.");
            Assert.True(
                string.Equals(Path.GetFileNameWithoutExtension(entry.Catalog), entry.Adapter, StringComparison.Ordinal),
                $"{entry.Project} names adapter '{entry.Adapter}' but catalog '{entry.Catalog}'; the catalog file is named for its adapter.");

            Assert.False(string.IsNullOrWhiteSpace(entry.Catalog), $"{entry.Project} is real but names no catalog.");
            string catalog = ResolveUnderRoot(root, entry.Catalog!);
            Assert.True(File.Exists(catalog), $"{entry.Project} names catalog '{entry.Catalog}', which does not exist.");
            Assert.True(FaultRow.IsMatch(File.ReadAllText(catalog)), $"{entry.Project} catalog '{entry.Catalog}' has no [[row.fault]].");
        }
    }

    [Fact]
    public void PendingEntriesNameAPacket()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string readme = File.ReadAllText(ResolveUnderRoot(root, ReadmePath));

        foreach (Entry entry in ReadManifest(root).Where(e => e.Status == RealPending))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Until), $"{entry.Project} is real-pending but names no 'until'.");

            var packetRow = new Regex(@"^\|\s*" + Regex.Escape(entry.Until!) + @"\s*\|", RegexOptions.Multiline);
            Assert.True(packetRow.IsMatch(readme), $"{entry.Project} names '{entry.Until}', which is not a packet in the README table.");
        }
    }

    [Fact]
    public void StubEntriesHaveNote()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);

        foreach (Entry entry in ReadManifest(root).Where(e => e.Status == Stub))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Note), $"{entry.Project} is a stub with no note.");
        }
    }

    private sealed record Entry(string Project, string Status, string? Adapter, string? Catalog, string? Until, string? Note);

    private static List<Entry> ReadManifest(string root)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ResolveUnderRoot(root, ManifestPath)));
        Assert.Equal(1, document.RootElement.GetProperty("schema").GetInt32());

        var entries = new List<Entry>();
        foreach (JsonElement element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new Entry(
                Field(element, "project") ?? string.Empty,
                Field(element, "status") ?? string.Empty,
                Field(element, "adapter"),
                Field(element, "catalog"),
                Field(element, "until"),
                Field(element, "note")));
        }

        return entries;
    }

    private static string? Field(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The project directories the manifest must cover: every <c>src/**/*.csproj</c> outside Tools, Shared and AppHost
    /// whose SDK is the Web SDK or whose output is an Exe, and every <c>src/Dhadgar.*/package.json</c>. A directory with
    /// both a csproj and a package.json is one entry.
    /// </summary>
    private static HashSet<string> DiscoverProjects(string root)
    {
        string src = Path.Combine(root, "src");
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (string csproj in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(src, csproj).Replace('\\', '/');
            string[] segments = relative.Split('/');
            if (segments.Any(s => Array.IndexOf(SkippedDirs, s) >= 0) || Array.IndexOf(ExcludedTopDirs, segments[0]) >= 0)
            {
                continue;
            }

            if (IsServiceProject(csproj))
            {
                found.Add("src/" + (Path.GetDirectoryName(relative) ?? string.Empty).Replace('\\', '/'));
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(src, "Dhadgar.*", SearchOption.TopDirectoryOnly))
        {
            if (File.Exists(Path.Combine(directory, "package.json")))
            {
                found.Add("src/" + Path.GetFileName(directory));
            }
        }

        return found;
    }

    private static bool HasProjectFile(string directory) =>
        Directory.Exists(directory)
        && (Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any()
            || File.Exists(Path.Combine(directory, "package.json")));

    private static bool IsServiceProject(string csproj)
    {
        XDocument document = XDocument.Load(csproj);
        string? sdk = document.Root?.Attribute("Sdk")?.Value;
        if (sdk is not null && sdk.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.Ordinal))
        {
            return true;
        }

        return document.Descendants("OutputType")
            .Any(e => string.Equals(e.Value.Trim(), "Exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The adapter names the lab ships. Each adapter's <c>Name</c> is a constant override, so an uninitialised instance
    /// reads it without running a constructor (the same approach as <c>DriverToolListTests</c>).
    /// </summary>
    private static HashSet<string> RegisteredAdapterNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Type type in typeof(ILabServiceAdapter).Assembly.GetTypes())
        {
            if (type.IsClass && !type.IsAbstract && typeof(ILabServiceAdapter).IsAssignableFrom(type))
            {
                var adapter = (ILabServiceAdapter)RuntimeHelpers.GetUninitializedObject(type);
                names.Add(adapter.Name);
            }
        }

        return names;
    }

    private static string ResolveUnderRoot(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        Assert.StartsWith(rootFull, full, StringComparison.OrdinalIgnoreCase);
        return full;
    }
}
