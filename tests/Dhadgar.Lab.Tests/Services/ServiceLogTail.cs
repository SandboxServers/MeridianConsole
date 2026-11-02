using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Xunit.Abstractions;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>Writes captured service log lines to test output, redacted, for the tests that run a stack.</summary>
internal static class ServiceLogTail
{
    /// <summary>Formats one captured line as <c>{service}-log {timestamp} {level} {category} {message}</c>, with credential pairs redacted.</summary>
    internal static string Format(string service, LogLine line)
    {
        string message = SensitiveText.Redact(line.Message, out _);
        return $"{service}-log {line.Timestamp:O} {line.Level} {line.Category} {message}";
    }

    /// <summary>Writes the last <paramref name="count"/> captured lines of each service, redacted by <see cref="Format"/>.</summary>
    internal static void Write(ITestOutputHelper output, LabProcessSupervisor supervisor, IEnumerable<string> services, int count)
    {
        foreach (string service in services)
        {
            Result<IReadOnlyList<LogLine>> lines = supervisor.ReadLines(service);
            if (lines.IsFailure)
            {
                output.WriteLine($"{service}-log unavailable: {lines.Error}");
                continue;
            }

            foreach (LogLine line in lines.Value!.TakeLast(count))
            {
                output.WriteLine(Format(service, line));
            }
        }
    }
}
