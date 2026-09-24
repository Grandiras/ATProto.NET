using System.Security.Cryptography;

namespace ATProtoNet.Identity;

/// <summary>
/// Generates TIDs that are strictly increasing and never repeat, as the TID spec requires of
/// a generator. Safe to share across threads.
/// </summary>
/// <remarks>
/// <para>Each generator fixes one 10-bit clock identifier for its lifetime and combines it with
/// a microsecond timestamp. When the clock has not advanced since the previous TID — several
/// calls in one microsecond, or the clock stepping backwards — the timestamp is taken as one
/// microsecond past the previous one, so ordering never depends on the clock.</para>
/// <para>Uniqueness holds per generator. Separate generators (in separate processes, say) avoid
/// colliding by drawing different clock identifiers, which is why the default is random;
/// pass an explicit one to give each worker of a cluster its own.</para>
/// <para><see cref="Tid.Next"/> and <see cref="RecordKey.NewTid"/> use one process-wide
/// instance.</para>
/// </remarks>
public sealed class TidGenerator
{
    /// <summary>The largest clock identifier: the TID keeps 10 bits for it.</summary>
    public const int MaxClockId = 1023;

    // 53 bits of microseconds (until the year 2255) sit below the TID's zero high bit.
    private const long MaxTimestamp = (1L << 53) - 1;

    private readonly TimeProvider _timeProvider;
    private long _lastTimestamp = -1;

    /// <summary>
    /// Creates a generator.
    /// </summary>
    /// <param name="clockId">
    /// The clock identifier, from 0 to <see cref="MaxClockId"/>. Omit it to draw one at random.
    /// </param>
    /// <param name="timeProvider">The clock to read. Omit it for the system clock.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="clockId"/> is out of range.</exception>
    public TidGenerator(int? clockId = null, TimeProvider? timeProvider = null)
    {
        if (clockId is { } id)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(id, nameof(clockId));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxClockId, nameof(clockId));
        }

        ClockId = clockId ?? RandomNumberGenerator.GetInt32(MaxClockId + 1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The process-wide generator behind <see cref="Tid.Next"/>.</summary>
    internal static TidGenerator Shared { get; } = new();

    /// <summary>The clock identifier in the low 10 bits of every TID this generator returns.</summary>
    public int ClockId { get; }

    /// <summary>
    /// Returns the next TID: greater than every TID this generator has returned before.
    /// </summary>
    /// <returns>A new TID.</returns>
    /// <exception cref="InvalidOperationException">
    /// The timestamp no longer fits the TID's 53 bits.
    /// </exception>
    public Tid Next()
    {
        // The clock gives 100 ns ticks; a TID counts microseconds. A clock before the epoch
        // is treated as the epoch rather than producing a negative timestamp.
        var now = Math.Max(0, (_timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / 10);

        long previous, next;
        do
        {
            previous = Volatile.Read(ref _lastTimestamp);
            next = Math.Max(now, previous + 1);
        }
        while (Interlocked.CompareExchange(ref _lastTimestamp, next, previous) != previous);

        if (next > MaxTimestamp)
            throw new InvalidOperationException("The TID timestamp no longer fits in 53 bits.");

        return Tid.FromInt64((next << 10) | (long)ClockId);
    }
}
