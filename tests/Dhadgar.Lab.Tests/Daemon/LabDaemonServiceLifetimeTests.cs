using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Daemon;

/// <summary>
/// R-DI: the daemon's service provider is built with scope and build-time validation, and every registered service resolves.
/// </summary>
[Collection("LabHome")]
public sealed class LabDaemonServiceLifetimeTests
{
    [Fact]
    public async Task Services_BuildWithScopeAndBuildValidation_ResolveEveryLabService()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;

            Assert.NotNull(services.GetRequiredService<LeaseBook>());
            Assert.NotNull(services.GetRequiredService<AuditLog>());
            Assert.NotNull(services.GetRequiredService<SecretVault>());
            Assert.NotNull(services.GetRequiredService<Redactor>());
            Assert.NotNull(services.GetRequiredService<LabToolRegistry>());
            Assert.Equal(8, services.GetServices<ILabTool>().Count());

            using IServiceScope scope = services.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<LabToolDispatcher>());
            Assert.Same(services.GetRequiredService<LeaseBook>(), scope.ServiceProvider.GetRequiredService<LeaseBook>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Token_WithTrailingNewline_IsVaultedTrimmedAndRedacted()
    {
        // TokenValidator trims before it decodes, so the vault must hold the trimmed token or a tool payload leaks it.
        string root = Path.Combine(Path.GetTempPath(), "lab-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token + "\n",
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            Redactor redactor = daemon.Services.GetRequiredService<Redactor>();
            string redacted = redactor.Redact("payload " + token);

            Assert.DoesNotContain(token, redacted, StringComparison.Ordinal);
            Assert.StartsWith("payload ***", redacted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
