using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Triage;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Triage;

/// <summary>
/// A results service wired with the triage observer and a draft writer over a temp lab root. Shared by the triage tool tests.
/// </summary>
internal sealed class TriageToolsFixture : IDisposable
{
    public const string PlantedSecret = "planted-vault-value-7731";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-triage-tools-" + Path.GetRandomFileName());
    private readonly ServiceProvider _services;

    public TriageToolsFixture()
    {
        UatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(UatRuns);
        Paths = LabPaths.FromRoot(_root, repoRoot: "");
        Validator = new PathValidator(NullLogger<PathValidator>.Instance);
        Vault = new SecretVault();
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
        var redactor = new Redactor(Vault);
        _services = new ServiceCollection()
            .AddSingleton(sp => new ResultsService(Paths, Validator, redactor, sp, NullLoggerFactory.Instance, Time))
            .AddSingleton<IIngestObserver>(sp => new TriageService(sp.GetRequiredService<ResultsService>(), Paths, Validator,
                NullLogger<TriageService>.Instance, Time))
            .BuildServiceProvider();
        Results = _services.GetRequiredService<ResultsService>();
        Writer = new DraftWriter(Results, Paths, Validator, new SecretScanner(Vault), Time);
    }

    public string UatRuns { get; }

    public LabPaths Paths { get; }

    public PathValidator Validator { get; }

    public SecretVault Vault { get; }

    public FakeTimeProvider Time { get; }

    public ResultsService Results { get; }

    public DraftWriter Writer { get; }

    /// <summary>Plants a signature, one row and a run whose signature text is <paramref name="text"/>.</summary>
    public async Task PlantSignatureAsync(string signatureId, string text, long seed = 3)
    {
        Result<bool> done = await Results.ReadAsync(db =>
        {
            Assert.True(db.UpsertRun(new RunRecord("run-plant", "uat", null, seed, "build-9", null, null, null, null, "fail", null, null,
                "C:\\bundle")).IsSuccess);
            Assert.True(db.InsertRow(new RowRecord("run-plant", "catalog-nodes", "row-p", "fail", null, seed, null, signatureId,
                "[\"" + signatureId + "\"]", null, null, null)).IsSuccess);
            Assert.True(db.UpsertSignature(new SignatureRecord(signatureId, "row", text, "2026-10-28T00:00:00Z", "new", null)).IsSuccess);
            Assert.True(db.RecountSignatureHits(signatureId).IsSuccess);
            return Result<bool>.Success(true);
        }, CancellationToken.None);
        Assert.True(done.IsSuccess, done.Error);
    }

    public void Dispose()
    {
        _services.Dispose();
        Results.Dispose();
        Vault.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // The temp folder is cleaned by the OS.
        }
    }
}
