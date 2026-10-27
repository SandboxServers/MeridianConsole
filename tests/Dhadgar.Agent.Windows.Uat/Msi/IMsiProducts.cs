namespace Dhadgar.Agent.Windows.Uat.Msi;

/// <summary>
/// Reads the Windows Installer database: which products carry an upgrade code, and their versions (P23). Fakes stand
/// in for it in unit tests, so no test reads the real installer state.
/// </summary>
internal interface IMsiProducts
{
    /// <summary>The product codes (<c>{GUID}</c>) installed under this upgrade code. Empty when none is installed.</summary>
    IReadOnlyList<string> RelatedProducts(string upgradeCode);

    /// <summary>The <c>VersionString</c> of an installed product, or <c>null</c> when the product is unknown.</summary>
    string? ProductVersion(string productCode);
}
