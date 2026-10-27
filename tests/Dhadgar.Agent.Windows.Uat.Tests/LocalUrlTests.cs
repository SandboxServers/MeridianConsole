using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class LocalUrlTests
{
    private static readonly IReadOnlySet<int> Allocated = new HashSet<int> { 5040 };

    [Fact]
    public void Create_HttpsLocalhostAllocatedPort_Succeeds()
    {
        var result = LocalUrl.Create("https", "localhost", 5040, Allocated);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://localhost:5040/", result.Value.ToString());
    }

    [Fact]
    public void Create_HttpLocalhost_Fails()
    {
        Assert.True(LocalUrl.Create("http", "localhost", 5040, Allocated).IsFailure);
    }

    [Fact]
    public void Create_HttpsLoopbackIp_Fails()
    {
        Assert.True(LocalUrl.Create("https", "127.0.0.1", 5040, Allocated).IsFailure);
    }

    [Fact]
    public void Create_ExternalHost_Fails()
    {
        Assert.True(LocalUrl.Create("https", "example.com", 5040, Allocated).IsFailure);
    }

    [Fact]
    public void Create_UnallocatedPort_Fails()
    {
        Assert.True(LocalUrl.Create("https", "localhost", 5041, Allocated).IsFailure);
    }
}
