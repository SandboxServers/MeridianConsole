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
    private readonly List<(string Value, SecretKind Kind)> _secrets = [];

    /// <summary>Registers a secret. Empty values are ignored.</summary>
    public void AddSecret(string value, SecretKind kind)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _secrets.Add((value, kind));
        }
    }

    /// <summary>Returns <paramref name="line"/> with every registered secret replaced.</summary>
    public string Redact(string line)
    {
        var result = line;
        foreach (var (value, kind) in _secrets)
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
        foreach (var line in File.ReadLines(filePath))
        {
            lineNumber++;
            foreach (var (value, kind) in _secrets)
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
