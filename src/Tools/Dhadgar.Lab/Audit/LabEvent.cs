namespace Dhadgar.Lab.Audit;

/// <summary>
/// One structured lab event. <c>Name</c> is for example "lab.lease" or "lab.call". Fields never carry secrets or tool arguments.
/// </summary>
public sealed record LabEvent(string Name, DateTimeOffset At, IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// Sink for lab events. The audit log (L04) is the production implementation.
/// </summary>
public interface ILabEvents
{
    void Emit(LabEvent labEvent);
}

/// <summary>
/// Discards every event. Used by tests and by code paths that run before the audit log exists.
/// </summary>
public sealed class NullLabEvents : ILabEvents
{
    public void Emit(LabEvent labEvent)
    {
    }
}
