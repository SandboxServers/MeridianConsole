namespace Dhadgar.Lab.Uat;

/// <summary>
/// The state of one <c>[[row.expect]]</c> clause (C8). <see cref="Unverified"/> is missing or unreadable evidence; it never passes.
/// </summary>
public enum ClauseState
{
    /// <summary>The observed evidence satisfies the op.</summary>
    Held,

    /// <summary>The evidence was read and does not satisfy the op.</summary>
    NotHeld,

    /// <summary>The evidence could not be read: a source error, an OS-unavailable source, or a pointer that names nothing.</summary>
    Unverified,

    /// <summary>A human clause that is not attested yet.</summary>
    Pending,
}
