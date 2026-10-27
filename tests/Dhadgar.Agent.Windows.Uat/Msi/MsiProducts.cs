using System.Runtime.InteropServices;

namespace Dhadgar.Agent.Windows.Uat.Msi;

/// <summary>
/// The real Windows Installer database, through <c>msi.dll</c>. Read-only: it never installs or removes anything.
/// </summary>
internal sealed partial class MsiProducts : IMsiProducts
{
    private const uint ErrorSuccess = 0;
    private const int ProductCodeBufferLength = 39; // 38-character {GUID} plus the terminating NUL
    private const int VersionBufferLength = 64;

    /// <inheritdoc />
    public IReadOnlyList<string> RelatedProducts(string upgradeCode)
    {
        var products = new List<string>();
        for (uint index = 0; ; index++)
        {
            var buffer = new char[ProductCodeBufferLength];

            // ERROR_NO_MORE_ITEMS (259) ends the loop; any other error ends it too, and the list holds what was found.
            if (MsiEnumRelatedProductsW(upgradeCode, 0, index, buffer) != ErrorSuccess)
            {
                break;
            }

            var length = Array.IndexOf(buffer, '\0');
            products.Add(new string(buffer, 0, length < 0 ? buffer.Length : length));
        }

        return products;
    }

    /// <inheritdoc />
    public string? ProductVersion(string productCode)
    {
        var buffer = new char[VersionBufferLength];
        var size = (uint)buffer.Length;
        if (MsiGetProductInfoW(productCode, "VersionString", buffer, ref size) != ErrorSuccess)
        {
            return null;
        }

        return new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length));
    }

    [LibraryImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, char[] productCode);

    [LibraryImport("msi.dll", EntryPoint = "MsiGetProductInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiGetProductInfoW(string productCode, string property, char[] value, ref uint valueSize);
}
