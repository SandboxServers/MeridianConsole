using Dhadgar.Lab.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Notify stack area (L36, D-ML65): the Notifications and Discord adapters and the webhook sink. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddNotifyStackArea(IServiceCollection services)
    {
        services.AddSingleton<ILabServiceAdapter, NotificationsLabAdapter>();
        services.AddSingleton<ILabServiceAdapter, DiscordLabAdapter>();
        services.AddSingleton<ILabServiceAdapter, SinkLabAdapter>();
    }
}
