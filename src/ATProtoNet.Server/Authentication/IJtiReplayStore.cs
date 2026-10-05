using System.Collections.Concurrent;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Remembers the single-use tokens this service has already accepted, so a captured one cannot
/// be presented twice.
/// </summary>
/// <remarks>
/// <para>Every token this SDK verifies on a server carries a <c>jti</c> that may be spent exactly
/// once: a service auth token (<see cref="ServiceAuthVerifier"/>), and on a space server a
/// delegation token and a client attestation. Their signatures are what make them
/// unforgeable; this is what makes them unrepeatable.</para>
/// <para>Entries are keyed on <c>(issuer, jti, expiry)</c> rather than on the <c>jti</c> alone.
/// Two issuers picking the same nonce is not a collision, and including the expiry is what lets
/// an implementation evict an entry once the token it guards would be rejected on its own
/// expiry anyway, so the store never has to grow without bound.</para>
/// <para>That eviction is safe only if the expiry a caller passes is the moment the caller itself
/// stops accepting the token. A verifier that allows clock skew accepts a token until its
/// <c>exp</c> plus that skew, so that sum is what it passes: an entry dropped at the bare
/// <c>exp</c> would leave the token replayable for the length of the skew.</para>
/// <para>An implementation backing a multi-instance deployment must be shared across instances.
/// <see cref="InMemoryJtiReplayStore"/> is per-process, so a replay is caught only by the
/// instance that saw the original;
/// <c>EfCoreJtiReplayStore&lt;TContext&gt;</c> (the <c>ATProtoNet.Server.EntityFrameworkCore</c>
/// package) is shared through a database. Anything with an atomic "set if absent, with expiry" — Redis
/// <c>SET key 1 NX EXAT exp</c>, say — makes a store in a few lines.</para>
/// </remarks>
public interface IJtiReplayStore
{
    /// <summary>Records a token identifier as spent, and reports whether it was still available.</summary>
    /// <param name="issuer">
    /// What scopes the identifier: the token's <c>iss</c>.
    /// </param>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <param name="expiresAt">
    /// The first instant at which the caller no longer accepts the token: its <c>exp</c> plus
    /// whatever clock skew the caller's expiry check allows. The entry must be kept until then,
    /// and need not be kept longer, since the token is refused on its own expiry from then on.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identifier had not been seen and is now consumed;
    /// <see langword="false"/> when it was already spent, which means a replay.
    /// </returns>
    /// <remarks>
    /// The check and the record must be atomic. Two concurrent presentations of the same token
    /// must not both come back <see langword="true"/>.
    /// </remarks>
    ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

/// <summary>An in-process <see cref="IJtiReplayStore"/>, suitable for a single-instance service.</summary>
/// <remarks>
/// Expired entries are swept at most once a minute, in the background, so its size tracks the
/// tokens in flight. It is per-process and holds nothing across a restart; see
/// <see cref="IJtiReplayStore"/> for when that matters.
/// </remarks>
public sealed class InMemoryJtiReplayStore : IJtiReplayStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);
    private readonly SweepSchedule _sweeps;

    /// <summary>Creates a store using the system clock.</summary>
    public InMemoryJtiReplayStore() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a store reading the current time from <paramref name="timeProvider"/>.</summary>
    /// <param name="timeProvider">The clock to use.</param>
    public InMemoryJtiReplayStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _sweeps = new SweepSchedule(timeProvider);
    }

    /// <summary>The number of identifiers currently held, for diagnostics and tests.</summary>
    public int Count => _consumed.Count;

    // The most recent background sweep, for tests to wait on.
    internal Task LastSweep => _sweeps.Last;

    /// <inheritdoc/>
    public ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        // The dictionary tolerates the consumptions the scan runs alongside.
        _sweeps.RunIfDue(now =>
        {
            foreach (var (key, expiry) in _consumed)
                if (expiry <= now)
                    _consumed.TryRemove(key, out _);

            return Task.CompletedTask;
        });

        var key = $"{issuer}|{tokenId}|{expiresAt.ToUnixTimeSeconds()}";
        return ValueTask.FromResult(_consumed.TryAdd(key, expiresAt));
    }
}

// Runs a replay store's expiry sweep at most once a minute, in the background, started by whichever
// call finds one due.
internal sealed class SweepSchedule(TimeProvider timeProvider)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private long _due = timeProvider.GetUtcNow().Add(Interval).ToUnixTimeMilliseconds();
    private Task _last = Task.CompletedTask;

    public Task Last => Volatile.Read(ref _last);

    // Starts sweep with the current time, when one is due, without waiting for it.
    public void RunIfDue(Func<DateTimeOffset, Task> sweep)
    {
        var now = timeProvider.GetUtcNow();
        var due = Interlocked.Read(ref _due);
        if (now.ToUnixTimeMilliseconds() < due ||
            Interlocked.CompareExchange(ref _due, now.Add(Interval).ToUnixTimeMilliseconds(), due) != due)
            return;

        // Not awaited, and not tied to the request's cancellation: the request that found the
        // sweep due should not pay for it, and cancelling it would only leave the entries for later.
        Volatile.Write(ref _last, Task.Run(() => sweep(now)));
    }
}
