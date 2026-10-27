using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Security;

/// <summary>
/// agent-state.json gets a protected DACL (SYSTEM, Administrators, the agent's account; no inheritance),
/// and a load trusts the file only when its owner is one of those and nobody else can write it (D-S233).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStateFileSecurity : IStateFileSecurity
{
    private const FileSystemRights WriteLikeRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    private const int GenericWriteOrAll = 0x40000000 | 0x10000000; // GENERIC_WRITE | GENERIC_ALL

    private readonly IReadOnlyList<SecurityIdentifier> _trusted;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsStateFileSecurity"/> class.
    /// The trusted set is SYSTEM, Administrators and the account this process runs as.
    /// </summary>
    public WindowsStateFileSecurity()
    {
        using var current = WindowsIdentity.GetCurrent();
        var sids = new List<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        if (current.User is { } user && !sids.Contains(user))
        {
            sids.Add(user);
        }

        _trusted = sids;
    }

    /// <inheritdoc />
    public Result<FileStream> CreateNew(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in _trusted)
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        FileStream? stream = null;
        try
        {
            stream = new FileInfo(path).Create(
                FileMode.CreateNew,
                FileSystemRights.Write | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize,
                FileShare.None,
                4096,
                System.IO.FileOptions.Asynchronous | System.IO.FileOptions.WriteThrough,
                security);
            var created = Result<FileStream>.Success(stream);
            stream = null; // ownership passes to the caller
            return created;
        }
        catch (IOException ex)
        {
            return Result<FileStream>.Failure($"[AgentState.Io] Failed to create state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<FileStream>.Failure("[AgentState.Io] Access denied creating state file");
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <inheritdoc />
    public Result VerifyTrusted(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !_trusted.Contains(owner))
            {
                return Result.Failure("[AgentState.Untrusted] State file is owned by an untrusted account");
            }

            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var writes = (rule.FileSystemRights & WriteLikeRights) != 0 || ((int)rule.FileSystemRights & GenericWriteOrAll) != 0;
                if (writes && (rule.IdentityReference is not SecurityIdentifier sid || !_trusted.Contains(sid)))
                {
                    return Result.Failure("[AgentState.Untrusted] State file is writable by an untrusted account");
                }
            }

            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"[AgentState.Io] Failed to inspect state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[AgentState.Io] Access denied inspecting state file");
        }
    }
}
