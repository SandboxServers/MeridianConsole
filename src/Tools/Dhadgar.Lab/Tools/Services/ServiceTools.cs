using Dhadgar.Lab.Services;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Services;

/// <summary>Arguments of <c>svc_start</c> and <c>svc_restart</c>. <c>build_id</c> defaults to the latest build.</summary>
public sealed record ServiceStartArgs(string? Service = null, string? BuildId = null);

/// <summary>Arguments of <c>svc_stop</c>.</summary>
public sealed record ServiceStopArgs(string? Service = null);

/// <summary>Arguments of <c>svc_config_set</c>. A null <c>value</c> clears the key.</summary>
public sealed record ServiceConfigSetArgs(string? Service = null, string? Key = null, string? Value = null);

/// <summary>
/// <c>svc_start</c> (Leased): starts a lab service container from a build behind its proxies. Refuses unless the stack is up.
/// </summary>
public sealed class ServiceStartTool : LabTool<ServiceStartArgs>
{
    private readonly LabServices _services;

    /// <summary>
    /// Creates the tool over the service controller.
    /// </summary>
    public ServiceStartTool(LabServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "svc_start";

    /// <inheritdoc />
    public override string Description => "Start a lab service (gateway, nodes) from a build; refused unless the stack is up.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(ServiceStartArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Service))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] service is required");
        }

        Result<LabServiceStartDto> result = await _services.StartAsync(args.Service, args.BuildId, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value!);
    }
}

/// <summary>
/// <c>svc_stop</c> (Leased): stops a lab service container. A service that is not running reports <c>absent</c>.
/// </summary>
public sealed class ServiceStopTool : LabTool<ServiceStopArgs>
{
    private readonly LabServices _services;

    /// <summary>
    /// Creates the tool over the service controller.
    /// </summary>
    public ServiceStopTool(LabServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "svc_stop";

    /// <inheritdoc />
    public override string Description => "Stop a lab service container.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(ServiceStopArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Service))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] service is required");
        }

        Result<LabServiceStartDto> result = await _services.StopAsync(args.Service, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value!);
    }
}

/// <summary>
/// <c>svc_restart</c> (Leased): stops and starts a lab service, which applies any config overlay change.
/// </summary>
public sealed class ServiceRestartTool : LabTool<ServiceStartArgs>
{
    private readonly LabServices _services;

    /// <summary>
    /// Creates the tool over the service controller.
    /// </summary>
    public ServiceRestartTool(LabServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "svc_restart";

    /// <inheritdoc />
    public override string Description => "Restart a lab service from a build (default latest); applies config overlay changes.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(ServiceStartArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Service))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] service is required");
        }

        Result<LabServiceStartDto> result = await _services.RestartAsync(args.Service, args.BuildId, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value!);
    }
}

/// <summary>
/// <c>svc_config_set</c> (Leased): sets or clears one allowlisted overlay key; applied at the next start.
/// </summary>
public sealed class ServiceConfigSetTool : LabTool<ServiceConfigSetArgs>
{
    private readonly LabServices _services;
    private readonly ConfigOverlay _overlay;

    /// <summary>
    /// Creates the tool over the service controller and the overlay store.
    /// </summary>
    public ServiceConfigSetTool(LabServices services, ConfigOverlay overlay)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    /// <inheritdoc />
    public override string Name => "svc_config_set";

    /// <inheritdoc />
    public override string Description => "Set or clear one allowlisted config key for a lab service; applied at its next start.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(ServiceConfigSetArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Service) || string.IsNullOrWhiteSpace(args.Key))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] service and key are required"));
        }

        Result<ILabServiceAdapter> adapter = _services.Adapter(args.Service);
        if (adapter.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(adapter.Error));
        }

        Result<IReadOnlyDictionary<string, string>> overlay = _overlay.Set(adapter.Value!, args.Key, args.Value);
        return Task.FromResult(overlay.IsFailure
            ? Result<object>.Failure(overlay.Error)
            : Result<object>.Success(new { service = args.Service, overlay = overlay.Value }));
    }
}
