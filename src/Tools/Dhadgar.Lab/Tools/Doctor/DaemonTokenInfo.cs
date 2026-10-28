namespace Dhadgar.Lab.Tools;

/// <summary>
/// The length of the daemon bearer token, registered by the daemon for <c>lab_doctor</c>. It never carries the value.
/// </summary>
public sealed record DaemonTokenInfo(int ByteLength);
