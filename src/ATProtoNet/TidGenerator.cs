using System.Security.Cryptography;

namespace ATProtoNet;

/// <summary>Generates TIDs that are strictly increasing and never repeat. Safe to share across threads.</summary>
/// <remarks>
/// <para>Each generator fixes one clock identifier for its lifetime. When the clock has not advanced since the previous TID (several
/// calls in one microsecond, or a clock stepping backwards) the timestamp is one microsecond past the previous one, so ordering never
/// depends on the clock.</para>
/// <para>Uniqueness holds per generator; separate processes avoid colliding by drawing different clock identifiers, which is why the
/// default is random. See <see href="https://atproto.com/specs/tid"/>.</para>
/// </remarks>
public sealed class TidGenerator
{
    /// <summary>The largest clock identifier, 10 bits.</summary>
    public const int MaxClockId = 1023;

    /// <summary>The largest timestamp in microseconds: 53 bits, until the year 2255.</summary>
    public const long MaxTimestamp = (1L << 53) - 1;

    private readonly TimeProvider _timeProvider;
    private long _lastTimestamp = -1;

    /// <summary>Creates a generator.</summary>
    /// <param name="clockId">The clock identifier, 0 to <see cref="MaxClockId"/>; a random one when omitted.</param>
    /// <param name="timeProvider">The clock to read; the system clock when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="clockId"/> is out of range.</exception>
    public TidGenerator(int? clockId = null, TimeProvider? timeProvider = null)
    {
        if (clockId is { } id) ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)id, (uint)MaxClockId, nameof(clockId));
        ClockId = clockId ?? RandomNumberGenerator.GetInt32(MaxClockId + 1);
        _timeProvider = timeProvider ?? SystemClock.Instance;
    }

    /// <summary>The process-wide generator behind <see cref="Tid.Next"/>, with a random clock identifier and the system clock.</summary>
    public static TidGenerator Default { get; } = new();

    /// <summary>The clock identifier in the low 10 bits of every TID this generator returns.</summary>
    public int ClockId { get; }

    /// <summary>The next TID, greater than every TID this generator returned before.</summary>
    /// <returns>A new TID.</returns>
    /// <exception cref="InvalidOperationException">The timestamp no longer fits 53 bits.</exception>
    public Tid Next()
    {
        // A clock before the epoch counts as the epoch.
        var now = Math.Max(0, (_timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / 10);
        long previous, next;
        do
        {
            previous = Volatile.Read(ref _lastTimestamp);
            next = Math.Max(now, previous + 1);
        }
        while (Interlocked.CompareExchange(ref _lastTimestamp, next, previous) != previous);

        if (next > MaxTimestamp) throw new InvalidOperationException("The TID timestamp no longer fits in 53 bits.");
        return Tid.FromInt64((next << 10) | (long)ClockId);
    }
}
