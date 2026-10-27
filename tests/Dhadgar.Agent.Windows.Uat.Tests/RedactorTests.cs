using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class RedactorTests
{
    [Fact]
    public void Redact_TokenInsideLongerLine_ReplacesOnlyTheToken()
    {
        var redactor = new Redactor();
        redactor.AddSecret("tok-123", SecretKind.EnrollmentToken);

        var line = redactor.Redact("POST enroll token=tok-123 status=ok");

        Assert.Equal("POST enroll token=***EnrollmentToken*** status=ok", line);
    }

    [Fact]
    public void Redact_TwoSecretsOnOneLine_ReplacesBoth()
    {
        var redactor = new Redactor();
        redactor.AddSecret("tok-123", SecretKind.EnrollmentToken);
        redactor.AddSecret("pw-456", SecretKind.Password);

        var line = redactor.Redact("tok-123 and pw-456");

        Assert.Equal("***EnrollmentToken*** and ***Password***", line);
    }

    [Fact]
    public void Scan_SecretOnSecondLine_ReportsLineAndKindOnly()
    {
        var redactor = new Redactor();
        redactor.AddSecret("tok-123", SecretKind.EnrollmentToken);
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(file, ["clean line", "leak tok-123 here"]);

            var hits = redactor.Scan(file);

            var hit = Assert.Single(hits);
            Assert.Equal(2, hit.LineNumber);
            Assert.Equal(SecretKind.EnrollmentToken, hit.Kind);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Redact_LongerSecretWithSharedPrefix_ReplacesWholeSecret()
    {
        var redactor = new Redactor();
        redactor.AddSecret("abc", SecretKind.Password);
        redactor.AddSecret("abcdef", SecretKind.EnrollmentToken);

        var line = redactor.Redact("x abcdef y");

        Assert.Equal("x ***EnrollmentToken*** y", line);
    }

    [Fact]
    public async Task AddSecret_WhileRedacting_DoesNotThrow()
    {
        var redactor = new Redactor();
        redactor.AddSecret("seed-secret", SecretKind.Password);

        var adder = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                redactor.AddSecret($"secret-{i}", SecretKind.Password);
            }
        });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                _ = redactor.Redact("line with seed-secret and secret-5");
            }
        });

        await Task.WhenAll(adder, reader);
    }

    [Fact]
    public void Scan_FindsATokenInAUtf16LeFileWithABom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uat-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "msi-install.log");
            File.WriteAllText(path, "first line\r\nMSI token ENROLLMENT_TOKEN=tok-msi-1 here\r\n", System.Text.Encoding.Unicode);
            var redactor = new Redactor();
            redactor.AddSecret("tok-msi-1", SecretKind.EnrollmentToken);

            var hits = redactor.Scan(path);

            Assert.Equal([new SecretHit(2, SecretKind.EnrollmentToken)], hits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Scan_FindsATokenInAUtf8FileWithoutABom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uat-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "msi-cleanup.log");
            File.WriteAllText(path, "tok-utf8-2 on line one\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var redactor = new Redactor();
            redactor.AddSecret("tok-utf8-2", SecretKind.EnrollmentToken);

            var hits = redactor.Scan(path);

            Assert.Equal([new SecretHit(1, SecretKind.EnrollmentToken)], hits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
