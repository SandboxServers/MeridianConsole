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
}
