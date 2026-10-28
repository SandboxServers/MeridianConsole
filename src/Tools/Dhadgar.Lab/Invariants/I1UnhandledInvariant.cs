using System.Text.RegularExpressions;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// I1: no unhandled exception and no unexplained 5xx (C11). Reads each service's <see cref="LogRing"/> for the window.
/// A 502, 503 or 504 is excused only by a fault in the answering service's impact set (<see cref="FaultImpactMap"/>);
/// a 500 or any other 5xx is never excused.
/// </summary>
public sealed partial class I1UnhandledInvariant : IInvariant
{
    private const int MaxDetailLength = 512;

    private readonly IReadOnlyList<string> _services;
    private readonly Func<string, LogRing?> _ringFor;
    private readonly IFaultHistory _faults;

    /// <summary>
    /// Creates the check over the named services' log rings and the fault history.
    /// </summary>
    public I1UnhandledInvariant(IEnumerable<string> services, Func<string, LogRing?> ringFor, IFaultHistory faults)
    {
        _services = services.ToArray();
        _ringFor = ringFor;
        _faults = faults;
    }

    /// <inheritdoc />
    public string Id => "I1";

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        var violations = new List<InvariantViolation>();
        IReadOnlyList<FaultHistoryEntry> faults = _faults.Snapshot();

        foreach (string service in _services)
        {
            ct.ThrowIfCancellationRequested();
            LogRing? ring = _ringFor(service);
            if (ring is null)
            {
                continue;
            }

            foreach (LogLine line in ring.Snapshot())
            {
                if (line.Timestamp < window.From || line.Timestamp > window.To)
                {
                    continue;
                }

                InvariantViolation? violation = Classify(service, line, faults);
                if (violation is not null)
                {
                    violations.Add(violation);
                }
            }
        }

        return Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Success(violations));
    }

    private static InvariantViolation? Classify(string service, LogLine line, IReadOnlyList<FaultHistoryEntry> faults)
    {
        bool isError = string.Equals(line.Level, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(line.Level, "Critical", StringComparison.OrdinalIgnoreCase);
        bool exceptionLog = isError
            && (line.Category.StartsWith("Microsoft.AspNetCore.Diagnostics.", StringComparison.Ordinal)
                || line.Category.Contains("GlobalExceptionHandler", StringComparison.Ordinal));
        if (exceptionLog || line.Message.Contains("Unhandled exception", StringComparison.Ordinal))
        {
            return Violation(service, "unhandled", line.Timestamp, Truncate(service + ": " + SensitiveText.Redact(line.Message, out _)));
        }

        if (!line.Category.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics", StringComparison.Ordinal))
        {
            return null;
        }

        Match match = RequestFinished().Match(line.Message);
        if (!match.Success || !int.TryParse(match.Groups["status"].ValueSpan, out int status) || status < 500 || status > 599)
        {
            return null;
        }

        string path = PathOf(match.Groups["url"].Value);
        if ((status is 502 or 503 or 504) && faults.Any(fault => FaultImpactMap.Explains(service, path, fault, line.Timestamp)))
        {
            return null;
        }

        return Violation(service, "unexplained-" + status, line.Timestamp,
            Truncate($"{service} answered {status} for {match.Groups["method"].Value} {path}"));
    }

    private static InvariantViolation Violation(string service, string checkId, DateTimeOffset at, string detail) =>
        new("I1", service, checkId, detail, at, null);

    private static string PathOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return uri.AbsolutePath;
        }

        int query = url.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? url[..query] : url;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxDetailLength ? text : text[..MaxDetailLength];

    [GeneratedRegex(@"Request finished (?:HTTP/\S+ )?(?<method>[A-Z]+) (?<url>\S+) - (?<status>\d{3})")]
    private static partial Regex RequestFinished();
}
