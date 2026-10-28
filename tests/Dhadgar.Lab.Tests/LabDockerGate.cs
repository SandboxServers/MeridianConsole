namespace Dhadgar.Lab.Tests;

/// <summary>
/// Serialises every <c>RequiresDocker</c> test class: they bring up the same fixed-name lab containers
/// (<c>meridian-lab-postgres</c> and the rest of the stack) on one Docker engine, so running them side by side collides.
/// </summary>
[Xunit.CollectionDefinition(LabDockerGate.Name, DisableParallelization = true)]
public sealed class LabDockerGate
{
    /// <summary>The xUnit collection name shared by the Docker test classes.</summary>
    public const string Name = "LabDocker";
}
