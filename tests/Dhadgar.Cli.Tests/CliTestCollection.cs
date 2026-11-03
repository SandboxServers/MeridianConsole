using Xunit;

namespace Dhadgar.Cli.Tests;

/// <summary>
/// Serialises every test that sets the process-wide <c>CliConfig.ConfigDirectoryOverride</c> seam.
/// </summary>
[CollectionDefinition("CliConfig", DisableParallelization = true)]
public sealed class CliConfigCollectionDefinition
{
}
