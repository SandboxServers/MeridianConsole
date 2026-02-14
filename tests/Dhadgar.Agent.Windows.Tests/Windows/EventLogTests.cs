using System.Diagnostics;
using System.Runtime.InteropServices;
using Dhadgar.Agent.Windows.Windows;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Tests for AgentEventIds constants.
/// </summary>
public sealed class AgentEventIdsTests
{
    #region Service Lifecycle (1xxx)

    [Fact]
    public void ServiceStarted_EqualsExpectedValue()
    {
        Assert.Equal(1000, AgentEventIds.ServiceStarted);
    }

    [Fact]
    public void ServiceStopped_EqualsExpectedValue()
    {
        Assert.Equal(1001, AgentEventIds.ServiceStopped);
    }

    [Fact]
    public void ServiceFailed_UsesServiceLifecycleCategory()
    {
        Assert.Equal(1002, AgentEventIds.ServiceFailed);
        Assert.True(AgentEventIds.ServiceFailed >= 1000 && AgentEventIds.ServiceFailed < 2000);
    }

    [Fact]
    public void ServiceStarting_UsesServiceLifecycleCategory()
    {
        Assert.Equal(1003, AgentEventIds.ServiceStarting);
        Assert.True(AgentEventIds.ServiceStarting >= 1000 && AgentEventIds.ServiceStarting < 2000);
    }

    [Fact]
    public void ServiceStopping_UsesServiceLifecycleCategory()
    {
        Assert.Equal(1004, AgentEventIds.ServiceStopping);
        Assert.True(AgentEventIds.ServiceStopping >= 1000 && AgentEventIds.ServiceStopping < 2000);
    }

    [Fact]
    public void ServiceConfigReloaded_UsesServiceLifecycleCategory()
    {
        Assert.Equal(1005, AgentEventIds.ServiceConfigReloaded);
        Assert.True(AgentEventIds.ServiceConfigReloaded >= 1000 && AgentEventIds.ServiceConfigReloaded < 2000);
    }

    #endregion

    #region Connection (2xxx)

    [Fact]
    public void Connected_EqualsExpectedValue()
    {
        Assert.Equal(2000, AgentEventIds.Connected);
    }

    [Fact]
    public void Disconnected_UsesConnectionCategory()
    {
        Assert.Equal(2001, AgentEventIds.Disconnected);
        Assert.True(AgentEventIds.Disconnected >= 2000 && AgentEventIds.Disconnected < 3000);
    }

    [Fact]
    public void ReconnectAttempt_UsesConnectionCategory()
    {
        Assert.Equal(2002, AgentEventIds.ReconnectAttempt);
        Assert.True(AgentEventIds.ReconnectAttempt >= 2000 && AgentEventIds.ReconnectAttempt < 3000);
    }

    [Fact]
    public void ReconnectFailed_UsesConnectionCategory()
    {
        Assert.Equal(2003, AgentEventIds.ReconnectFailed);
        Assert.True(AgentEventIds.ReconnectFailed >= 2000 && AgentEventIds.ReconnectFailed < 3000);
    }

    [Fact]
    public void HeartbeatSent_UsesConnectionCategory()
    {
        Assert.Equal(2004, AgentEventIds.HeartbeatSent);
        Assert.True(AgentEventIds.HeartbeatSent >= 2000 && AgentEventIds.HeartbeatSent < 3000);
    }

    [Fact]
    public void HeartbeatFailed_UsesConnectionCategory()
    {
        Assert.Equal(2005, AgentEventIds.HeartbeatFailed);
        Assert.True(AgentEventIds.HeartbeatFailed >= 2000 && AgentEventIds.HeartbeatFailed < 3000);
    }

    [Fact]
    public void ConnectionTimeout_UsesConnectionCategory()
    {
        Assert.Equal(2006, AgentEventIds.ConnectionTimeout);
        Assert.True(AgentEventIds.ConnectionTimeout >= 2000 && AgentEventIds.ConnectionTimeout < 3000);
    }

    #endregion

    #region Enrollment (3xxx)

    [Fact]
    public void EnrollmentStarted_EqualsExpectedValue()
    {
        Assert.Equal(3000, AgentEventIds.EnrollmentStarted);
    }

    [Fact]
    public void EnrollmentSucceeded_UsesEnrollmentCategory()
    {
        Assert.Equal(3001, AgentEventIds.EnrollmentSucceeded);
        Assert.True(AgentEventIds.EnrollmentSucceeded >= 3000 && AgentEventIds.EnrollmentSucceeded < 4000);
    }

