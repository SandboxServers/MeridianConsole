using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Agent.Core.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Processes;

/// <summary>
/// Serialises the L07 process tests: they share the <c>MERIDIAN_LAB_HOME</c> environment variable and real child processes.
/// </summary>
[CollectionDefinition("LabProcesses", DisableParallelization = true)]
public sealed class LabProcessesCollection
{
}

/// <summary>
/// Builds a supervisor over a temp lab root, a fake clock, a lease book and a vault. Disposes everything.
/// </summary>
internal sealed class SupervisorHarness : IDisposable
{
    private readonly string _root;
    private readonly string? _previousHome;

    public SupervisorHarness(IRawLineObserver? observer = null, IFaultTargets? faults = null)
    {
        _root = Path.Combine(Path.GetTempPath(), "lab-l07-" + Guid.NewGuid().ToString("N"));
        _previousHome = Environment.GetEnvironmentVariable(LabDefaults.HomeEnvVar);
        Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, _root);
        Paths = LabPaths.Create(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
        Leases = new LeaseBook(Time, new NullLabEvents(), new LeaseOptions());
        Vault = new SecretVault();
        Redactor = new Redactor(Vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        Supervisor = new LabProcessSupervisor(
            new LogPathGuard(Paths, validator),
            Redactor,
            Leases,
            Time,
            observer ?? new NullRawLineObserver(),
            faults ?? new NoFaults());
    }

    public LabPaths Paths { get; }

    public FakeTimeProvider Time { get; }

    public LeaseBook Leases { get; }

    public SecretVault Vault { get; }

    public Redactor Redactor { get; }

    public LabProcessSupervisor Supervisor { get; }

    /// <summary>
    /// The scripted child's assembly, copied next to the test assembly by the project reference.
    /// </summary>
    public static string ChildDll => Path.Combine(AppContext.BaseDirectory, "Dhadgar.Lab.TestChild.dll");

    /// <summary>
    /// A spec that runs the scripted child with the given child arguments.
    /// </summary>
    public static LabProcessSpec ChildSpec(string name, bool relaunch, params string[] childArguments)
    {
        var arguments = new List<string> { ChildDll };
        arguments.AddRange(childArguments);
        return new LabProcessSpec(
            name,
            "dotnet",
            arguments,
            new Dictionary<string, string>(StringComparer.Ordinal),
            AppContext.BaseDirectory,
            null,
            relaunch);
    }

    /// <summary>
    /// Polls a condition on real time until it holds or the timeout passes.
    /// </summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return condition();
    }

    public void Dispose()
    {
        Supervisor.Dispose();
        Vault.Dispose();
        Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, _previousHome);
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A child may still hold a log file for a moment; the temp folder is left for the OS to clear.
        }
    }
}

/// <summary>
/// A fixed set of faulted targets for the watchdog tests.
/// </summary>
internal sealed class FixedFaults : IFaultTargets
{
    private readonly HashSet<string> _targets;

    public FixedFaults(params string[] targets)
    {
        _targets = new HashSet<string>(targets, StringComparer.Ordinal);
    }

    public bool IsFaulted(string targetId) => _targets.Contains(targetId);
}
