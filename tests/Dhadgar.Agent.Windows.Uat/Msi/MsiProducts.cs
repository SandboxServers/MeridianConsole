using System.Runtime.InteropServices;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Msi;

/// <summary>
/// The real Windows Installer database, through <c>msi.dll</c>. Read-only: it never installs or removes anything.
/// </summary>
internal sealed partial class MsiProducts : IMsiProducts
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorUnknownProduct = 1605; // ERROR_UNKNOWN_PRODUCT: the product is not installed
    private const uint ErrorNoMoreItems = 259; // ERROR_NO_MORE_ITEMS: the enumeration has ended
    private const int ProductCodeBufferLength = 39; // 38-character {GUID} plus the terminating NUL
    private const int VersionBufferLength = 64;
    private readonly EnumerateRelated _enumerate;

    /// <summary>Creates the reader over the real <c>msi.dll</c>.</summary>
    public MsiProducts()
        : this(MsiEnumRelatedProductsW)
    {
    }

    /// <summary>Creates the reader over the given enumeration (tests pass a fake that returns installer codes).</summary>
    internal MsiProducts(EnumerateRelated enumerate)
    {
        _enumerate = enumerate;
    }

    /// <summary>One <c>MsiEnumRelatedProductsW</c> call; the return value is the Windows error code.</summary>
    internal delegate uint EnumerateRelated(string upgradeCode, uint reserved, uint index, char[] productCode);

    /// <inheritdoc />
    public Result<IReadOnlyList<string>> RelatedProducts(string upgradeCode)
    {
        var products = new List<string>();
        for (uint index = 0; ; index++)
        {
            var buffer = new char[ProductCodeBufferLength];
            var code = _enumerate(Braced(upgradeCode), 0, index, buffer);

            // The installer wants the braced form; an unbraced code makes it fail with ERROR_INVALID_PARAMETER (87).
            if (code == ErrorNoMoreItems)
            {
                return Result<IReadOnlyList<string>>.Success(products);
            }

            if (code != ErrorSuccess)
            {
                return Result<IReadOnlyList<string>>.Failure(
                    $"MsiEnumRelatedProductsW returned {code}; the installed products cannot be listed");
            }

            var length = Array.IndexOf(buffer, '\0');
            products.Add(new string(buffer, 0, length < 0 ? buffer.Length : length));
        }
    }

    /// <inheritdoc />
    public Result<string> ProductVersion(string productCode)
    {
        var buffer = new char[VersionBufferLength];
        var size = (uint)buffer.Length;
        var code = MsiGetProductInfoW(productCode, "VersionString", buffer, ref size);
        if (code == ErrorUnknownProduct)
        {
            return Result<string>.Failure($"product {productCode} is not installed");
        }

        if (code != ErrorSuccess)
        {
            return Result<string>.Failure($"MsiGetProductInfoW returned {code} for {productCode}");
        }

        return Result<string>.Success(new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)));
    }

    /// <summary>
    /// The braced upper-case form of an upgrade or product code (<c>{GUID}</c>). A code already in braces is returned as
    /// it is. The installer compares the braced string, so a bare GUID finds nothing.
    /// </summary>
    internal static string Braced(string guid) =>
        guid.StartsWith('{') ? guid : "{" + guid.ToUpperInvariant() + "}";

    [LibraryImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, char[] productCode);

    [LibraryImport("msi.dll", EntryPoint = "MsiGetProductInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiGetProductInfoW(string productCode, string property, char[] value, ref uint valueSize);
}
