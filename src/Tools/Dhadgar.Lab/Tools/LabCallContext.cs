namespace Dhadgar.Lab.Tools;

/// <summary>
/// Per-call facts the dispatcher and the tool need. <c>Caller</c> is "mcp", "uat:&lt;runId&gt;" or "cli".
/// <c>ToString</c> omits <c>LeaseId</c>.
/// </summary>
public sealed record LabCallContext(string CallId, string? LeaseId, string Caller)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("CallId = ").Append(CallId).Append(", Caller = ").Append(Caller);
        return true;
    }
}
