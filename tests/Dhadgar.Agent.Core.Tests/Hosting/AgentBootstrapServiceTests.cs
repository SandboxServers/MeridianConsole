using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Hosting;

public sealed class AgentBootstrapServiceTests
{
    private static readonly Guid NodeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherNodeId = Guid.Parse("66666666-7777-8888-9999-000000000000");
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly AgentIdentity StoredIdentity = new(NodeId, OrganizationId, DateTimeOffset.UtcNow);
    private const string Token = "super-secret-token-value";

    [Fact]
    public async Task StoredIdentityWithCertificate_ConnectsWithoutEnrolling()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        h.Identity.Received().Set(StoredIdentity);
    }

    [Fact]
    public async Task NoIdentity_WithToken_EnrollsSavesSetsIdentityAndConnects()
    {
        var h = new Harness();
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));
        h.Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = NodeId,
                OrganizationId = OrganizationId,
                CertificateExpiry = DateTime.UtcNow.AddDays(90),
            })));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        Received.InOrder(() =>
        {
            h.Enrollment.EnrollAsync(Token, Arg.Any<CancellationToken>());
            h.Store.SaveAsync(Arg.Is<AgentIdentity>(i => i.NodeId == NodeId), Arg.Any<CancellationToken>());
            h.Identity.Set(Arg.Is<AgentIdentity>(i => i.NodeId == NodeId));
            h.Client.ConnectAsync(Arg.Any<CancellationToken>());
        });

        // The token must never reach the log. Non-empty first, so the check cannot pass against a logger that records nothing.
        Assert.NotEmpty(h.Logger.Entries);
        Assert.DoesNotContain(h.Logger.Messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoIdentity_NoToken_DoesNotEnroll_AndRetriesAfterInterval()
    {
        var h = new Harness();
        h.Tokens.GetToken().Returns(Result<string>.Success(string.Empty), Result<string>.Success(Token));
        h.Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = NodeId,
                OrganizationId = OrganizationId,
                CertificateExpiry = DateTime.UtcNow.AddDays(90),
            })));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.Tokens.ReceivedCalls().Any());
            await Task.Delay(50);
            await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

            await h.AdvanceUntilAsync(() => h.ConnectCount >= 1, TimeSpan.FromSeconds(60));
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Enrollment.Received(1).EnrollAsync(Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorruptState_DoesNotEnroll()
    {
        var h = new Harness();
        h.Store.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file is not valid JSON")));
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        // The fixed service returns from ExecuteAsync here; a reverted one loops forever and this wait throws.
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Client.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
        Assert.True(h.Store.ReceivedCalls().Any(), "Store.LoadAsync was not called");
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task IdentityWithoutCertificate_EnrollsAgain()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(null);
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));
        h.Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = NodeId,
                OrganizationId = OrganizationId,
                CertificateExpiry = DateTime.UtcNow.AddDays(90),
            })));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Enrollment.Received(1).EnrollAsync(Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectFailure_BacksOffAndRetries()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.Client.ConnectAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(Result.Failure("[ControlPlane.Test] unreachable")),
            _ => Task.FromResult(Result.Success()));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
            await WaitUntilAsync(() => h.Time.HasPendingTimer);

            // ReconnectDelaySeconds (5) is the first backoff: no second attempt before it elapses.
            h.Time.Advance(TimeSpan.FromSeconds(4));
            await Task.Delay(50);
            Assert.Equal(1, h.ConnectCount);

            h.Time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => h.ConnectCount >= 2);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RenewalDue_RenewsThenReconnects()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);
        h.Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                h.Events.Enqueue("renew");
                return Task.FromResult(Result<CertificateRenewalResult>.Success(RenewalResult()));
            });

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitForRenewalCallsAsync(1);
            await WaitUntilAsync(() => h.ConnectCount >= 2 || h.DisconnectCount >= 1);
            await Task.Delay(50);

            // D-S231: the renewed certificate reaches the hub only through a disconnect and a rebuild.
            await h.AdvanceUntilAsync(() => h.ConnectCount >= 2, TimeSpan.FromSeconds(60));
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        // D-S231 order: renew, then disconnect, then a rebuild. Recorded in one queue because NSubstitute's InOrder
        // does not span separate substitutes here.
        var events = h.Events.ToArray();
        var renewed = Array.IndexOf(events, "renew");
        var disconnected = Array.IndexOf(events, "disconnect", renewed + 1);
        var rebuilt = Array.IndexOf(events, "connect", disconnected + 1);
        Assert.True(renewed >= 0 && disconnected > renewed && rebuilt > disconnected, string.Join(",", events));
    }

    [Fact]
    public async Task ClientReconnecting_IsRebuiltByConnectAsync()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Reconnecting;

        // The fake keeps reporting Reconnecting, as SignalR's automatic reconnect does while it retries.
        h.Client.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            return Task.FromResult(Result.Success());
        });

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(60));
            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Client.Received().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewalFailure_RetriesAfterRetryInterval_NotBefore()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Connected;
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);
        h.Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(Result<CertificateRenewalResult>.Failure("[Renewal.Test] response lost")),
            _ => Task.FromResult(Result<CertificateRenewalResult>.Success(RenewalResult())));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitForRenewalCallsAsync(1);

            // RenewalRetryMinutes (5) - 1: four one-minute passes, still no second call.
            for (var i = 0; i < 4; i++)
            {
                await h.StepPassAsync();
            }

            Assert.Equal(1, h.RenewalCalls);

            await h.StepPassAsync();
            await h.WaitForRenewalCallsAsync(2);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RenewalSuccess_NextCheckAfterFullInterval()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Connected;
        h.Settings.ControlPlane.RenewalCheckIntervalMinutes = 10;
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);
        h.Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                h.Events.Enqueue("renew");
                return Task.FromResult(Result<CertificateRenewalResult>.Success(RenewalResult()));
            });

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitForRenewalCallsAsync(1);

            for (var i = 0; i < 9; i++)
            {
                await h.StepPassAsync();
            }

            Assert.Equal(1, h.RenewalCalls);

            await h.StepPassAsync();
            await h.WaitForRenewalCallsAsync(2);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void ControlPlaneOptions_DefaultRenewalRetry_IsInsideGraceWindow()
    {
        var options = new ControlPlaneOptions();

        Assert.Equal(AgentCertificateRenewalPolicy.AgentRetryMinutes, options.RenewalRetryMinutes);
        Assert.True(options.RenewalRetryMinutes * 4 <= AgentCertificateRenewalPolicy.DefaultGraceMinutes);
    }

    [Fact]
    public async Task SaveFailure_StillConnects_LogsCritical()
    {
        var h = new Harness();
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));
        h.Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = NodeId,
                OrganizationId = OrganizationId,
                CertificateExpiry = DateTime.UtcNow.AddDays(90),
            })));
        h.Store.SaveAsync(Arg.Any<AgentIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Failure("[AgentState.Io] disk full")));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.SaveCount >= 1);
            await h.AdvanceUntilAsync(() => h.SaveCount >= 3, TimeSpan.FromSeconds(2));

            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        Assert.Equal(3, h.SaveCount);
        h.Identity.Received().Set(Arg.Is<AgentIdentity>(i => i.NodeId == NodeId));
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("not persisted", StringComparison.Ordinal));
    }

    [Fact]
    public Task LoadFailure_Io_DoesNotEnroll() =>
        RunLoadFailureAsync("[AgentState.Io] Failed to read agent state file");

    [Fact]
    public Task LoadFailure_Untrusted_DoesNotEnroll() =>
        RunLoadFailureAsync("[AgentState.Untrusted] State file is writable by group or others");

    private static async Task RunLoadFailureAsync(string error)
    {
        var h = new Harness();
        h.Store.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<StoredAgentState>.Failure(error)));
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        // The fixed service returns from ExecuteAsync here; a reverted one loops forever and this wait throws.
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Client.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task LoadThrows_LogsCriticalAndDoesNotEnroll()
    {
        var h = new Harness();
        h.Store.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<Result<StoredAgentState>>(new IOException("disk gone")));
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, h.EnrollCalls);
        await h.Client.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task EnrollmentThrows_BacksOffAndRetries_DoesNotFaultHost()
    {
        var h = new Harness();
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));
        h.Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<Result<EnrollmentResult>>(new TaskCanceledException()));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.EnrollCalls >= 1);
            await WaitUntilAsync(() => h.Time.HasPendingTimer);

            // EnrollmentRetrySeconds (60) is the backoff: no second attempt before it elapses.
            h.Time.Advance(TimeSpan.FromSeconds(59));
            await Task.Delay(50);
            Assert.Equal(1, h.EnrollCalls);

            h.Time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => h.EnrollCalls >= 2);
            Assert.False(sut.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RenewalThrows_RetriesAfterRetryInterval_NotBefore()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Connected;
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);
        h.Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<Result<CertificateRenewalResult>>(new TaskCanceledException()),
            _ => Task.FromResult(Result<CertificateRenewalResult>.Success(RenewalResult())));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitForRenewalCallsAsync(1);

            for (var i = 0; i < 4; i++)
            {
                await h.StepPassAsync();
            }

            Assert.Equal(1, h.RenewalCalls);

            await h.StepPassAsync();
            await h.WaitForRenewalCallsAsync(2);
            Assert.False(sut.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DisconnectThrows_AfterRenewal_StillRebuildsOnNextPass()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Connected;
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);
        h.Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<CertificateRenewalResult>.Success(RenewalResult())));

        // The hub still reports Connected on the old certificate, so only the forced rebuild can present the new one.
        h.Client.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("hub busy")));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitForRenewalCallsAsync(1);
            await h.AdvanceUntilAsync(() => h.ConnectCount >= 1, TimeSpan.FromSeconds(60));
            Assert.False(sut.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StoredIdentity_CertificateForOtherNode_DoesNotEnrollOrConnect()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(OtherNodeId);
        h.Tokens.GetToken().Returns(Result<string>.Success(Token));

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        // The fixed service returns from ExecuteAsync here; a reverted one loops forever and this wait throws.
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        h.Identity.DidNotReceive().Set(Arg.Any<AgentIdentity>());
        await h.Client.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
        Assert.True(h.Certificates.ReceivedCalls().Any(), "The certificate was not checked");
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("[AgentState.CertificateMismatch]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigSeed_CertificateForOtherNode_DoesNotSeed()
    {
        var h = new Harness();
        h.Settings.NodeId = NodeId;
        h.Settings.OrganizationId = OrganizationId;
        h.UseCertificate(OtherNodeId);

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        // The fixed service returns from ExecuteAsync here; a reverted one loops forever and this wait throws.
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        await h.Store.DidNotReceive().SaveAsync(Arg.Any<AgentIdentity>(), Arg.Any<CancellationToken>());
        await h.Client.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfigSeed_MatchingCertificate_SavesAndConnects()
    {
        var h = new Harness();
        h.Settings.NodeId = NodeId;
        h.Settings.OrganizationId = OrganizationId;
        h.UseCertificate(NodeId);

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Store.Received(1).SaveAsync(Arg.Is<AgentIdentity>(i => i.NodeId == NodeId), Arg.Any<CancellationToken>());
        await h.Enrollment.DidNotReceive().EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewalThreshold_ComesFromSecurityOptions()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.Settings.Security.CertificateRenewalThresholdDays = 90;

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => h.ConnectCount >= 1);
            h.Certificates.Received().NeedsRenewal(90);
            h.Certificates.DidNotReceive().NeedsRenewal(AgentCertificateProfile.RenewalThresholdDays);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AutoRenewalDisabled_NeverRenews()
    {
        var h = new Harness();
        h.StoredState(StoredIdentity);
        h.UseCertificate(NodeId);
        h.ClientState = ConnectionState.Connected;
        h.Settings.Security.EnableCertificateAutoRenewal = false;
        h.Certificates.NeedsRenewal(Arg.Any<int>()).Returns(true);

        using var sut = h.Create();
        await sut.StartAsync(CancellationToken.None);
        try
        {
            await h.StepPassAsync();
            await h.StepPassAsync();
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        await h.Enrollment.DidNotReceive().RenewCertificateAsync(Arg.Any<CancellationToken>());
    }

    private static CertificateRenewalResult RenewalResult() =>
        new() { NewExpiry = DateTime.UtcNow.AddDays(90), OldExpiry = DateTime.UtcNow.AddDays(10) };

    /// <summary>A certificate whose only URI SAN is the SPIFFE ID of <paramref name="nodeId"/>, built through the real profile.</summary>
    private static X509Certificate2 CertificateFor(Guid nodeId)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest(
            $"CN={nodeId:D}, O={AgentCertificateProfile.Organization}",
            key,
            HashAlgorithmName.SHA384);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(nodeId)));
        request.CertificateExtensions.Add(san.Build());
        using var self = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
        return X509CertificateLoader.LoadCertificate(self.RawData);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met within 10 seconds.");
            }

            await Task.Delay(5);
        }
    }

    private sealed class CapturingLogger : ILogger<AgentBootstrapService>
    {
        // Written from the service's thread and read by the test, so a concurrent queue, enumerated as a snapshot.
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IEnumerable<string> Messages => Entries.Select(e => e.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// A fake clock that counts the delay timers armed and not yet fired. The bootstrap loop is sequential and parks on
    /// exactly one delay per pass, so a pending timer means the loop has finished the pass and is waiting for the clock.
    /// </summary>
    private sealed class ObservableFakeTimeProvider : FakeTimeProvider
    {
        private int _pendingTimers;

        public ObservableFakeTimeProvider(DateTimeOffset start)
            : base(start)
        {
        }

        public bool HasPendingTimer => Volatile.Read(ref _pendingTimers) > 0;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Counted before the timer is armed so a fire can never observe a count that has not been raised yet.
            Interlocked.Increment(ref _pendingTimers);
            return base.CreateTimer(
                s =>
                {
                    Interlocked.Decrement(ref _pendingTimers);
                    callback(s);
                },
                state,
                dueTime,
                period);
        }
    }

    /// <summary>Substitutes for every bootstrap dependency, plus a clock and counters the tests wait on.</summary>
    private sealed class Harness
    {
        private volatile ConnectionState _clientState = ConnectionState.Disconnected;
        private int _stateReads;

        public Harness()
        {
            Store.LoadAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Result<StoredAgentState>.Success(StoredAgentState.None)));
            Store.SaveAsync(Arg.Any<AgentIdentity>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                return Task.FromResult(Result.Success());
            });
            Identity.Set(Arg.Any<AgentIdentity>()).Returns(Result.Success());
            Tokens.GetToken().Returns(Result<string>.Success(string.Empty));
            Certificates.NeedsRenewal(Arg.Any<int>()).Returns(false);
            Enrollment.EnrollAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Result<EnrollmentResult>.Failure("[Enrollment.Test] not configured")));
            Enrollment.RenewCertificateAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                        return Task.FromResult(Result<CertificateRenewalResult>.Failure("[Renewal.Test] not configured"));
                });

            Client.State.Returns(_ =>
            {
                Interlocked.Increment(ref _stateReads);
                return _clientState;
            });
            Client.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                _clientState = ConnectionState.Connected;
                Events.Enqueue("connect");
                return Task.FromResult(Result.Success());
            });
            Client.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                _clientState = ConnectionState.Disconnected;
                Events.Enqueue("disconnect");
                return Task.CompletedTask;
            });
        }

        public ConcurrentQueue<string> Events { get; } = new();

        public ObservableFakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));

        public IAgentStateStore Store { get; } = Substitute.For<IAgentStateStore>();

        public IAgentIdentityProvider Identity { get; } = Substitute.For<IAgentIdentityProvider>();

        public IEnrollmentService Enrollment { get; } = Substitute.For<IEnrollmentService>();

        public IEnrollmentTokenSource Tokens { get; } = Substitute.For<IEnrollmentTokenSource>();

        public ICertificateStore Certificates { get; } = Substitute.For<ICertificateStore>();

        public IControlPlaneClient Client { get; } = Substitute.For<IControlPlaneClient>();

        public AgentOptions Settings { get; } = new();

        public CapturingLogger Logger { get; } = new();

        public ConnectionState ClientState
        {
            get => _clientState;
            set => _clientState = value;
        }

        /// <summary>Calls recorded on the substitutes, so tests that override a mock's return value still count.</summary>
        public int ConnectCount => Client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IControlPlaneClient.ConnectAsync));

        public int DisconnectCount => Client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IControlPlaneClient.DisconnectAsync));

        public int EnrollCalls => Enrollment.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IEnrollmentService.EnrollAsync));

        public int SaveCount => Store.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAgentStateStore.SaveAsync));

        public int RenewalCalls => Enrollment.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IEnrollmentService.RenewCertificateAsync));

        public AgentBootstrapService Create() => new(
            Store,
            Identity,
            Enrollment,
            Tokens,
            Certificates,
            Client,
            Microsoft.Extensions.Options.Options.Create(Settings),
            Time,
            Logger);

        public void StoredState(AgentIdentity identity) =>
            Store.LoadAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Result<StoredAgentState>.Success(new StoredAgentState(identity))));

        public void UseCertificate(Guid? nodeId) =>
            Certificates.GetClientCertificate().Returns(_ => nodeId is { } id ? CertificateFor(id) : null);

        /// <summary>
        /// Advances the clock one check interval and waits for the bootstrap loop to run that pass and park on its next delay.
        /// The clock moves only once the loop is parked. Advancing earlier can land between the loop's state read and its
        /// delay, and that advance is then lost: the new delay is armed from the advanced time and nothing wakes the loop.
        /// </summary>
        public async Task StepPassAsync()
        {
            await WaitUntilAsync(() => Time.HasPendingTimer);
            var before = Volatile.Read(ref _stateReads);
            Time.Advance(TimeSpan.FromSeconds(Settings.ControlPlane.ConnectionCheckIntervalSeconds));
            await WaitUntilAsync(() => Volatile.Read(ref _stateReads) > before && Time.HasPendingTimer);
        }

        public Task WaitForRenewalCallsAsync(int count) => WaitUntilAsync(() => RenewalCalls >= count);

        /// <summary>
        /// Advances the clock in steps until the condition holds. Unlike a single advance, this is not lost when the
        /// bootstrap has not yet registered its delay timer.
        /// </summary>
        public async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan step)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Condition not met within 10 seconds.");
                }

                Time.Advance(step);
                await Task.Delay(10);
            }
        }
    }
}