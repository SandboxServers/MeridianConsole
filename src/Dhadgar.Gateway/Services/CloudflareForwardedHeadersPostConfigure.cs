using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Dhadgar.Gateway.Services;

/// <summary>
/// Post-configures ForwardedHeadersOptions with Cloudflare IP ranges from the IP service.
/// This runs after the initial Configure, allowing the hosted service to fetch IPs first.
/// </summary>
public class CloudflareForwardedHeadersPostConfigure : IPostConfigureOptions<ForwardedHeadersOptions>
{
    private readonly ICloudflareIpService _cloudflareIpService;
    private readonly IWebHostEnvironment _environment;

    public CloudflareForwardedHeadersPostConfigure(
        ICloudflareIpService cloudflareIpService,
        IWebHostEnvironment environment)
    {
        _cloudflareIpService = cloudflareIpService;
        _environment = environment;
    }

    public void PostConfigure(string? name, ForwardedHeadersOptions options)
    {
        // Add all known Cloudflare IP networks
        foreach (var network in _cloudflareIpService.GetKnownNetworks())
        {
            options.KnownIPNetworks.Add(network);
        }

        // Allow localhost for development
        if (_environment.IsDevelopment())
        {
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        }
    }
}
