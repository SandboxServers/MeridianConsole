using System.Diagnostics;
using System.Security;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Manages Windows Event Log source registration for the Meridian Console Agent.
/// </summary>
/// <remarks>
/// SECURITY: Event source creation and deletion require administrator privileges.
/// These operations should only be performed during installation/uninstallation.
/// The source and log names are constants to prevent injection attacks.
/// </remarks>
public static class EventLogSetup
{
    /// <summary>
    /// The event source name registered in Windows Event Log.
    /// </summary>
    public const string SourceName = "Meridian Console Agent";

    /// <summary>
    /// The Windows Event Log to write to.
    /// </summary>
    public const string LogName = "Application";

    /// <summary>
    /// Ensures the event source exists in the Windows Event Log.
    /// Creates it if it does not exist.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the source exists or was created successfully;
    /// <c>false</c> if creation failed (usually due to insufficient privileges).
    /// </returns>
    /// <remarks>
    /// SECURITY: This operation requires administrator privileges.
    /// Should be called during service installation, not at runtime.
    /// After creating a new event source, a system restart may be required
    /// for the source to become fully available.
    /// </remarks>
    public static bool EnsureEventSource()
    {
        try
        {
            if (EventLog.SourceExists(SourceName))
            {
                // Verify the source is registered to the correct log
                var existingLogName = EventLog.LogNameFromSourceName(SourceName, ".");
                if (string.Equals(existingLogName, LogName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Source exists but is registered to a different log
                // This requires removal and re-creation, which needs admin privileges
                // and may require a restart to take effect
                return false;
            }

            // Create the event source
            EventLog.CreateEventSource(SourceName, LogName);
            return true;
        }
        catch (SecurityException)
        {
            // Insufficient privileges to check or create event source
            return false;
        }
        catch (ArgumentException)
        {
            // Source name is invalid or already exists for a different log
            return false;
        }
    }

    /// <summary>
    /// Removes the event source from the Windows Event Log.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the source was removed or did not exist;
    /// <c>false</c> if removal failed (usually due to insufficient privileges).
    /// </returns>
    /// <remarks>
    /// SECURITY: This operation requires administrator privileges.
    /// Should be called during service uninstallation.
    /// After removing an event source, a system restart may be required
    /// before a source with the same name can be recreated.
    /// </remarks>
    public static bool RemoveEventSource()
    {
        try
        {
            if (!EventLog.SourceExists(SourceName))
            {
                // Source does not exist, nothing to remove
                return true;
            }

            EventLog.DeleteEventSource(SourceName);
            return true;
        }
        catch (SecurityException)
        {
            // Insufficient privileges to remove event source
            return false;
        }
        catch (ArgumentException)
        {
            // Source name is invalid
            return false;
        }
    }

    /// <summary>
    /// Checks if the event source is properly registered.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the source exists and is registered to the correct log;
    /// <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// This method does not require administrator privileges to check
    /// if the source exists, but may still fail if the user lacks read
    /// access to the registry.
    /// </remarks>
    public static bool IsEventSourceRegistered()
    {
        try
        {
            if (!EventLog.SourceExists(SourceName))
            {
                return false;
            }

            var existingLogName = EventLog.LogNameFromSourceName(SourceName, ".");
            return string.Equals(existingLogName, LogName, StringComparison.OrdinalIgnoreCase);
        }
        catch (SecurityException)
        {
            // Cannot determine - assume not registered
            return false;
        }
    }

    /// <summary>
    /// Writes an event directly to the Windows Event Log.
    /// </summary>
    /// <param name="message">The event message.</param>
    /// <param name="type">The event type (Information, Warning, Error).</param>
    /// <param name="eventId">The event ID from <see cref="AgentEventIds"/>.</param>
    /// <returns>
    /// <c>true</c> if the event was written successfully;
    /// <c>false</c> if writing failed.
    /// </returns>
    /// <remarks>
    /// SECURITY: This method should only be used for critical events during
    /// startup/shutdown when the logging infrastructure is not available.
    /// Normal logging should go through Microsoft.Extensions.Logging.
    /// </remarks>
    public static bool WriteEvent(string message, EventLogEntryType type, int eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        try
        {
            if (!EventLog.SourceExists(SourceName))
            {
                return false;
            }

            EventLog.WriteEntry(SourceName, message, type, eventId);
            return true;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
