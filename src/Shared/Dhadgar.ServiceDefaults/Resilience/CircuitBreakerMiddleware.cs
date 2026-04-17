using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Resilience;

/// <summary>
/// Circuit breaker middleware that prevents cascading failures by temporarily
/// blocking requests to unhealthy backend services.
///
/// This middleware can be used standalone or with YARP. When used with YARP,
/// set the "CircuitBreaker:ServiceId" item in HttpContext.Items before this
/// middleware runs (typically via YARP's IReverseProxyFeature).
/// </summary>
public class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ICircuitBreakerStateStore _stateStore;

    // Metrics
    private static readonly Meter Meter = new("Dhadgar.ServiceDefaults.CircuitBreaker", "1.0.0");
    private static readonly Counter<long> CircuitOpenedCounter = Meter.CreateCounter<long>(
        "circuit_breaker.opened",
        description: "Number of times circuit breakers have opened");
    private static readonly Counter<long> CircuitClosedCounter = Meter.CreateCounter<long>(
        "circuit_breaker.closed",
        description: "Number of times circuit breakers have closed");
    private static readonly Counter<long> RequestsBlockedCounter = Meter.CreateCounter<long>(
        "circuit_breaker.requests_blocked",
        description: "Number of requests blocked by open circuits");
    private static readonly Counter<long> FailuresRecordedCounter = Meter.CreateCounter<long>(
        "circuit_breaker.failures_recorded",
        description: "Number of failures recorded by circuit breakers");

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        IOptions<CircuitBreakerOptions> options,
        ICircuitBreakerStateStore? stateStore = null,
        TimeProvider? timeProvider = null)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stateStore = stateStore ?? new InMemoryCircuitBreakerStateStore();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get service ID from context items (set by YARP or other middleware)
        var serviceId = context.Items["CircuitBreaker:ServiceId"] as string;

        // If no service ID, pass through without circuit breaking
        if (string.IsNullOrEmpty(serviceId))
        {
            await _next(context);
            return;
        }

        var circuitState = _stateStore.GetOrCreateState(serviceId);

        // Check if circuit is open
        if (IsCircuitOpen(circuitState, serviceId))
        {
            _logger.LogWarning(
                "Circuit open for service {ServiceId}, returning 503",
                serviceId);

            RequestsBlockedCounter.Add(1, new KeyValuePair<string, object?>("service_id", serviceId));

            await WriteCircuitOpenResponse(context, serviceId);
            return;
        }

        try
        {
            await _next(context);
            // Track response for circuit state
            TrackResponse(circuitState, serviceId, context.Response.StatusCode);
        }
        catch (Exception)
        {
            // Treat unhandled exception as failure (500) for circuit breaker tracking
            TrackResponse(circuitState, serviceId, StatusCodes.Status500InternalServerError);
            throw;
        }
    }

    private bool IsCircuitOpen(CircuitState state, string serviceId)
    {
        lock (state.Lock)
        {
            if (state.Status == CircuitStatus.Closed)
            {
                return false;
            }

            if (state.Status == CircuitStatus.Open)
            {
                // Check if we should transition to half-open
                var now = _timeProvider.GetUtcNow();
                if (state.OpenedAt.HasValue &&
                    now - state.OpenedAt.Value > TimeSpan.FromSeconds(_options.OpenDurationSeconds))
                {
                    state.Status = CircuitStatus.HalfOpen;
                    state.HalfOpenSuccessCount = 0;
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                            "Circuit for service {ServiceId} transitioning to half-open",
                            serviceId);
                    }
                    return false;
                }
                return true;
            }

            // Half-open - allow requests through to test
            return false;
        }
    }

    private void TrackResponse(CircuitState state, string serviceId, int statusCode)
    {
        var isFailure = _options.FailureStatusCodes.Contains(statusCode);

        lock (state.Lock)
        {
            if (state.Status == CircuitStatus.Closed)
            {
                if (isFailure)
                {
                    state.FailureCount++;
                    FailuresRecordedCounter.Add(1, new KeyValuePair<string, object?>("service_id", serviceId));

                    if (state.FailureCount >= _options.FailureThreshold)
                    {
                        state.Status = CircuitStatus.Open;
                        state.OpenedAt = _timeProvider.GetUtcNow();
                        CircuitOpenedCounter.Add(1, new KeyValuePair<string, object?>("service_id", serviceId));

                        _logger.LogWarning(
                            "Circuit opened for service {ServiceId} after {FailureCount} failures",
                            serviceId,
                            state.FailureCount);
                    }
                }
                else
                {
                    // Reset failure count on success
                    state.FailureCount = 0;
                }
            }
            else if (state.Status == CircuitStatus.HalfOpen)
            {
                if (isFailure)
                {
                    // Back to open on any failure
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = _timeProvider.GetUtcNow();
                    CircuitOpenedCounter.Add(1, new KeyValuePair<string, object?>("service_id", serviceId));

                    _logger.LogWarning(
                        "Circuit re-opened for service {ServiceId} after failure in half-open state",
                        serviceId);
                }
                else
                {
                    state.HalfOpenSuccessCount++;
                    if (state.HalfOpenSuccessCount >= _options.SuccessThreshold)
                    {
                        state.Status = CircuitStatus.Closed;
                        state.FailureCount = 0;
                        state.OpenedAt = null;
                        CircuitClosedCounter.Add(1, new KeyValuePair<string, object?>("service_id", serviceId));

                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "Circuit closed for service {ServiceId} after {SuccessCount} successful requests",
                                serviceId,
                                state.HalfOpenSuccessCount);
                        }
                    }
                }
            }
        }
    }

    private async Task WriteCircuitOpenResponse(HttpContext context, string serviceId)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["Retry-After"] = _options.OpenDurationSeconds.ToString(CultureInfo.InvariantCulture);

        var problemDetails = new ProblemDetails
        {
            Type = "https://httpstatuses.com/503",
            Title = "Service Temporarily Unavailable",
            Status = 503,
            Detail = _options.IncludeServiceNameInErrors
                ? $"The {serviceId} service is temporarily unavailable due to circuit breaker activation. Please retry after the specified time."
                : "The requested service is temporarily unavailable. Please retry after the specified time.",
            Instance = context.Request.Path.Value
        };

        // Add correlation ID if available
        if (context.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            problemDetails.Extensions["traceId"] = correlationId?.ToString();
        }

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}

