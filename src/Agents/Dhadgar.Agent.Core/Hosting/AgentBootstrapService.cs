using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Hosting;

/// <summary>
/// Startup service (#101): resolves the agent identity (loading it, or enrolling when there is none), persists it,
/// connects to the control plane and renews the certificate before it expires.
/// </summary>
/// <remarks>
/// Fail closed: a state file that cannot be loaded or trusted, or a certificate that names another node, stops
/// the bootstrap without enrolling or connecting (D-S232, D-S233). Every delay goes through <see cref="TimeProvider"/>.
/// </remarks>
public sealed class AgentBootstrapService : BackgroundService
{
    private const int SaveAttempts = 3;
    private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxEnrollmentDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NoTokenWarningInterval = TimeSpan.FromHours(1);

    private readonly IAgentStateStore _store;
    private readonly IAgentIdentityProvider _identity;
    private readonly IEnrollmentService _enrollment;
    private readonly IEnrollmentTokenSource _tokenSource;
    private readonly ICertificateStore _certificateStore;
    private readonly IControlPlaneClient _client;
    private readonly AgentOptions _options;
    private readonly ControlPlaneOptions _cp;
    private readonly SecurityOptions _security;
    private readonly TimeProvider _time;
    private readonly ILogger<AgentBootstrapService> _logger;
    private bool _autoRenewalOffLogged;

