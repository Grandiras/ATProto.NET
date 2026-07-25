using ATProtoNet.Identity;

namespace ATProtoNet.Pds;

/// <summary>
/// Mints strictly increasing commit revisions (TIDs) for repositories.
/// <para>
/// The AT Protocol repository spec requires a commit's <c>rev</c> to be greater than the
/// previous commit's: consumers use it to order commits and to decide whether a repo they hold
/// is stale. <see cref="Tid.Next"/> cannot supply that on its own — it resolves only to the
/// millisecond and picks a random clock identifier, so two commits landing in the same
/// millisecond are ordered arbitrarily and a rapid second write can produce a revision that
/// sorts <em>before</em> the first.
/// </para>
/// <para>
/// This generator therefore clamps: the value it returns is always greater than both the last
/// value it issued and the caller-supplied floor (the stored head's revision, which survives a
/// process restart).
/// </para>
/// </summary>
public sealed class PdsRevisionGenerator
{
    private long _last;

    /// <summary>
    /// Mints the next revision.
    /// </summary>
    /// <param name="previousRev">
    /// The repository's current revision, if any. The result is guaranteed to sort after it,
    /// which matters after a restart when this instance has issued nothing yet.
    /// </param>
    /// <returns>A TID string strictly greater than every revision previously issued.</returns>
    public string Next(string? previousRev = null)
    {
        var floor = 0L;
        if (Tid.TryParse(previousRev, out var previous))
            floor = previous.ToInt64();

        // Microseconds since the UNIX epoch in the top 53 bits; clock id left at zero, because
        // this generator is the single writer for the repos it serves and monotonicity comes
        // from the clamp rather than from randomness.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L << 10;

        while (true)
        {
            var last = Interlocked.Read(ref _last);
            var next = Math.Max(now, Math.Max(last, floor) + 1);

            if (Interlocked.CompareExchange(ref _last, next, last) == last)
                return Tid.FromInt64(next).Value;
        }
    }
}
