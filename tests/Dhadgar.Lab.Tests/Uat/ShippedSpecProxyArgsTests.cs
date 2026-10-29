using System.Globalization;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Faults;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// D-ML111: a <c>proxy</c> argument in any shipped spec is a <see cref="ProxyCatalog"/> name, never a target id such as
/// <c>p:nodes-postgres</c>; the fault tools build the target id themselves (<c>FaultTools.cs:116</c>). Reads the repository's spec files; no Docker.
/// </summary>
public sealed class ShippedSpecProxyArgsTests
{
    private static readonly Regex ProxyArgument = new("proxy\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);

    [Fact]
    public void ProxyArgs_AreC10Names()
    {
        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string specs = Path.Combine(repoRoot, "docs", "guides", "uat-specs");
        var names = ProxyCatalog.All.Select(proxy => proxy.Name).ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        int matches = 0;

        foreach (string file in Directory.EnumerateFiles(specs, "*.toml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                foreach (Match match in ProxyArgument.Matches(lines[index]))
                {
                    matches++;
                    string value = match.Groups[1].Value;
                    if (!names.Contains(value))
                    {
                        failures.Add(Path.GetRelativePath(repoRoot, file) + ":" + (index + 1).ToString(CultureInfo.InvariantCulture) + " " + value);
                    }
                }
            }
        }

        Assert.True(matches > 0, "no proxy argument was found under " + specs);
        Assert.True(failures.Count == 0, "proxy arguments that are not ProxyCatalog names: " + string.Join("; ", failures));
    }
}
