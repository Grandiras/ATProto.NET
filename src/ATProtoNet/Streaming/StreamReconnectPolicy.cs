using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// How a stream consumer reconnects after its connection drops: an exponential backoff from
/// <see cref="InitialDelay"/> up to <see cref="MaxDelay"/>, for at most <see cref="MaxAttempts"/>
/// consecutive failed attempts.
/// </summary>
/// <remarks>
/// <para>The attempt count resets whenever a connection delivers a frame, so a consumer that runs
/// for days survives any number of isolated drops. When <see cref="MaxAttempts"/> consecutive
/// attempts fail, <c>ConsumeAsync</c> throws an <see cref="EventStreamException"/> whose
/// <see cref="Exception.InnerException"/> is the last failure, rather than completing as if the
/// stream had ended.</para>
/// <para>A failure that reconnecting cannot fix is thrown at once, whatever the policy: an error
/// frame such as <c>FutureCursor</c>, or a subscription the server refused before the WebSocket
/// upgrade (see <see cref="EventStreamException.IsRetryable"/>).</para>
/// </remarks>
public sealed record StreamReconnectPolicy
{
    /// <summary>The delay before the first reconnect attempt, doubled on each further one. Default: 5 seconds.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest delay between two attempts. Default: 30 seconds.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many consecutive reconnect attempts may fail before the consumer gives up, or
    /// <see langword="null"/> to reconnect forever. <c>0</c> never reconnects. Default: 10.
    /// </summary>
    public int? MaxAttempts { get; init; } = 10;

    /// <summary>The delay before attempt <paramref name="attempt"/> (1-based), with up to 10 % jitter.</summary>
    internal TimeSpan DelayFor(int attempt)
    {
        var max = MaxDelay < InitialDelay ? InitialDelay : MaxDelay;
        var factor = Math.Pow(2, Math.Min(attempt - 1, 30));
        var ticks = Math.Min(max.Ticks, InitialDelay.Ticks * factor);

        // Jitter spreads out the consumers a relay restart disconnects all at once.
        return TimeSpan.FromTicks((long)(ticks * (1 + (Random.Shared.NextDouble() * 0.1))));
    }

    internal void Validate()
    {
        if (InitialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), InitialDelay, "The delay cannot be negative.");
        if (MaxDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxDelay), MaxDelay, "The delay cannot be negative.");
        if (MaxAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "Use null for unlimited attempts.");
    }
}

/// <summary>
/// The reconnect state of one <c>ConsumeAsync</c> call: counts consecutive failed attempts and
/// waits out the policy's delay between them.
/// </summary>
internal sealed class ReconnectBackoff(StreamReconnectPolicy policy, ILogger logger, string stream)
{
    private int _attempts;

    /// <summary>The connection delivered a frame: the next drop starts counting from zero.</summary>
    public void Reset() => _attempts = 0;

    /// <summary>
    /// Waits before the next attempt. Returns <see langword="false"/> when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <exception cref="EventStreamException">Every attempt the policy allows has failed.</exception>
    public async ValueTask<bool> WaitAsync(Exception? lastFailure, CancellationToken cancellationToken)
    {
        _attempts++;
        if (policy.MaxAttempts is { } max && _attempts > max)
        {
            throw new EventStreamException(
                $"The {stream} disconnected and {max} reconnect attempt(s) failed; giving up." +
                (lastFailure is null ? string.Empty : $" Last failure: {lastFailure.Message}"),
                (lastFailure as EventStreamException)?.Error,
                (lastFailure as EventStreamException)?.StatusCode,
                lastFailure);
        }

        var delay = policy.DelayFor(_attempts);
        logger.LogWarning(lastFailure,
            "The {Stream} disconnected; reconnecting in {Delay} (attempt {Attempt} of {Max})",
            stream, delay, _attempts, policy.MaxAttempts?.ToString() ?? "unlimited");

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
