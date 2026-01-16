using Microsoft.Extensions.Options;
using Dhadgar.Gateway.Options;

namespace Dhadgar.Gateway.Services;

/// <summary>
/// Background service that periodically refreshes Cloudflare IP ranges.
/// Also performs initial fetch on startup before the application starts accepting requests.
/// </summary>
public class CloudflareIpHostedService : BackgroundService
{
    private readonly ICloudflareIpService _cloudflareIpService;
    private readonly ILogger<CloudflareIpHostedService> _logger;
    private readonly CloudflareOptions _options;

    public CloudflareIpHostedService(
        ICloudflareIpService cloudflareIpService,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareIpHostedService> logger)
    {
        _cloudflareIpService = cloudflareIpService;
        _options = options.Value;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableDynamicFetch)
        {
            _logger.LogInformation("Dynamic Cloudflare IP fetching is disabled. Using fallback ranges only.");
            return;
        }

        // Fetch IPs on startup before accepting requests
        _logger.LogInformation("Fetching initial Cloudflare IP ranges...");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.FetchTimeoutSeconds));

            await _cloudflareIpService.RefreshAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Initial Cloudflare IP fetch timed out after {Timeout}s. Using fallback ranges.",
                _options.FetchTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial Cloudflare IP fetch failed. Using fallback ranges.");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableDynamicFetch)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.RefreshIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await _cloudflareIpService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic Cloudflare IP refresh. Will retry in {Interval}.", interval);
            }
        }
    }
}
