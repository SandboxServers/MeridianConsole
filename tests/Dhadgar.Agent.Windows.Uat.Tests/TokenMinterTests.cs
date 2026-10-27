using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class TokenMinterTests
{
    [Fact]
    public async Task MintAsync_StoresTheRow_AndRedactsThePlainText()
    {
        var name = Guid.NewGuid().ToString();
        NodesDbContext Create() => new(new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var redactor = new Redactor();
        var org = Guid.NewGuid();

        var result = await new TokenMinter(Create, "run1", redactor).MintAsync(org, "uat-smoke", CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var db = Create();
        var row = await db.EnrollmentTokens.AsNoTracking().SingleAsync(t => t.Id == result.Value.Id);
        Assert.Equal(org, row.OrganizationId);
        Assert.Equal("uat-smoke", row.Label);
        Assert.Equal("uat-run1", row.CreatedByUserId);
        Assert.InRange(row.ExpiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(28), TimeSpan.FromMinutes(31));
        Assert.DoesNotContain(result.Value.PlainText, row.TokenHash, StringComparison.Ordinal);
        Assert.Equal("***EnrollmentToken***", redactor.Redact(result.Value.PlainText));
        Assert.Equal(result.Value.Id.ToString(), result.Value.ToString());
    }
}
