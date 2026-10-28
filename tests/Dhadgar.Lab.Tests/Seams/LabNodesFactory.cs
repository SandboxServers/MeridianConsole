using Dhadgar.Nodes.Data;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// Hosts the Nodes product in-process for S-L2. Modelled on <c>NodesWebApplicationFactory</c>: InMemory store,
/// dummy RabbitMQ settings, MassTransit removed, background services removed. The lab reaches the same store
/// through <see cref="CreateDbContext"/>.
/// </summary>
public sealed class LabNodesFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"lab-nodes-{Guid.NewGuid()}";
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"lab-nodes-ca-{Guid.NewGuid()}");

    /// <summary>
    /// Creates a <see cref="NodesDbContext"/> on the same InMemory store the hosted Nodes app uses: it takes the app's own
    /// <see cref="DbContextOptions{TContext}"/>, so both share one internal EF provider and one store.
    /// </summary>
    public NodesDbContext CreateDbContext()
    {
        return new NodesDbContext(Services.GetRequiredService<DbContextOptions<NodesDbContext>>());
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_caDirectory))
                {
                    Directory.Delete(_caDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort: a temp directory the OS still holds is left for the temp cleaner.
            }
        }

        base.Dispose(disposing);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // A throwaway CA directory per factory: the CA creates its key and certificate on first enrollment.
        Directory.CreateDirectory(_caDirectory);

        // Dummy RabbitMQ credentials: RabbitMqOptions is [Required]-validated at startup.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Username"] = "lab-rabbit-user",
                ["RabbitMq:Password"] = "lab-rabbit-password",
                ["Nodes:CaStoragePath"] = _caDirectory,
                ["Nodes:CaKeySize"] = "2048"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<NodesDbContext>>();
            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
            services.AddDbContext<NodesDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .UseInternalServiceProvider(efProvider));

            var massTransitDescriptors = services
                .Where(d => d.ServiceType.FullName?.StartsWith("MassTransit", StringComparison.Ordinal) == true ||
                            d.ImplementationType?.FullName?.StartsWith("MassTransit", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in massTransitDescriptors)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IPublishEndpoint>();
            services.AddSingleton<IPublishEndpoint>(new LabNoOpPublisher());

            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// Drops published messages. The lab does not observe the Nodes event bus in S-L2.
/// </summary>
internal sealed class LabNoOpPublisher : IPublishEndpoint
{
    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => new LabNoOpConnectHandle();

    private sealed class LabNoOpConnectHandle : ConnectHandle
    {
        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }
}
