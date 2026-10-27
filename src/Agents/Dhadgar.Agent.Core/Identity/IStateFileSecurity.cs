using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Creates and checks the agent state file with platform-specific protection (D-S233).
/// Core registers <see cref="PortableStateFileSecurity"/>; the Windows host registers its own before AddAgentCore.
/// </summary>
public interface IStateFileSecurity
{
    /// <summary>
    /// Creates <paramref name="path"/> with FileMode.CreateNew (never opens an existing file), write-through,
    /// accessible only to the agent's account (and SYSTEM and Administrators on Windows). The caller disposes the stream.
    /// </summary>
    Result<FileStream> CreateNew(string path);

    /// <summary>
    /// Success when the existing file at <paramref name="path"/> may be trusted; Failure("[AgentState.Untrusted] …") when
    /// someone other than the agent could have written it; Failure("[AgentState.Io] …") when it cannot be inspected.
    /// </summary>
    Result VerifyTrusted(string path);
}
