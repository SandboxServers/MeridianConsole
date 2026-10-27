using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Msi;

/// <summary>
/// Reads the Windows Installer database: which products carry an upgrade code, and their versions (P23). Fakes stand
/// in for it in unit tests, so no test reads the real installer state.
/// </summary>
internal interface IMsiProducts
{
    /// <summary>
    /// The product codes (<c>{GUID}</c>) installed under this upgrade code. Empty when none is installed. A failure means
    /// the installer could not be asked, so the caller cannot prove that no product is installed.
    /// </summary>
    Result<IReadOnlyList<string>> RelatedProducts(string upgradeCode);

    /// <summary>The <c>VersionString</c> of an installed product. A failure when the product is unknown or unreadable.</summary>
    Result<string> ProductVersion(string productCode);
}
