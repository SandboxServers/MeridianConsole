using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Jobs;

/// <summary>
/// Paces the lab's Gateway traffic (D-ML88, F84). All lab traffic reaches the Gateway from one IP, and enroll is on the
/// <c>Auth</c> policy (30 per 60 s, <c>src/Dhadgar.Gateway/Program.cs:202-203</c>). The pacer grants at most
/// <see cref="Limit"/> calls in any rolling <see cref="Window"/>, which stays under that limit. It is a singleton: every
/// job that enrolls through the Gateway takes it from DI, and none copies it. No lab job raises a Gateway limit.
/// </summary>
public sealed class GatewayPacer
{
    /// <summary>Grants per rolling window. Under the Auth policy's 30 per 60 s.</summary>
    public const int Limit = 25;

    /// <summary>The rolling window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _grants = new();

    /// <summary>
    /// Creates the pacer over the injected clock.
    /// </summary>
    public GatewayPacer(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Waits until one more Gateway call is within the limit, then returns success. Returns a failure (never throws) when
    /// <paramref name="ct"/> is cancelled while waiting.
    /// </summary>
    public async ValueTask<Result> WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                DateTimeOffset now = _time.GetUtcNow();
                while (_grants.Count > 0 && now - _grants.Peek() >= Window)
                {
                    _grants.Dequeue();
                }

                if (_grants.Count < Limit)
                {
                    _grants.Enqueue(now);
                    return Result.Success();
                }

                wait = _grants.Peek() + Window - now;
            }

            try
            {
                await Task.Delay(wait, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Result.Failure("[Lab.Cancelled] the gateway pacer was cancelled while waiting");
            }
        }
    }
}
