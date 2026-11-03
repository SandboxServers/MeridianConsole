using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Commands;

/// <summary>
/// Source-level tripwire: every command action in <c>CliApp.cs</c> must return the exit code of its command.
/// </summary>
public sealed class ActionSourceTests
{
    private static readonly Regex AwaitWithoutReturn = new(@"^\s*await \w+\.ExecuteAsync\(", RegexOptions.Compiled);

    [Fact]
    public void EveryAsyncAction_ReturnsItsExitCode()
    {
        var cliAppPath = Path.Combine(FindRepoRoot(), "src", "Dhadgar.Cli", "CliApp.cs");
        var lines = File.ReadAllLines(cliAppPath);

        var offenders = lines
            .Select((line, index) => (line, number: index + 1))
            .Where(item => AwaitWithoutReturn.IsMatch(item.line))
            .Select(item => $"{item.number}: {item.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("each command action must 'return await X.ExecuteAsync(...)'");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln"))
                || Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