    /// <summary>
    /// Creates the bootstrap service.
    /// </summary>
    public AgentBootstrapService(
        IAgentStateStore store,
        IAgentIdentityProvider identity,
        IEnrollmentService enrollment,
        IEnrollmentTokenSource tokenSource,
        ICertificateStore certificateStore,
        IControlPlaneClient client,
        IOptions<AgentOptions> options,
        TimeProvider time,
        ILogger<AgentBootstrapService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _enrollment = enrollment ?? throw new ArgumentNullException(nameof(enrollment));
        _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _cp = _options.ControlPlane;
        _security = _options.Security;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = await ResolveIdentityAsync(stoppingToken);
        if (identity is null)
        {
            return; // stopping, or state that must not be enrolled over (logged Critical)
        }

        var set = _identity.Set(identity);
        if (set.IsFailure)
        {
            _logger.LogCritical("Agent identity could not be set ({Error}); not connecting", set.Error);
            return;
        }

        _logger.LogInformation("Agent identity resolved. NodeId: {NodeId}", identity.NodeId);

        // Check at once on start: a crash after a renewal commit is recovered inside the grace window.
        var nextRenewalCheck = DateTimeOffset.MinValue;
        var checkInterval = TimeSpan.FromSeconds(_cp.ConnectionCheckIntervalSeconds);
        var maxConnectDelay = TimeSpan.FromSeconds(_cp.MaxReconnectDelaySeconds);
        var connectDelay = TimeSpan.FromSeconds(_cp.ReconnectDelaySeconds);
        var rebuildRequired = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = checkInterval;

            // Rebuild the connection unless it is Connected. Reconnecting counts: SignalR's automatic reconnect
            // reuses the certificate it was built with and never gives up (D-S231). After a renewal the rebuild
            // is forced, because a disconnect that threw may have left the hub Connected on the old certificate.
            if (_client.State != ConnectionState.Connected || rebuildRequired)
            {
                var connected = await _client.ConnectAsync(stoppingToken);
                if (!connected.IsSuccess)
                {
                    _logger.LogWarning("Control plane connect failed ({Error}); retrying in {Delay}", connected.Error, connectDelay);
                    if (!await DelayAsync(connectDelay, stoppingToken))
                    {
                        return;
                    }

                    var doubled = connectDelay + connectDelay;
                    connectDelay = doubled < maxConnectDelay ? doubled : maxConnectDelay;
                    continue;
                }

                connectDelay = TimeSpan.FromSeconds(_cp.ReconnectDelaySeconds);
                rebuildRequired = false;
            }

            if (_time.GetUtcNow() >= nextRenewalCheck)
            {
                nextRenewalCheck = _time.GetUtcNow() + TimeSpan.FromMinutes(_cp.RenewalCheckIntervalMinutes);
                if (!_security.EnableCertificateAutoRenewal)
                {
                    if (!_autoRenewalOffLogged)
                    {
                        _logger.LogWarning("Certificate auto-renewal is disabled (Agent:Security:EnableCertificateAutoRenewal=false)");
                        _autoRenewalOffLogged = true;
                    }
                }
                else if (_certificateStore.NeedsRenewal(_security.CertificateRenewalThresholdDays))
                {
                    var renewed = await GuardAsync<CertificateRenewalResult>(
                        () => _enrollment.RenewCertificateAsync(stoppingToken),
                        "Certificate renewal",
                        stoppingToken);

                    // D-S231: the hub pins its certificate per HubConnection, so only a disconnect followed by
                    // ConnectAsync presents the renewed one. The next pass reconnects.
                    if (renewed.IsSuccess)
                    {
                        _logger.LogInformation("Agent certificate renewed; reconnecting with the new certificate");
                        await DisconnectSafelyAsync(stoppingToken);
                        rebuildRequired = true;
                    }
                    else
                    {
                        // D-S212: the response may have been lost after Nodes committed, and the old certificate
                        // renews only inside the grace window, so retry soon rather than after the full interval.
                        _logger.LogError(
                            "Certificate renewal failed ({Error}); retrying in {Minutes} minutes",
                            renewed.Error,
                            _cp.RenewalRetryMinutes);
                        nextRenewalCheck = _time.GetUtcNow() + TimeSpan.FromMinutes(_cp.RenewalRetryMinutes);
                    }
                }
            }
            if (!await DelayAsync(wait, stoppingToken))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Resolves the identity in order: stored state bound to its certificate; the configured seed bound the same way;
    /// otherwise enrollment. Returns null when the agent must not connect.
    /// </summary>
    private async Task<AgentIdentity?> ResolveIdentityAsync(CancellationToken stoppingToken)
    {
        var loaded = await GuardAsync<StoredAgentState>(() => _store.LoadAsync(stoppingToken), "Agent state load", stoppingToken);
        if (loaded.IsFailure)
        {
            _logger.LogCritical(
                "Agent state unusable ({Error}); not enrolling. Remove agent-state.json and supply a new token to recover",
                loaded.Error);
            return null;
        }

        var stored = loaded.Value!.Identity;
        if (stored is not null)
        {
            using var certificate = _certificateStore.GetClientCertificate();
            if (certificate is not null)
            {
                if (AgentCertificateIdentity.HasOnlySpiffeUri(certificate, AgentCertificateProfile.SpiffeId(stored.NodeId)))
                {
                    return stored;
                }

                LogCertificateMismatch(certificate, stored.NodeId);
                return null;
            }

            _logger.LogError("Agent state is present but the client certificate is missing; a new enrollment token is required");
        }
        else if (_options.NodeId is { } seedNodeId && _options.OrganizationId is { } seedOrganizationId)
        {
            // D-S213 seed: configuration may name the node, but only when the certificate names it too.
            using var certificate = _certificateStore.GetClientCertificate();
            if (certificate is not null)
            {
                if (!AgentCertificateIdentity.HasOnlySpiffeUri(certificate, AgentCertificateProfile.SpiffeId(seedNodeId)))
                {
                    LogCertificateMismatch(certificate, seedNodeId);
                    return null;
                }

                var seeded = new AgentIdentity(seedNodeId, seedOrganizationId, _time.GetUtcNow());
                await SaveIdentityAsync(seeded, stoppingToken);
                return seeded;
            }
        }

        return await EnrollUntilSucceededAsync(stoppingToken);
    }

    /// <summary>
    /// Enrolls with the token source's token, re-reading it on every attempt, until enrollment succeeds or the host stops.
    /// Never retries in a tight loop: a missing token waits <c>EnrollmentRetrySeconds</c>, and a failed attempt backs off.
    /// </summary>
    private async Task<AgentIdentity?> EnrollUntilSucceededAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(_cp.EnrollmentRetrySeconds);
        var enrollmentDelay = retryDelay;
        DateTimeOffset? lastNoTokenWarning = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var token = _tokenSource.GetToken();
            if (token.IsFailure || string.IsNullOrWhiteSpace(token.Value))
            {
                var now = _time.GetUtcNow();
                if (lastNoTokenWarning is null || now - lastNoTokenWarning.Value >= NoTokenWarningInterval)
                {
                    if (token.IsFailure)
                    {
                        _logger.LogWarning("Enrollment token could not be read ({Error}); waiting for a token", token.Error);
                    }
                    else
                    {
                        _logger.LogWarning("No enrollment token is configured; waiting for one");
                    }

                    lastNoTokenWarning = now;
                }

                if (!await DelayAsync(retryDelay, stoppingToken))
                {
                    return null;
                }

                continue;
            }

            var enrolled = await GuardAsync<EnrollmentResult>(
                () => _enrollment.EnrollAsync(token.Value!, stoppingToken),
                "Enrollment",
                stoppingToken);
            if (enrolled.IsSuccess)
            {
                var identity = new AgentIdentity(
                    enrolled.Value!.NodeId,
                    enrolled.Value.OrganizationId,
                    _time.GetUtcNow());
                _logger.LogInformation("Enrolled as node {NodeId}", identity.NodeId);
                await SaveIdentityAsync(identity, stoppingToken);
                return identity;
            }

            _logger.LogError("Enrollment failed ({Error}); retrying in {Delay}", enrolled.Error, enrollmentDelay);
            if (!await DelayAsync(enrollmentDelay, stoppingToken))
            {
                return null;
            }

            var doubled = enrollmentDelay + enrollmentDelay;
            enrollmentDelay = doubled < MaxEnrollmentDelay ? doubled : MaxEnrollmentDelay;
        }

