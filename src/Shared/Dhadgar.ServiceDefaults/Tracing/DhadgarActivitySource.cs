using System.Diagnostics;

namespace Dhadgar.ServiceDefaults.Tracing;

/// <summary>
/// Shared ActivitySource for creating custom business operation spans.
/// Use this to instrument important operations that should appear in traces.
/// </summary>
/// <remarks>
/// <para>
/// Activities (spans) provide visibility into business operations. Use DhadgarActivitySource
/// to create spans for operations that are important to trace, such as:
/// </para>
/// <list type="bullet">
///   <item>Server provisioning/deprovisioning</item>
///   <item>Task execution and scheduling</item>
///   <item>File transfers</item>
///   <item>Authentication/authorization flows</item>
///   <item>Background job processing</item>
/// </list>
/// <para>
/// <b>Important:</b> Always check for null - sampling may skip the activity.
/// Use a <c>using</c> statement to ensure the activity is properly disposed/completed.
/// </para>
/// <para>
/// For error handling, set the activity status and record exceptions:
/// </para>
/// <code>
/// using var activity = DhadgarActivitySource.StartActivity("server.provision");
/// try
/// {
///     activity?.SetTag("server.id", serverId);
///     // ... do work ...
///     activity?.SetStatus(ActivityStatusCode.Ok);
/// }
/// catch (Exception ex)
/// {
///     activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
///     activity?.RecordException(ex);
///     throw;
/// }
/// </code>
/// <para>
/// Tags should follow OpenTelemetry semantic conventions where applicable.
/// See: https://opentelemetry.io/docs/concepts/semantic-conventions/
/// </para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code>
/// using var activity = DhadgarActivitySource.StartActivity("server.provision");
/// activity?.SetTag("server.id", serverId);
/// activity?.SetTag("tenant.id", tenantId);
/// // ... do work ...
/// activity?.SetStatus(ActivityStatusCode.Ok);
/// </code>
///
/// With initial tags:
/// <code>
/// var tags = new List&lt;KeyValuePair&lt;string, object?&gt;&gt;
/// {
///     new("server.id", serverId),
///     new("tenant.id", tenantId)
/// };
/// using var activity = DhadgarActivitySource.StartActivity("server.provision", ActivityKind.Internal, tags);
/// </code>
/// </example>
public static class DhadgarActivitySource
{
    /// <summary>
    /// The shared ActivitySource for Dhadgar business operations.
    /// </summary>
    /// <remarks>
    /// This ActivitySource is automatically registered with OpenTelemetry when using
    /// <see cref="TracingExtensions.AddDhadgarTracing"/>. Custom spans created with
    /// this source will appear in traces exported to your observability backend.
    /// </remarks>
    public static readonly ActivitySource Source = new("Dhadgar.Operations", "1.0.0");

    /// <summary>
    /// The name of the ActivitySource for registration with OpenTelemetry.
    /// </summary>
    /// <remarks>
    /// Use this when manually registering the source with OpenTelemetry:
    /// <code>
    /// services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing.AddSource(DhadgarActivitySource.Name));
    /// </code>
    /// </remarks>
    public static string Name => Source.Name;

    /// <summary>
    /// Starts a new activity (span) for a business operation.
    /// Returns null if no listeners are registered (sampling).
    /// </summary>
    /// <param name="operationName">
    /// Name of the operation (e.g., "server.start", "task.execute").
    /// Use dot-notation for hierarchical naming: "{entity}.{action}".
    /// </param>
    /// <param name="kind">
    /// The kind of activity. Defaults to <see cref="ActivityKind.Internal"/> for business logic.
    /// Use <see cref="ActivityKind.Server"/> for incoming requests,
    /// <see cref="ActivityKind.Client"/> for outgoing calls.
    /// </param>
    /// <returns>A started Activity, or null if not sampled.</returns>
    /// <example>
    /// <code>
    /// using var activity = DhadgarActivitySource.StartActivity("server.start");
    /// activity?.SetTag("server.id", serverId);
    /// // ... start the server ...
    /// activity?.SetStatus(ActivityStatusCode.Ok);
    /// </code>
    /// </example>
    public static Activity? StartActivity(
        string operationName,
        ActivityKind kind = ActivityKind.Internal)
    {
        return Source.StartActivity(operationName, kind);
    }

    /// <summary>
    /// Starts a new activity with initial tags.
    /// </summary>
    /// <param name="operationName">
    /// Name of the operation (e.g., "server.start", "task.execute").
    /// </param>
    /// <param name="kind">The kind of activity.</param>
    /// <param name="tags">Initial tags to set on the activity.</param>
    /// <returns>A started Activity with tags set, or null if not sampled.</returns>
    /// <remarks>
    /// Tags are only set if <see cref="Activity.IsAllDataRequested"/> is true,
    /// respecting the sampling decision.
    /// </remarks>
    /// <example>
    /// <code>
    /// var tags = new List&lt;KeyValuePair&lt;string, object?&gt;&gt;
    /// {
    ///     new("server.id", serverId),
    ///     new("tenant.id", tenantId),
    ///     new("server.game", "minecraft")
    /// };
    /// using var activity = DhadgarActivitySource.StartActivity(
    ///     "server.provision",
    ///     ActivityKind.Internal,
    ///     tags);
    /// </code>
    /// </example>
    public static Activity? StartActivity(
        string operationName,
        ActivityKind kind,
        IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var activity = Source.StartActivity(operationName, kind);

        if (activity?.IsAllDataRequested == true)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        return activity;
    }
}
