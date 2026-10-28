using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

/// <summary>
/// The bundle's secret rules end to end through the real <see cref="BundleWriter"/>: encoded vault values never reach a file, and a value
/// that cannot be redacted is removed by the scan and blocks the run.
/// </summary>
public sealed class BundleSecretTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private const string LeakSpec = """
        schema = 1

        [section]
        id = "bundle-secret"
        title = "Bundle secret"

        [[row]]
        id = "leaky"
        title = "Leaky row"
        expected = "pass"
        depends_on = []

          [[row.expect]]
          id = "leak-clause"
          text = "no leak"
          source = "human"
          op = "truthy"
          required = true
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-w3fb-secret-" + Guid.NewGuid().ToString("N"));
    private readonly string _uatRuns;
    private readonly FakeTimeProvider _time = new(Start);
    private readonly SecretVault _vault = new();

    public BundleSecretTests()
    {
        _uatRuns = Path.Combine(_root, "uat-runs");
        _ = Directory.CreateDirectory(_uatRuns);
    }

    [Fact]
    public void EncodedVaultValue_IsRedactedInEveryFile_AndTheScanPasses()
    {
        // Quote, backslash, a character the default encoder escapes, a non-ASCII letter and a bang.
        string raw = "ab\"cd\\ef+ghé!";
        _ = _vault.Add(SecretKind.Password, raw);
        string escaped = JsonEncodedText.Encode(raw, JavaScriptEncoder.Default).ToString();
        UatSpec spec = Load(LeakSpec);
        JsonElement observed = JsonSerializer.SerializeToElement(new { echo = raw });
        BundleWriter writer = CreateWriter();

        writer.RunStarted("run-w3fb-escape", 3UL, spec);
        writer.RowFinished(new UatRowResult(
            "leaky",
            RowGrade.Fail,
            "leaked",
            [new UatClauseResult("leak-clause", ClauseState.Unverified, "boom " + raw, observed)]));
        writer.RunFinished(new UatRunResult("run-w3fb-escape", 3UL, false, RunOverall.Incomplete, []));

        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        string[] texts = Directory.GetFiles(writer.Folder!, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
        Assert.NotEmpty(texts);
        Assert.Contains(texts, text => text.Contains("***", StringComparison.Ordinal));
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(raw, text, StringComparison.Ordinal);
            Assert.DoesNotContain(escaped, text, StringComparison.Ordinal);
        });

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(writer.Folder!, "run.json")));
        Assert.Equal("passed", run.RootElement.GetProperty("secret_scan").GetString());
        Assert.True(run.RootElement.GetProperty("redactions").GetInt32() > 0);
    }

    [Fact]
    public void UnredactableSecret_IsRemovedByTheScan_BlocksTheLedger_AndStaysOut()
    {
        const string planted = "planted-secret-w3fb";
        _ = _vault.Add(SecretKind.Password, planted);
        UatSpec spec = Load(LeakSpec);
        BundleWriter writer = CreateWriter();

        writer.RunStarted("run-w3fb-plant", 4UL, spec);

        // A raw write that bypasses the redactor, into the rows folder the first pass writes to.
        string folder = Path.Combine(_uatRuns, _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-abcd1234-run-w3fb-plant");
        string plantDir = Path.Combine(folder, "rows", "planted");
        _ = Directory.CreateDirectory(plantDir);
        File.WriteAllText(Path.Combine(plantDir, "plant.json"), "{\"echo\":\"" + planted + "\"}");

        writer.RowFinished(new UatRowResult("leaky", RowGrade.Pass, "fine", []));
        writer.RunFinished(new UatRunResult("run-w3fb-plant", 4UL, false, RunOverall.Incomplete, []));

        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        string bundle = writer.Folder!;
        Assert.False(File.Exists(Path.Combine(bundle, "rows", "planted", "plant.json")));

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("secret_scan").GetString());
        string removed = run.RootElement.GetProperty("secret_scan_removed").GetRawText();
        Assert.Contains("rows/planted/plant.json", removed, StringComparison.Ordinal);

        Assert.Contains("BLOCKED", File.ReadAllText(Path.Combine(bundle, "ledger.md")), StringComparison.Ordinal);
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(planted, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VaultValueWithPipe_InClauseError_IsRedactedBeforeTheLedgerRenders()
    {
        // A pipe is the character the ledger renderer rewrites, so the value is redacted before rendering.
        const string planted = "a|b-secret-w3fb";
        _ = _vault.Add(SecretKind.Password, planted);
        UatSpec spec = Load(LeakSpec);
        JsonElement observed = JsonSerializer.SerializeToElement(new { echo = planted });
        BundleWriter writer = CreateWriter();

        writer.RunStarted("run-w3fb-pipe", 5UL, spec);
        writer.RowFinished(new UatRowResult(
            "leaky",
            RowGrade.Fail,
            "leaked",
            [new UatClauseResult("leak-clause", ClauseState.Unverified, "boom " + planted, observed)]));
        writer.RunFinished(new UatRunResult("run-w3fb-pipe", 5UL, false, RunOverall.Incomplete, []));

        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        string bundle = writer.Folder!;
        string escapedPipe = "a\\|b-secret-w3fb";
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(planted, text, StringComparison.Ordinal);
            Assert.DoesNotContain(escapedPipe, text, StringComparison.Ordinal);
        }

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        string scan = run.RootElement.GetProperty("secret_scan").GetString()!;
        Assert.True(scan is "passed" or "failed");
    }

    [Fact]
    public void DisposedEvidence_SetsWriteError_RunsTheScan_AndMarksTheRunFailed()
    {
        const string planted = "planted-secret-w3fb-n4";
        _ = _vault.Add(SecretKind.Password, planted);
        UatSpec spec = Load(LeakSpec);
        BundleWriter writer = CreateWriter();

        writer.RunStarted("run-w3fb-disposed", 6UL, spec);
        string folder = Path.Combine(_uatRuns, _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-abcd1234-run-w3fb-disposed");
        _ = Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ledger.md"), "planted " + planted);

        // An element whose document is disposed: reading it throws, so the evidence write fails part way through.
        JsonElement disposed;
        using (JsonDocument document = JsonDocument.Parse("{\"echo\":1}"))
        {
            disposed = document.RootElement;
        }

        writer.RowFinished(new UatRowResult(
            "leaky",
            RowGrade.Fail,
            "leaked",
            [new UatClauseResult("leak-clause", ClauseState.Unverified, "boom", disposed)]));
        writer.RunFinished(new UatRunResult("run-w3fb-disposed", 6UL, false, RunOverall.Incomplete, []));

        Assert.NotNull(writer.WriteError);
        Assert.Contains("[Lab.Unavailable]", writer.WriteError, StringComparison.Ordinal);
        Assert.NotNull(writer.Folder);
        string bundle = writer.Folder!;
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(planted, File.ReadAllText(file), StringComparison.Ordinal);
        }

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("secret_scan").GetString());
    }

    [Fact]
    public void RuntimeMintedTokenAndCookie_InStepEvidence_AreBlankedInEveryFile()
    {
        // Not in the vault: the stack minted them at run time. The lease id and the CLI token never enter the evidence.
        const string token = "eyJhbGciOiJIUzI1NiJ9.runtime-minted-w3fb";
        const string cookie = "session=runtime-cookie-w3fb";
        const string leaseId = "lease-w3fb-n5-0123456789";
        const string cliToken = "cli-token-w3fb-n5-abcdef";
        UatSpec spec = Load(LeakSpec);
        JsonElement response = JsonSerializer.SerializeToElement(new
        {
            status = 200,
            access_token = token,
            headers = new Dictionary<string, string> { ["Set-Cookie"] = cookie },
            header_list = new[] { new { name = "Set-Cookie", value = cookie } },
        });
        var step = new UatStepResult("probe", "http_request", true, null, null, null, 5L, response);
        BundleWriter writer = CreateWriter();

        writer.RunStarted("run-w3fb-n5", 7UL, spec);
        writer.RowFinished(new UatRowResult(
            "leaky",
            RowGrade.Fail,
            "leaked",
            [new UatClauseResult("leak-clause", ClauseState.Unverified, "boom")],
            [step]));
        writer.RunFinished(new UatRunResult("run-w3fb-n5", 7UL, false, RunOverall.Incomplete, []));

        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        string bundle = writer.Folder!;
        string rowText = File.ReadAllText(Path.Combine(bundle, "rows", "bundle-secret", "leaky.json"));
        Assert.Contains("[redacted]", rowText, StringComparison.Ordinal);
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            Assert.DoesNotContain(cookie, text, StringComparison.Ordinal);
            Assert.DoesNotContain(leaseId, text, StringComparison.Ordinal);
            Assert.DoesNotContain(cliToken, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnredactableSecret_InFingerprint_IsRemovedAndNotRewritten()
    {
        // The redactor rewrites "Password=<value>" to "Password=***", and the scanner still matches "Password=", so the
        // first-pass scan removes fingerprint.json. The rewrite must not bring it back.
        const string planted = "Password=fixture-only-w3fb";
        UatSpec spec = Load(LeakSpec);
        BundleWriter writer = CreateWriter();
        writer.UseLiveParts(new LiveFingerprintParts(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            planted,
            []));

        writer.RunStarted("run-w3fb-fp", 5UL, spec);
        writer.RowFinished(new UatRowResult("leaky", RowGrade.Pass, "fine", []));
        writer.RunFinished(new UatRunResult("run-w3fb-fp", 5UL, false, RunOverall.Incomplete, []));

        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        string bundle = writer.Folder!;
        Assert.False(File.Exists(Path.Combine(bundle, "fingerprint.json")));

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("secret_scan").GetString());
        string removed = run.RootElement.GetProperty("secret_scan_removed").GetRawText();
        Assert.Contains("fingerprint.json", removed, StringComparison.Ordinal);

        Assert.Contains("BLOCKED", File.ReadAllText(Path.Combine(bundle, "ledger.md")), StringComparison.Ordinal);
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(planted, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private BundleWriter CreateWriter()
    {
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        return new BundleWriter(_uatRuns, validator, new Redactor(_vault), new SecretScanner(_vault), _time, "abcd1234", "linux", "build", null);
    }

    private static UatSpec Load(string text)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("bundle-secret.toml", text);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }
}
