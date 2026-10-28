namespace Dhadgar.Lab.Processes;

/// <summary>
/// Sees every captured child line before redaction. The observer never stores the line; L21's I4 uses it to catch a secret
/// the product logged.
/// </summary>
public interface IRawLineObserver
{
    /// <summary>
    /// Called once per raw line, before <see cref="Redactor"/> runs.
    /// </summary>
    /// <param name="process">The supervised process name.</param>
    /// <param name="rawLine">The line as the child wrote it.</param>
    void Observe(string process, ReadOnlySpan<char> rawLine);
}

/// <summary>
/// The default observer: ignores every line.
/// </summary>
public sealed class NullRawLineObserver : IRawLineObserver
{
    /// <inheritdoc />
    public void Observe(string process, ReadOnlySpan<char> rawLine)
    {
    }
}
