using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Soak;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools.Invariants;
using Dhadgar.Lab.Uat.Eval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Invariants area (L21): the <c>invariants_check</c> tool, the monitor over I1 to I6, the ledger of checkpoints and the
/// <c>invariant</c> clause source. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddInvariantsArea(IServiceCollection services)
    {
        AddLabTool<InvariantsCheckTool>(services);

        services.AddSingleton<InvariantLedger>();
        services.AddSingleton(sp => new InvariantMonitor(sp.GetServices<IInvariant>()));
        services.AddSingleton<IClauseSource, InvariantSource>();

        // Feeds: supervisor exits for I6 (subscribed at daemon start) and the RabbitMQ management reader for I5.
        services.AddSingleton<ProcessExitFeed>();
        services.AddHostedService<InvariantFeedStarter>();
        services.AddSingleton<RabbitMqManagementClient>(sp => new RabbitMqManagementClient(
            sp.GetRequiredService<SecretVault>(),
            sp.GetRequiredService<LabCredentialRefs>()));
        services.AddSingleton<IFaultHistory>(sp => new FaultBookHistory(sp.GetRequiredService<FaultBook>()));

        // The restarts the lab's own calls caused. The dispatcher records them; I6 reads them as excuses.
        services.AddSingleton<LabRestartExcuses>();

        // I1 to I6. I4 is also the raw-line observer: this registration follows L07's NullRawLineObserver, so it is the one resolved.
        services.AddSingleton<IInvariant>(sp => new I1UnhandledInvariant(
            sp.GetRequiredService<LabServices>().ServiceNames,
            name => sp.GetRequiredService<LabProcessSupervisor>().RingFor(name),
            sp.GetRequiredService<IFaultHistory>()));
        services.AddSingleton<IInvariant>(sp => new I2OutboxInvariant(
            I2OutboxInvariant.AllTargets,
            InvariantWiring.SqlQuery(sp.GetRequiredService<LabDb>()),
            sp.GetRequiredService<Redactor>(),
            InvariantWiring.DbGate(sp.GetRequiredService<LabDb>(), sp.GetRequiredService<LabCredentialRefs>())));
        services.AddSingleton<IInvariant>(sp => new I3DbChecksInvariant(
            Path.Combine(sp.GetRequiredService<LabPaths>().RepoRoot, "docs", "guides", "uat-specs", "invariants"),
            InvariantWiring.SqlQuery(sp.GetRequiredService<LabDb>()),
            sp.GetRequiredService<Redactor>(),
            InvariantWiring.DbGate(sp.GetRequiredService<LabDb>(), sp.GetRequiredService<LabCredentialRefs>())));
        services.AddSingleton(sp => new I4SecretLeakInvariant(
            sp.GetRequiredService<SecretVault>(),
            sp.GetRequiredService<Redactor>(),
            InvariantWiring.DaemonTokenPredicate(sp.GetRequiredService<EdgeState>()),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IInvariant>(sp => sp.GetRequiredService<I4SecretLeakInvariant>());
        services.AddSingleton<IRawLineObserver>(sp => sp.GetRequiredService<I4SecretLeakInvariant>());
        services.AddSingleton<IInvariant>(sp =>
        {
            FaultBook book = sp.GetRequiredService<FaultBook>();
            RabbitMqManagementClient rabbit = sp.GetRequiredService<RabbitMqManagementClient>();
            ToxiproxyClient toxiproxy = sp.GetRequiredService<ToxiproxyClient>();
            IDockerCli docker = sp.GetRequiredService<IDockerCli>();
            JobRunner jobs = sp.GetRequiredService<JobRunner>();
            return new I5OrphanInvariant(
                InvariantWiring.LabelledContainers(docker),
                sp.GetRequiredService<ILabTargetRegistry>(),
                rabbit.ListQueuesAsync,
                toxiproxy.ListProxiesAsync,
                book.ListActive,
                InvariantWiring.BackupFolders(sp.GetRequiredService<LabPaths>()),
                InvariantWiring.StackCreated(sp.GetRequiredService<LabCredentialRefs>()),
                // L54 (A2): mutant containers are found by their label and may exist only while a mutation job runs.
                mutantContainers: ct => I5OrphanInvariant.ListMutantContainersAsync(docker, DockerEngine.Linux, ct),
                mutationRunning: () => jobs.List().Any(job => job.Kind == MutationRunJob.Kind && job.State == "running"),
                // L30: member containers are found by their label and must be listed by the pool.
                memberContainers: ct => I5OrphanInvariant.ListMemberContainersAsync(docker, DockerEngine.Linux, ct),
                poolMembers: sp.GetRequiredService<Dhadgar.Lab.Pool.PoolManager>().MemberContainerNames);
        });
        services.AddSingleton<IInvariant>(sp =>
        {
            ProcessExitFeed exits = sp.GetRequiredService<ProcessExitFeed>();
            return new I6RestartInvariant(
                InvariantWiring.Observations(sp.GetRequiredService<ILabTargetRegistry>(), sp.GetRequiredService<IDockerInspector>()),
                InvariantWiring.Excuses(sp.GetRequiredService<IFaultHistory>(), sp.GetRequiredService<LabRestartExcuses>()),
                exits.Snapshot);
        });

        // I7 (L29): the soak's resource trend. Skips outside a soak; the store is written by SoakJob.
        services.AddSingleton<ResourceSampleStore>();
        services.AddSingleton<IInvariant>(sp => new I7ResourceTrendInvariant(sp.GetRequiredService<ResourceSampleStore>(), () => SoakLimits.Load(Path.Combine(sp.GetRequiredService<LabPaths>().RepoRoot, "docs", "guides", "uat-specs", "invariants", "soak.toml"))));
    }
}