/// <summary>
/// Tracks the state of a circuit breaker for a single service.
/// </summary>
public class CircuitState
{
    public object Lock { get; } = new();
    public int FailureCount { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
    public int HalfOpenSuccessCount { get; set; }
}

/// <summary>
/// The three states of a circuit breaker.
/// </summary>
public enum CircuitStatus
{
    /// <summary>
    /// Normal operation - requests pass through.
    /// </summary>
    Closed,

    /// <summary>
    /// Failure threshold exceeded - requests blocked.
    /// </summary>
    Open,

    /// <summary>
    /// Testing if service recovered - limited requests allowed.
    /// </summary>
    HalfOpen
}

/// <summary>
/// Interface for circuit breaker state storage, enabling different backends
/// (in-memory, Redis, etc.) for distributed scenarios.
/// </summary>
public interface ICircuitBreakerStateStore
{
    CircuitState GetOrCreateState(string serviceId);
    void RemoveState(string serviceId);
    IEnumerable<(string ServiceId, CircuitState State)> GetAllStates();
}

/// <summary>
/// In-memory implementation of circuit breaker state store.
/// Suitable for single-instance deployments.
/// </summary>
public class InMemoryCircuitBreakerStateStore : ICircuitBreakerStateStore
{
    private readonly ConcurrentDictionary<string, CircuitState> _states = new();

    public CircuitState GetOrCreateState(string serviceId)
    {
        return _states.GetOrAdd(serviceId, _ => new CircuitState());
    }

    public void RemoveState(string serviceId)
    {
        _states.TryRemove(serviceId, out _);
    }

    public IEnumerable<(string ServiceId, CircuitState State)> GetAllStates()
    {
        return _states.Select(kvp => (kvp.Key, kvp.Value));
    }
}
