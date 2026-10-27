using System.Net;
using System.Net.Sockets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>Loopback ports for one run. Every port the stack uses is allocated here, so <see cref="Infra.LocalUrl"/> accepts it.</summary>
internal static class FreePorts
{
    /// <summary>The RabbitMQ port. <c>RabbitMqOptions</c> has no port setting, so the broker must listen on 5672 (Program.cs:128).</summary>
    public const int RabbitMqPort = 5672;

    /// <summary>Binds a loopback socket to port 0, reads the port the system chose, and releases it.</summary>
    public static Result<int> AllocateLoopbackPort()
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return Result<int>.Success(((IPEndPoint)listener.LocalEndpoint).Port);
            }
            finally
            {
                listener.Stop();
            }
        }
        catch (SocketException ex)
        {
            return Result<int>.Failure($"no loopback port could be allocated: {ex.Message}");
        }
    }
}
