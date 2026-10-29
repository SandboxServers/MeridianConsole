using System.Reflection;
using Xunit;

namespace Dhadgar.Lab.Tests;

/// <summary>
/// Guards the Docker gate (D-ML103): every class that needs Docker, the lab stack, port 8789 or the single-instance mutex
/// is in the <see cref="LabDockerGate"/> collection, which runs alone and after the parallel collections.
/// </summary>
public sealed class LabDockerGateTests
{
    /// <summary>Classes that must be gated by name, whatever their traits (D-ML103).</summary>
    private static readonly string[] ExplicitlyGated = ["SelfCheckE2ETests"];

    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void EveryDockerClass_IsInTheDockerGate()
    {
        var offenders = new List<string>();
        foreach (Type type in typeof(LabDockerGateTests).Assembly.GetTypes().Where(type => type.IsClass))
        {
            if (NeedsGate(type) && !IsInGate(type))
            {
                offenders.Add(type.FullName ?? type.Name);
            }
        }

        Assert.True(offenders.Count == 0,
            "These classes need [Collection(LabDockerGate.Name)] (D-ML103): " + string.Join(", ", offenders));
    }

    private static bool NeedsGate(Type type) =>
        ExplicitlyGated.Contains(type.Name, StringComparer.Ordinal)
        || HasDockerTrait(type)
        || type.GetMethods(DeclaredMembers).Any(HasDockerTrait);

    private static bool IsInGate(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType == typeof(Xunit.CollectionAttribute)
            && attribute.ConstructorArguments.Count == 1
            && attribute.ConstructorArguments[0].Value as string == LabDockerGate.Name);

    private static bool HasDockerTrait(MemberInfo member) =>
        member.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType == typeof(Xunit.TraitAttribute)
            && attribute.ConstructorArguments.Count == 2
            && attribute.ConstructorArguments[0].Value as string == "RequiresDocker"
            && attribute.ConstructorArguments[1].Value as string == "true");
}
