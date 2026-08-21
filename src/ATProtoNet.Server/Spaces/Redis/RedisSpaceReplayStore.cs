using ATProtoNet.Server.Spaces;
using StackExchange.Redis;

namespace ATProtoNet.Server.Redis;

/// <summary>
/// Redis-backed <see cref="ISpaceReplayStore"/>: single-use token identifiers held in one Redis
/// instance that every service instance shares.
/// </summary>
/// <remarks>
/// <para>Consuming a token is a single <c>SET key value NX EX ttl</c>. Redis executes it
/// atomically, so two instances presented the same delegation token, client attestation, or
/// DPoP proof at the same moment see exactly one success between them — which is the guarantee
/// <see cref="InMemorySpaceReplayStore"/> cannot give across a load balancer, and the reason a
/// multi-instance deployment needs a shared store at all.</para>
/// <para>Expiry is the key's own TTL, taken from the token's <c>exp</c>: an entry disappears at
/// the moment the token it guards would be rejected on its expiry anyway, so nothing sweeps and
/// the key space tracks the tokens in flight. A token that arrives already expired is still
/// recorded, briefly — the caller rejects it on its expiry, and this store's job is only to
/// answer whether the identifier was still available.</para>
/// <para>Register with
/// <see cref="RedisSpaceStoreExtensions.AddAtProtoRedisSpaceReplayStore(Microsoft.Extensions.DependencyInjection.IServiceCollection, string?)"/>.</para>
/// </remarks>
public sealed class RedisSpaceReplayStore : ISpaceReplayStore
{
    /// <summary>The key prefix used when none is given.</summary>
    public const string DefaultKeyPrefix = "atproto:space:replay:";

    /// <summary>
    /// The shortest TTL an entry is written with, for a token whose expiry has already passed or
    /// is about to.
    /// </summary>
    /// <remarks>
    /// Redis rejects a <c>SET</c> with a non-positive TTL, and an entry that is never written is
    /// an identifier that could be spent twice.
    /// </remarks>
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(1);

    private readonly Func<IDatabase> _database;
    private readonly string _keyPrefix;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the store over a connection multiplexer's default database.
    /// </summary>
    /// <param name="connection">The Redis connection.</param>
    /// <param name="keyPrefix">
    /// The prefix every key is written under. Defaults to <see cref="DefaultKeyPrefix"/>; set it
    /// when several services share one Redis instance.
    /// </param>
    /// <param name="timeProvider">The clock TTLs are measured from. Defaults to the system clock.</param>
    public RedisSpaceReplayStore(
        IConnectionMultiplexer connection, string? keyPrefix = null, TimeProvider? timeProvider = null)
        : this(FromConnection(connection), keyPrefix, timeProvider)
    {
    }

    /// <summary>
    /// Creates the store over a specific database.
    /// </summary>
    /// <param name="database">The Redis database.</param>
    /// <param name="keyPrefix">The prefix every key is written under.</param>
    /// <param name="timeProvider">The clock TTLs are measured from.</param>
    public RedisSpaceReplayStore(
        IDatabase database, string? keyPrefix = null, TimeProvider? timeProvider = null)
        : this(Constant(database), keyPrefix, timeProvider)
    {
    }

    private RedisSpaceReplayStore(Func<IDatabase> database, string? keyPrefix, TimeProvider? timeProvider)
    {
        _database = database;
        _keyPrefix = keyPrefix ?? DefaultKeyPrefix;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private static Func<IDatabase> FromConnection(IConnectionMultiplexer connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return () => connection.GetDatabase();
    }

    private static Func<IDatabase> Constant(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return () => database;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        cancellationToken.ThrowIfCancellationRequested();

        var lifetime = expiresAt - _timeProvider.GetUtcNow();
        if (lifetime < MinimumLifetime)
            lifetime = MinimumLifetime;

        // NX is the whole check: the key exists only if this identifier was already spent, and
        // the write and the test are one round trip that no other instance can interleave with.
        return await _database().StringSetAsync(
            BuildKey(issuer, tokenId, expiresAt),
            RedisValue.EmptyString,
            lifetime,
            when: When.NotExists);
    }

    /// <summary>
    /// Builds the key an identifier is stored under.
    /// </summary>
    /// <param name="issuer">The token's <c>iss</c>.</param>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <param name="expiresAt">The token's expiry.</param>
    /// <remarks>
    /// Keyed on <c>(iss, jti, exp)</c> like the interface itself: two issuers picking the same
    /// nonce is not a collision.
    /// </remarks>
    internal string BuildKey(string issuer, string tokenId, DateTimeOffset expiresAt) =>
        $"{_keyPrefix}{issuer}|{tokenId}|{expiresAt.ToUnixTimeSeconds()}";
}
