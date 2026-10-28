namespace Dhadgar.Lab.Tools;

/// <summary>
/// Per-call facts the dispatcher and the tool need. <c>Caller</c> is "mcp", "uat:&lt;runId&gt;" or "cli".
/// </summary>
public sealed record LabCallContext(string CallId, string? LeaseId, string Caller);
