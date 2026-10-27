using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// Writes and inspects the enrollment token the agent reads from HKLM (P20 U2). Abstracted so legs can run with a fake.
/// </summary>
internal interface IRegistryToken
{
    /// <summary>Records the manifest entries, then writes the token value.</summary>
    Result Write(string token);

    /// <summary>True when the token value exists in <c>HKLM\SOFTWARE\Meridian Console\Agent</c>.</summary>
    bool ValueExists();
}

/// <summary>
/// The registry path the agent reads its enrollment token from (<c>AgentRegistry</c> in the agent). Manifest entries
/// are appended before the change they describe (R-CLEANUP).
/// </summary>
internal sealed class RegistryToken : IRegistryToken
{
    /// <summary>Parent key the driver creates when it is missing.</summary>
    public const string ParentKeyPath = @"SOFTWARE\Meridian Console";

    /// <summary>The agent's key.</summary>
    public const string AgentKeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>The value the agent reads and deletes after a successful enrollment.</summary>
    public const string TokenValueName = "EnrollmentToken";

    private const string HklmHive = "HKLM";

    private readonly CleanupManifest _manifest;

    /// <summary>Creates the writer for one run.</summary>
    public RegistryToken(CleanupManifest manifest)
    {
        _manifest = manifest;
    }

    /// <inheritdoc />
    public Result Write(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Result.Failure("[RegistryToken.Empty] token is empty");
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

            var parentExisted = KeyExists(hklm, ParentKeyPath);
            var agentExisted = KeyExists(hklm, AgentKeyPath);
            var valueExisted = ValueExistsIn(hklm);

            // Manifest first: a failure after this point still leaves the entries to undo.
            var appended = _manifest.Append(new RegistryKeyEntry(HklmHive, ParentKeyPath, parentExisted));
            if (appended.IsFailure)
            {
                return appended;
            }

            appended = _manifest.Append(new RegistryKeyEntry(HklmHive, AgentKeyPath, agentExisted));
            if (appended.IsFailure)
            {
                return appended;
            }

            appended = _manifest.Append(new RegistryValueEntry(AgentKeyPath, TokenValueName, valueExisted));
            if (appended.IsFailure)
            {
                return appended;
            }

            using var agentKey = hklm.CreateSubKey(AgentKeyPath, writable: true);
            agentKey.SetValue(TokenValueName, token, RegistryValueKind.String);
            return Result.Success();
        }
        catch (System.Security.SecurityException)
        {
            return Result.Failure("[RegistryToken.Access] access denied writing the enrollment token");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[RegistryToken.Access] access denied writing the enrollment token");
        }
        catch (IOException)
        {
            return Result.Failure("[RegistryToken.Io] registry write failed");
        }
    }

    /// <inheritdoc />
    public bool ValueExists()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return ValueExistsIn(hklm);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool KeyExists(RegistryKey hklm, string path)
    {
        using var key = hklm.OpenSubKey(path, writable: false);
        return key is not null;
    }

    private static bool ValueExistsIn(RegistryKey hklm)
    {
        using var key = hklm.OpenSubKey(AgentKeyPath, writable: false);
        return key?.GetValue(TokenValueName) is not null;
    }
}
