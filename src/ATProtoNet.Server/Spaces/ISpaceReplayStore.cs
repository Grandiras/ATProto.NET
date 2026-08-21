using System.Collections.Concurrent;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Remembers the single-use tokens this service has already accepted, so a captured one cannot
/// be presented twice.
/// </summary>
/// <remarks>
/// <para>Three of the credentials in the space flow are single-use: a delegation token, a client
/// attestation, and a DPoP proof each carry a <c>jti</c> that may be spent exactly once. Their
/// signatures are what make them unforgeable; this is what makes them unrepeatable.</para>
/// <para>Entries are keyed on <c>(issuer, jti, expiry)</c> rather than on the <c>jti</c> alone.
/// Two issuers picking the same nonce is not a collision, and including the expiry is what lets
/// an implementation evict an entry once the token it guards would be rejected on its own
/// expiry anyway, so the store never has to grow without bound.</para>
/// <para>An implementation backing a multi-instance deployment must be shared across instances.
/// <see cref="InMemorySpaceReplayStore"/> is per-process, so a replay is caught only by the
/// instance that saw the original; <see cref="ATProtoNet.Server.Redis.RedisSpaceReplayStore"/>
/// and <see cref="ATProtoNet.Server.EntityFrameworkCore.EfCoreSpaceReplayStore{TContext}"/> are
/// the two shipped implementations that are not.</para>
/// </remarks>
public interface ISpaceReplayStore
{
    /// <summary>
    /// Records a token identifier as spent, and reports whether it was still available.
    /// </summary>
    /// <param name="issuer">The token's <c>iss</c>, which scopes the identifier.</param>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <param name="expiresAt">
    /// When the token expires. An entry need not outlive this, since the token is rejected on
    /// its own expiry from then on.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

/// <summary>
/// An in-process <see cref="ISpaceReplayStore"/>, suitable for a single-instance service.
/// </summary>
/// <remarks>
/// Expired entries are swept opportunistically as new ones arrive, so the store's size tracks
/// the number of tokens in flight rather than the number ever seen. It holds no state across a
/// restart: a token accepted before one can be replayed after it, within its own (short)
/// lifetime. Use a shared store where that matters, or where more than one instance serves the
/// same DID — <c>AddAtProtoRedisSpaceReplayStore()</c> or
/// <c>AddAtProtoEfCoreSpaceReplayStore&lt;TContext&gt;()</c>.
/// </remarks>
public sealed class InMemorySpaceReplayStore : ISpaceReplayStore
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private long _sweepDue;

    /// <summary>Creates a store using the system clock.</summary>
    public InMemorySpaceReplayStore() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a store reading the current time from <paramref name="timeProvider"/>.</summary>
    /// <param name="timeProvider">The clock to use.</param>
    public InMemorySpaceReplayStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _sweepDue = _timeProvider.GetUtcNow().Add(SweepInterval).ToUnixTimeMilliseconds();
    }

    /// <summary>The number of identifiers currently held, for diagnostics and tests.</summary>
    public int Count => _consumed.Count;

    /// <inheritdoc/>
    public ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var now = _timeProvider.GetUtcNow();
        SweepIfDue(now);

        var key = $"{issuer}|{tokenId}|{expiresAt.ToUnixTimeSeconds()}";
        return ValueTask.FromResult(_consumed.TryAdd(key, expiresAt));
    }

    private void SweepIfDue(DateTimeOffset now)
    {
        var due = Interlocked.Read(ref _sweepDue);
        if (now.ToUnixTimeMilliseconds() < due)
            return;

        var next = now.Add(SweepInterval).ToUnixTimeMilliseconds();
        if (Interlocked.CompareExchange(ref _sweepDue, next, due) != due)
            return;

        foreach (var (key, expiry) in _consumed)
        {
            if (expiry <= now)
                _consumed.TryRemove(key, out _);
        }
    }
}
