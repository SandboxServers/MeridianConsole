using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// Logical actor identifiers (C18): who did an operation, independent of process, container or connection.
/// Form: <c>lab | runner | driver | client:&lt;n&gt; | agent:&lt;nnnn&gt; | member:&lt;id&gt; | peer:&lt;name&gt; | tenant:&lt;a|b&gt;</c>.
/// </summary>
public static class ActorIds
{
    /// <summary>
    /// The lab itself (the daemon acting on its own behalf).
    /// </summary>
    public const string Lab = "lab";

    /// <summary>
    /// The UAT runner.
    /// </summary>
    public const string Runner = "runner";

    /// <summary>
    /// The driver of a history or scenario run.
    /// </summary>
    public const string Driver = "driver";

    private static readonly Regex Pattern = new(
        "^(lab|runner|driver|(client|agent|member|peer|tenant):[a-z0-9-]{1,32})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The actor id for client number <paramref name="n"/>.
    /// </summary>
    public static string Client(int n) => $"client:{n}";

    /// <summary>
    /// The actor id for agent number <paramref name="index"/>, zero padded to four digits.
    /// </summary>
    public static string Agent(int index) => $"agent:{index:D4}";

    /// <summary>
    /// Checks an actor id against the C18 pattern. Returns the same text when valid.
    /// </summary>
    public static Result<string> Validate(string actorId)
    {
        if (string.IsNullOrEmpty(actorId) || !Pattern.IsMatch(actorId))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] actor id is not a valid logical actor");
        }

        return Result<string>.Success(actorId);
    }
}
