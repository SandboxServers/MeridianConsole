using Dhadgar.Agent.Windows.Uat.Msi;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The installer enumeration behind <see cref="MsiProducts"/> (P23): the braced code it sends, the end-of-list and error
/// codes it reads. The fakes return installer codes, so no test installs or removes a product; the one live test only
/// reads the database.
/// </summary>
public class MsiProductsTests
{
    private const string Bare = "8A3B4E6F-1D2C-4A5B-9E0F-7C8D6B5A4E3F";
    private const string Braced = "{8A3B4E6F-1D2C-4A5B-9E0F-7C8D6B5A4E3F}";
    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorInvalidParameter = 87;
    private const uint ErrorAccessDenied = 5;

    [Fact]
    public void Braced_AddsBracesAndUpperCasesABareCode()
    {
        Assert.Equal(Braced, MsiProducts.Braced(Bare));
        Assert.Equal(Braced, MsiProducts.Braced(Bare.ToLowerInvariant()));
    }

    [Fact]
    public void Braced_KeepsACodeThatIsAlreadyBraced()
    {
        Assert.Equal(Braced, MsiProducts.Braced(Braced));
    }

    [Fact]
    public void RelatedProducts_SendsTheBracedUpgradeCodeToTheInstaller()
    {
        var sent = new List<string>();
        var products = new MsiProducts((upgradeCode, _, _, _) =>
        {
            sent.Add(upgradeCode);
            return ErrorNoMoreItems;
        });

        var result = products.RelatedProducts(AgentWindowsFacts.MsiUpgradeCode);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value);
        Assert.Equal([Braced], sent);
    }

    [Fact]
    public void RelatedProducts_ReadsEveryItemUntilNoMoreItems()
    {
        var items = new[] { "{11111111-1111-1111-1111-111111111111}", "{22222222-2222-2222-2222-222222222222}" };
        var products = new MsiProducts((_, _, index, buffer) =>
        {
            if (index >= items.Length)
            {
                return ErrorNoMoreItems;
            }

            items[index].CopyTo(0, buffer, 0, 38);
            return 0;
        });

        var result = products.RelatedProducts(Bare);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(items, result.Value);
    }

    [Theory]
    [InlineData(ErrorInvalidParameter)]
    [InlineData(ErrorAccessDenied)]
    public void RelatedProducts_AnyOtherErrorIsAFailureNotAnEmptyList(uint code)
    {
        var products = new MsiProducts((_, _, _, _) => code);

        var result = products.RelatedProducts(Bare);

        Assert.True(result.IsFailure);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RelatedProducts_AFailureAfterAnItemIsAFailureNotAPartialList()
    {
        var products = new MsiProducts((_, _, index, buffer) =>
        {
            if (index == 0)
            {
                "{11111111-1111-1111-1111-111111111111}".CopyTo(0, buffer, 0, 38);
                return 0;
            }

            return ErrorAccessDenied;
        });

        var result = products.RelatedProducts(Bare);

        Assert.True(result.IsFailure);
    }

    [SkippableFact]
    public void RealInstaller_ListsTheUpgradeCodeWithoutAnError()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test: calls the real msi.dll, which does not exist on non-Windows runners");
        // Read-only against the real msi.dll: whatever is installed, the enumeration ends cleanly (259), never an error.
        var result = new MsiProducts().RelatedProducts(AgentWindowsFacts.MsiUpgradeCode);

        Assert.True(result.IsSuccess, result.Error);
    }

    [SkippableFact]
    public void RealInstaller_AnUnusedUpgradeCodeListsNothing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test: calls the real msi.dll, which does not exist on non-Windows runners");
        var result = new MsiProducts().RelatedProducts(Guid.NewGuid().ToString("D"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value);
    }
}
