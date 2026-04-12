namespace ATProtoNet.Http;

/// <summary>
/// Rate limit information parsed from HTTP response headers.
/// </summary>
public sealed class RateLimitInfo
{
    /// <summary>Maximum number of requests allowed per time window.</summary>
    public int? Limit { get; init; }

    /// <summary>Number of requests remaining in the current window.</summary>
    public int? Remaining { get; init; }

    /// <summary>Time at which the rate limit window resets (UTC).</summary>
    public DateTimeOffset? Reset { get; init; }

    /// <summary>Whether the rate limit has been exceeded (i.e. remaining is 0).</summary>
    public bool IsExceeded => Remaining is 0;
}