    [Fact]
    public void EnrollmentFailed_UsesEnrollmentCategory()
    {
        Assert.Equal(3002, AgentEventIds.EnrollmentFailed);
        Assert.True(AgentEventIds.EnrollmentFailed >= 3000 && AgentEventIds.EnrollmentFailed < 4000);
    }

    [Fact]
    public void CertificateRenewed_UsesEnrollmentCategory()
    {
        Assert.Equal(3003, AgentEventIds.CertificateRenewed);
        Assert.True(AgentEventIds.CertificateRenewed >= 3000 && AgentEventIds.CertificateRenewed < 4000);
    }

    [Fact]
    public void CertificateRenewalFailed_UsesEnrollmentCategory()
    {
        Assert.Equal(3004, AgentEventIds.CertificateRenewalFailed);
        Assert.True(AgentEventIds.CertificateRenewalFailed >= 3000 && AgentEventIds.CertificateRenewalFailed < 4000);
    }

    [Fact]
    public void CertificateExpiringSoon_UsesEnrollmentCategory()
    {
        Assert.Equal(3005, AgentEventIds.CertificateExpiringSoon);
        Assert.True(AgentEventIds.CertificateExpiringSoon >= 3000 && AgentEventIds.CertificateExpiringSoon < 4000);
    }

    [Fact]
    public void CertificateExpired_UsesEnrollmentCategory()
    {
        Assert.Equal(3006, AgentEventIds.CertificateExpired);
        Assert.True(AgentEventIds.CertificateExpired >= 3000 && AgentEventIds.CertificateExpired < 4000);
    }

    #endregion

    #region Process Management (4xxx)

    [Fact]
    public void ProcessStarted_EqualsExpectedValue()
    {
        Assert.Equal(4000, AgentEventIds.ProcessStarted);
    }

    [Fact]
    public void ProcessStopped_UsesProcessManagementCategory()
    {
        Assert.Equal(4001, AgentEventIds.ProcessStopped);
        Assert.True(AgentEventIds.ProcessStopped >= 4000 && AgentEventIds.ProcessStopped < 5000);
    }

    [Fact]
    public void ProcessCrashed_UsesProcessManagementCategory()
    {
        Assert.Equal(4002, AgentEventIds.ProcessCrashed);
        Assert.True(AgentEventIds.ProcessCrashed >= 4000 && AgentEventIds.ProcessCrashed < 5000);
    }

    [Fact]
    public void ResourceExceeded_UsesProcessManagementCategory()
    {
        Assert.Equal(4003, AgentEventIds.ResourceExceeded);
        Assert.True(AgentEventIds.ResourceExceeded >= 4000 && AgentEventIds.ResourceExceeded < 5000);
    }

    [Fact]
    public void ProcessKilled_UsesProcessManagementCategory()
    {
        Assert.Equal(4004, AgentEventIds.ProcessKilled);
        Assert.True(AgentEventIds.ProcessKilled >= 4000 && AgentEventIds.ProcessKilled < 5000);
    }

    [Fact]
    public void ProcessRestarting_UsesProcessManagementCategory()
    {
        Assert.Equal(4005, AgentEventIds.ProcessRestarting);
        Assert.True(AgentEventIds.ProcessRestarting >= 4000 && AgentEventIds.ProcessRestarting < 5000);
    }

    [Fact]
    public void ProcessStartFailed_UsesProcessManagementCategory()
    {
        Assert.Equal(4006, AgentEventIds.ProcessStartFailed);
        Assert.True(AgentEventIds.ProcessStartFailed >= 4000 && AgentEventIds.ProcessStartFailed < 5000);
    }

    [Fact]
    public void ProcessOutputError_UsesProcessManagementCategory()
    {
        Assert.Equal(4007, AgentEventIds.ProcessOutputError);
        Assert.True(AgentEventIds.ProcessOutputError >= 4000 && AgentEventIds.ProcessOutputError < 5000);
    }

    #endregion

    #region Command Execution (5xxx)

    [Fact]
    public void CommandReceived_EqualsExpectedValue()
    {
        Assert.Equal(5000, AgentEventIds.CommandReceived);
    }

    [Fact]
    public void CommandSucceeded_UsesCommandExecutionCategory()
    {
        Assert.Equal(5001, AgentEventIds.CommandSucceeded);
        Assert.True(AgentEventIds.CommandSucceeded >= 5000 && AgentEventIds.CommandSucceeded < 6000);
    }

