using System.Globalization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// Logical operation (C18): one attempt by one actor. A retry is a new <see cref="OpId"/> whose CausedBy is the first.
/// Text form: <c>&lt;runId&gt;/&lt;actor&gt;/&lt;seq&gt;</c>.
/// </summary>
public readonly record struct OpId(string RunId, string Actor, long Seq)
{
    /// <summary>
    /// The text form <c>runId/actor/seq</c>.
    /// </summary>
    public override string ToString() => $"{RunId}/{Actor}/{Seq.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Parses the text form. The run id may not contain a separator; the actor must be a valid <see cref="ActorIds"/> value;
    /// the sequence must be a positive integer.
    /// </summary>
    public static Result<OpId> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Result<OpId>.Failure("[Lab.InvalidArgument] op id is empty");
        }

        int last = text.LastIndexOf('/');
        if (last <= 0 || last == text.Length - 1)
        {
            return Result<OpId>.Failure("[Lab.InvalidArgument] op id must be runId/actor/seq");
        }

        string head = text[..last];
        string seqText = text[(last + 1)..];
        int middle = head.LastIndexOf('/');
        if (middle <= 0 || middle == head.Length - 1)
        {
            return Result<OpId>.Failure("[Lab.InvalidArgument] op id must be runId/actor/seq");
        }

        string runId = head[..middle];
        string actor = head[(middle + 1)..];
        if (!long.TryParse(seqText, NumberStyles.None, CultureInfo.InvariantCulture, out long seq) || seq < 1)
        {
            return Result<OpId>.Failure("[Lab.InvalidArgument] op id sequence must be a positive integer");
        }

        Result<string> validActor = ActorIds.Validate(actor);
        if (validActor.IsFailure)
        {
            return Result<OpId>.Failure(validActor.Error);
        }

        return Result<OpId>.Success(new OpId(runId, actor, seq));
    }
}