        return null;
    }

    /// <summary>
    /// Saves the identity, retrying a failed save. If every attempt fails the agent carries on in memory and logs Critical.
    /// </summary>
    private async Task<bool> SaveIdentityAsync(AgentIdentity identity, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= SaveAttempts; attempt++)
        {
            var saved = await GuardAsync(() => _store.SaveAsync(identity, stoppingToken), "Agent state save", stoppingToken);
            if (saved.IsSuccess)
            {
                return true;
            }

            _logger.LogError(
                "Saving agent state failed on attempt {Attempt} of {Attempts} ({Error})",
                attempt,
                SaveAttempts,
                saved.Error);

            if (attempt < SaveAttempts && !await DelayAsync(SaveRetryDelay, stoppingToken))
            {
                break;
            }
        }

        _logger.LogCritical("Agent identity not persisted; the next restart will need a new enrollment token");
        return false;
    }

    private void LogCertificateMismatch(X509Certificate2 certificate, Guid storedNodeId)
    {
        _logger.LogCritical(
            "[AgentState.CertificateMismatch] Stored node {StoredNodeId} does not match the client certificate's node {CertificateNodeId}; not enrolling and not connecting",
            storedNodeId,
            DescribeCertificateNode(certificate));
    }

    private static string DescribeCertificateNode(X509Certificate2 certificate)
    {
        foreach (var uri in AgentCertificateIdentity.ReadUriNames(certificate))
        {
            if (uri.StartsWith(AgentCertificateProfile.SpiffeNodeIdPrefix, StringComparison.Ordinal)
                && Guid.TryParse(uri.AsSpan(AgentCertificateProfile.SpiffeNodeIdPrefix.Length), out var nodeId))
            {
                return nodeId.ToString("D");
            }
        }

        return "none";
    }

    /// <summary>
    /// Runs an external call and turns an exception into a failure result, so the existing backoff, retry and
    /// Critical paths handle it. Without this a timeout or IO fault would fault <c>ExecuteAsync</c> and stop the host.
    /// Only the exception type is logged: messages can carry paths and URLs.
    /// </summary>
    private async Task<Result<T>> GuardAsync<T>(Func<Task<Result<T>>> call, string operation, CancellationToken stoppingToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError("{Operation} threw {ExceptionType}; treating it as a failure", operation, ex.GetType().Name);
            return Result<T>.Failure($"[Bootstrap.Unexpected] {operation} threw {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Non-generic form of <see cref="GuardAsync{T}"/>, for calls that return a plain <see cref="Result"/>.
    /// </summary>
    private async Task<Result> GuardAsync(Func<Task<Result>> call, string operation, CancellationToken stoppingToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError("{Operation} threw {ExceptionType}; treating it as a failure", operation, ex.GetType().Name);
            return Result.Failure($"[Bootstrap.Unexpected] {operation} threw {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Disconnects after a renewal. A fault here is not fatal: the renewed certificate is already stored, and the
    /// loop forces a rebuild on the next pass.
    /// </summary>
    private async Task DisconnectSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _client.DisconnectAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Disconnect after renewal threw {ExceptionType}; reconnecting on the next pass", ex.GetType().Name);
        }
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, _time, stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}