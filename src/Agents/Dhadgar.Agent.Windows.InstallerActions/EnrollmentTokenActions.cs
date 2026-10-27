using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace Dhadgar.Agent.Windows.InstallerActions;

/// <summary>
/// Writes and deletes the enrollment token outside the MSI Registry table, so no MSI log records it (D-S245).
/// Never log the token, its length or any part of it.
/// </summary>
public static class EnrollmentTokenActions
{
    /// <summary>Deferred, no-impersonate, HideTarget. CustomActionData is the raw token.</summary>
    [CustomAction]
    public static ActionResult WriteEnrollmentToken(Session session)
    {
        if (session is null)
        {
            return ActionResult.Failure;
        }

        var token = session["CustomActionData"];
        if (token is null || token.Length == 0)
        {
            session.Log("WriteEnrollmentToken: no token supplied; nothing written.");
            return ActionResult.Success;
        }

        if (!EnrollmentTokenData.IsAcceptable(token))
        {
            session.Log("WriteEnrollmentToken: the token is malformed; nothing written.");
            return ActionResult.Failure;
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(EnrollmentTokenData.KeyPath, writable: true);
            if (key is null)
            {
                session.Log("WriteEnrollmentToken: the agent key is missing.");
                return ActionResult.Failure;
            }

            key.SetValue(EnrollmentTokenData.ValueName, token, RegistryValueKind.String);
            session.Log("WriteEnrollmentToken: token written.");
            return ActionResult.Success;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            session.Log("WriteEnrollmentToken: write failed: " + ex.GetType().Name);
            return ActionResult.Failure;
        }
    }

    /// <summary>Deferred (uninstall) and rollback: deletes the value. A missing key or value is success.</summary>
    [CustomAction]
    public static ActionResult DeleteEnrollmentToken(Session session)
    {
        if (session is null)
        {
            return ActionResult.Failure;
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(EnrollmentTokenData.KeyPath, writable: true);
            key?.DeleteValue(EnrollmentTokenData.ValueName, throwOnMissingValue: false);
            return ActionResult.Success;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            session.Log("DeleteEnrollmentToken: delete failed: " + ex.GetType().Name);
            return ActionResult.Failure;
        }
    }
}
