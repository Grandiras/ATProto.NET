namespace ATProtoNet.Http;

/// <summary>
/// How the client handles HTTP 429 (Too Many Requests).
/// </summary>
/// <remarks>
/// <para>On a 429 the client waits as long as the service asks — <c>Retry-After</c> in seconds
/// or as an HTTP date, otherwise until <c>RateLimit-Reset</c> — plus a little jitter, and
/// retries. A service that asks for longer than <see cref="MaxDelay"/> is not waited on: the
/// call throws <see cref="XrpcRateLimitException"/> at once, carrying the requested
/// <see cref="XrpcRateLimitException.RetryAfter"/>, so a daily window (the limit on
/// <c>createSession</c>, say) cannot hold a request open for hours.</para>
/// <para>When the response names no wait, the retries back off exponentially from one second,
/// capped at <see cref="MaxDelay"/>.</para>
/// </remarks>
public sealed class XrpcRateLimitOptions
{
    private int _maxRetries = 3;
    private TimeSpan _maxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times one call is retried after a 429. Default: 3. Zero disables retrying, for
    /// an <see cref="HttpClient"/> whose own pipeline already retries (a resilience handler, for
    /// example).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxRetries = value;
        }
    }

    /// <summary>
    /// The longest single wait the client accepts before retrying. Default: 30 seconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan MaxDelay
    {
        get => _maxDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _maxDelay = value;
        }
    }
}