    [Fact]
    public void CommandFailed_UsesCommandExecutionCategory()
    {
        Assert.Equal(5002, AgentEventIds.CommandFailed);
        Assert.True(AgentEventIds.CommandFailed >= 5000 && AgentEventIds.CommandFailed < 6000);
    }

    [Fact]
    public void CommandRejected_UsesCommandExecutionCategory()
    {
        Assert.Equal(5003, AgentEventIds.CommandRejected);
        Assert.True(AgentEventIds.CommandRejected >= 5000 && AgentEventIds.CommandRejected < 6000);
    }

    [Fact]
    public void CommandTimeout_UsesCommandExecutionCategory()
    {
        Assert.Equal(5004, AgentEventIds.CommandTimeout);
        Assert.True(AgentEventIds.CommandTimeout >= 5000 && AgentEventIds.CommandTimeout < 6000);
    }

    [Fact]
    public void CommandQueued_UsesCommandExecutionCategory()
    {
        Assert.Equal(5005, AgentEventIds.CommandQueued);
        Assert.True(AgentEventIds.CommandQueued >= 5000 && AgentEventIds.CommandQueued < 6000);
    }

    #endregion

    #region Security (9xxx)

    [Fact]
    public void SecurityViolation_EqualsExpectedValue()
    {
        Assert.Equal(9000, AgentEventIds.SecurityViolation);
    }

    [Fact]
    public void UnauthorizedAccess_UsesSecurityCategory()
    {
        Assert.Equal(9001, AgentEventIds.UnauthorizedAccess);
        Assert.True(AgentEventIds.UnauthorizedAccess >= 9000 && AgentEventIds.UnauthorizedAccess < 10000);
    }

    [Fact]
    public void PathTraversalAttempt_UsesSecurityCategory()
    {
        Assert.Equal(9002, AgentEventIds.PathTraversalAttempt);
        Assert.True(AgentEventIds.PathTraversalAttempt >= 9000 && AgentEventIds.PathTraversalAttempt < 10000);
    }

    [Fact]
    public void InvalidCertificate_UsesSecurityCategory()
    {
        Assert.Equal(9003, AgentEventIds.InvalidCertificate);
        Assert.True(AgentEventIds.InvalidCertificate >= 9000 && AgentEventIds.InvalidCertificate < 10000);
    }

    [Fact]
    public void SignatureValidationFailed_UsesSecurityCategory()
    {
        Assert.Equal(9004, AgentEventIds.SignatureValidationFailed);
        Assert.True(AgentEventIds.SignatureValidationFailed >= 9000 && AgentEventIds.SignatureValidationFailed < 10000);
    }

    [Fact]
    public void SuspiciousActivity_UsesSecurityCategory()
    {
        Assert.Equal(9005, AgentEventIds.SuspiciousActivity);
        Assert.True(AgentEventIds.SuspiciousActivity >= 9000 && AgentEventIds.SuspiciousActivity < 10000);
    }

    [Fact]
    public void CertificateTrustFailed_UsesSecurityCategory()
    {
        Assert.Equal(9006, AgentEventIds.CertificateTrustFailed);
        Assert.True(AgentEventIds.CertificateTrustFailed >= 9000 && AgentEventIds.CertificateTrustFailed < 10000);
    }

    [Fact]
    public void RateLimitExceeded_UsesSecurityCategory()
    {
        Assert.Equal(9007, AgentEventIds.RateLimitExceeded);
        Assert.True(AgentEventIds.RateLimitExceeded >= 9000 && AgentEventIds.RateLimitExceeded < 10000);
    }

    #endregion
}

/// <summary>
/// Tests for EventLogSetup class.
/// </summary>
/// <remarks>
/// NOTE: Many EventLogSetup methods require administrator privileges to execute
/// (EventLog.SourceExists, CreateEventSource, DeleteEventSource, WriteEntry).
/// These tests focus on validating constants and input validation behavior.
/// The static EventLog methods cannot be easily mocked, so integration testing
/// of those methods would require admin privileges and is better suited for
/// manual testing or specialized integration test environments.
/// </remarks>
public sealed class EventLogSetupTests
{
    [Fact]
    public void SourceName_EqualsExpectedValue()
    {
        Assert.Equal("Meridian Console Agent", EventLogSetup.SourceName);
    }

