using System.Threading;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>Kinds of secret the driver knows how to redact.</summary>
internal enum SecretKind
{
    /// <summary>A single-use agent enrollment token.</summary>
    EnrollmentToken,

    /// <summary>A generated password (database, broker, CA, PFX).</summary>
    Password,
}

/// <summary>A secret found by <see cref="Redactor.Scan"/>: the line number and kind only, never the text.</summary>
internal sealed record SecretHit(int LineNumber, SecretKind Kind);

/// <summary>
/// Replaces known secrets in text with <c>***&lt;kind&gt;***</c> before they reach the console, a log,
/// <c>results.*</c> or the manifest (R-LOG).
/// </summary>
internal sealed class Redactor
{
    private readonly object _gate = new();

    // Replaced as a whole under _gate and read with Volatile.Read, so AddSecret can run while child output is redacted.
    // Longest first: a secret that is a prefix of a longer one must not leave the tail of the longer one in the text.
    private (string Value, SecretKind Kind)[] _secrets = [];

    /// <summary>Registers a secret. Empty values are ignored. Safe to call while another thread redacts.</summary>
    public void AddSecret(string value, SecretKind kind)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        lock (_gate)
        {
            // Insert after every secret at least as long, which keeps the array sorted longest first.
            var current = _secrets;
            var index = 0;
            while (index < current.Length && current[index].Value.Length >= value.Length)
            {
                index++;
            }

            var next = new (string Value, SecretKind Kind)[current.Length + 1];
            current.AsSpan(0, index).CopyTo(next);
            next[index] = (value, kind);
            current.AsSpan(index).CopyTo(next.AsSpan(index + 1));
            _secrets = next;
        }
    }

    /// <summary>Returns <paramref name="line"/> with every registered secret replaced.</summary>
    public string Redact(string line)
    {
        var result = line;
        foreach (var (value, kind) in Volatile.Read(ref _secrets))
        {
            result = result.Replace(value, $"***{kind}***", StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Scans a text file for registered secrets. Reports line numbers and kinds only.
    /// A missing file has no hits; callers check existence first.
    /// </summary>
    public IReadOnlyList<SecretHit> Scan(string filePath)
    {
        var hits = new List<SecretHit>();
        if (!File.Exists(filePath))
        {
            return hits;
        }

        var lineNumber = 0;
        var secrets = Volatile.Read(ref _secrets);
        foreach (var line in File.ReadLines(filePath))
        {
            lineNumber++;
            foreach (var (value, kind) in secrets)
            {
                if (line.Contains(value, StringComparison.Ordinal))
                {
                    hits.Add(new SecretHit(lineNumber, kind));
                }
            }
        }

        return hits;
    }
}
