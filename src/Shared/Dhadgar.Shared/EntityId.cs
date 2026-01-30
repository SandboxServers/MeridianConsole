using System.Diagnostics.CodeAnalysis;

namespace Dhadgar.Shared;

/// <summary>
/// Represents a strongly-typed entity identifier.
/// </summary>
/// <typeparam name="T">The type of entity this ID represents.</typeparam>
/// <remarks>
/// This type provides compile-time safety by preventing mixing of IDs from different entity types.
/// For example, EntityId&lt;User&gt; cannot be accidentally used where EntityId&lt;Organization&gt; is expected.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1000:DoNotDeclareStaticMembersOnGenericTypes",
    Justification = "EntityId<T> is a strongly-typed primitive with convenience factories.")]
public readonly record struct EntityId<T>(Guid Value)
{
    /// <summary>
    /// Creates a new entity ID with a randomly generated GUID.
    /// </summary>
    /// <returns>A new entity ID.</returns>
    public static EntityId<T> New() => new(Guid.NewGuid());

    /// <summary>
    /// Gets an empty entity ID (all zeros).
    /// </summary>
    public static EntityId<T> Empty => new(Guid.Empty);

    /// <summary>
    /// Creates an entity ID from a GUID (named alternative for implicit operator).
    /// </summary>
    /// <param name="value">The GUID value.</param>
    /// <returns>A new entity ID.</returns>
    public static EntityId<T> FromGuid(Guid value) => new(value);

    /// <summary>
    /// Converts this entity ID to a GUID (named alternative for implicit operator).
    /// </summary>
    /// <returns>The underlying GUID value.</returns>
    public Guid ToGuid() => Value;

    /// <summary>
    /// Implicitly converts a GUID to an entity ID.
    /// </summary>
    /// <param name="value">The GUID to convert.</param>
    public static implicit operator EntityId<T>(Guid value) => FromGuid(value);

    /// <summary>
    /// Implicitly converts an entity ID to a GUID.
    /// </summary>
    /// <param name="entityId">The entity ID to convert.</param>
    public static implicit operator Guid(EntityId<T> entityId) => entityId.ToGuid();

    /// <summary>
    /// Returns the string representation of the entity ID.
    /// </summary>
    /// <returns>The GUID formatted as a string.</returns>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Parses a string into an entity ID.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <returns>The parsed entity ID.</returns>
    /// <exception cref="FormatException">Thrown when the string is not a valid GUID.</exception>
    public static EntityId<T> Parse(string value) => new(Guid.Parse(value));

    /// <summary>
    /// Tries to parse a string into an entity ID.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="result">The parsed entity ID, or Empty if parsing failed.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    public static bool TryParse(string? value, out EntityId<T> result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new EntityId<T>(guid);
            return true;
        }

        result = Empty;
        return false;
    }
}
