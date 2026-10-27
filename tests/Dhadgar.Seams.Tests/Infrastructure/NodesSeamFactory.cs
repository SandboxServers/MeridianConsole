extern alias nodes;

using nodes::Dhadgar.Nodes.Data;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// Runs the real Nodes host in-process with a real certificate authority (on disk in a per-factory temp
/// directory) and EF InMemory. Only the broker and the database provider are replaced.
/// </summary>
public sealed class NodesSeamFactory : WebApplicationFactory<nodes::Program>
{
    private readonly string _databaseName = $"seams-nodes-{Guid.NewGuid()}";
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"seams-nodes-ca-{Guid.NewGuid()}");

    /// <summary>Messages published by Nodes during the test.</summary>
    public CapturingPublishEndpoint Events { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_caDirectory);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // RabbitMqOptions is validated on start; the bus itself is removed below.
                ["RabbitMq:Username"] = "seams-rabbit-user",
                ["RabbitMq:Password"] = "seams-rabbit-password",
                ["Nodes:CaStoragePath"] = _caDirectory,
                ["Nodes:CaKeySize"] = "2048",
                ["Mtls:Enabled"] = "true"
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
            services.AddSingleton<IPublishEndpoint>(Events);

            services.RemoveAll<IHostedService>();

            // Nodes registers no auth scheme yet (F-01). VS-06 removes this once Nodes registers JWT itself.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            SeamJwt.Apply(services);

            services.AddSingleton<IStartupFilter>(new DbInitStartupFilter());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Directory.Delete(_caDirectory, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup of the temp CA directory
            }
            catch (UnauthorizedAccessException)
            {
                // best-effort cleanup of the temp CA directory
            }
        }
    }

    private sealed class DbInitStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                using var scope = app.ApplicationServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
                db.Database.EnsureCreated();
                next(app);
            };
        }
    }
}