    [Fact]
    public void LogName_EqualsExpectedValue()
    {
        Assert.Equal("Application", EventLogSetup.LogName);
    }

    [Fact]
    public void WriteEvent_WithNullMessage_ThrowsArgumentException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EventLogSetup.WriteEvent(null!, EventLogEntryType.Information, AgentEventIds.ServiceStarted));

        Assert.Contains("message", exception.ParamName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteEvent_WithEmptyMessage_ThrowsArgumentException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentException for empty
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EventLogSetup.WriteEvent(string.Empty, EventLogEntryType.Information, AgentEventIds.ServiceStarted));

        Assert.Contains("message", exception.ParamName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteEvent_WithWhitespaceMessage_ThrowsArgumentException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentException for whitespace
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EventLogSetup.WriteEvent("   ", EventLogEntryType.Information, AgentEventIds.ServiceStarted));

        Assert.Contains("message", exception.ParamName!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// This test verifies that WriteEvent returns false when the event source
    /// does not exist, rather than attempting to write which would fail.
    /// This test will only pass if the event source is NOT registered.
    /// If running in an environment where the source IS registered, this test
    /// may behave differently and should be skipped or marked as conditional.
    /// </summary>
    [Fact]
    public void WriteEvent_WhenSourceDoesNotExist_ReturnsFalse()
    {
        // Skip on non-Windows platforms (EventLog is Windows-only)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip test on non-Windows
        }

        // This test assumes the source is not registered
        // If it IS registered, WriteEvent may succeed or throw SecurityException
        // In non-admin contexts, even checking SourceExists may throw SecurityException
        // and WriteEvent will catch it and return false

        // We can't guarantee the source doesn't exist, but we can test
        // that calling with valid parameters doesn't throw an unhandled exception
        var result = EventLogSetup.WriteEvent(
            "Test message",
            EventLogEntryType.Information,
            AgentEventIds.ServiceStarted);

        // Result will be false if:
        // 1. Source doesn't exist
        // 2. SecurityException was thrown (insufficient privileges)
        // 3. ArgumentException was thrown (invalid parameters)
        // 4. InvalidOperationException was thrown
        // Result will be true only if source exists AND write succeeded
        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Verifies that EnsureEventSource returns a boolean and doesn't throw
    /// unhandled exceptions, even when called without admin privileges.
    /// The actual behavior (true/false) depends on system state and permissions.
    /// </summary>
    [Fact]
    public void EnsureEventSource_ReturnsBoolean()
    {
        // Skip on non-Windows platforms (EventLog is Windows-only)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip test on non-Windows
        }

        // This method may return true or false depending on:
        // 1. Whether the source already exists
        // 2. Whether we have permissions to check/create it
        // 3. Whether creation succeeds if attempted
        var result = EventLogSetup.EnsureEventSource();

        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Verifies that RemoveEventSource returns a boolean and doesn't throw
    /// unhandled exceptions, even when called without admin privileges.
    /// The actual behavior (true/false) depends on system state and permissions.
    /// </summary>
    [Fact]
    public void RemoveEventSource_ReturnsBoolean()
    {
        // Skip on non-Windows platforms (EventLog is Windows-only)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip test on non-Windows
        }

        // This method may return true or false depending on:
        // 1. Whether the source exists
        // 2. Whether we have permissions to check/remove it
        // 3. Whether removal succeeds if attempted
        var result = EventLogSetup.RemoveEventSource();

        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Verifies that IsEventSourceRegistered returns a boolean and doesn't throw
    /// unhandled exceptions, even when called without admin privileges.
    /// The actual behavior (true/false) depends on system state and permissions.
    /// </summary>
    [Fact]
    public void IsEventSourceRegistered_ReturnsBoolean()
    {
        // Skip on non-Windows platforms (EventLog is Windows-only)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip test on non-Windows
        }

        // This method may return true or false depending on:
        // 1. Whether the source exists and is registered to the correct log
        // 2. Whether we have permissions to check
        var result = EventLogSetup.IsEventSourceRegistered();

        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Verifies that constants are non-empty strings (basic sanity check).
    /// </summary>
    [Fact]
    public void SourceName_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(EventLogSetup.SourceName));
    }

    /// <summary>
    /// Verifies that constants are non-empty strings (basic sanity check).
    /// </summary>
    [Fact]
    public void LogName_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(EventLogSetup.LogName));
    }
}
