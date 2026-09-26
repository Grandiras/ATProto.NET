using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// An authorization that has been pushed to the authorization server and is waiting for the
/// user to come back: everything <see cref="OAuthClient.CompleteAuthorizationAsync"/> needs, kept
/// by an <see cref="IOAuthStateStore"/> under its <see cref="State"/>.
/// </summary>
/// <remarks>
/// <b>Security:</b> it holds the PKCE verifier and the DPoP private key the session's tokens will
/// be bound to. A store that leaves the process should encrypt it (see
/// <see cref="DistributedCacheOAuthStateStoreOptions.Protect"/>), and it must never be logged.
/// </remarks>
public sealed record OAuthPendingAuthorization
{
    /// <summary>The <c>state</c> parameter, which the callback carries back.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>
    /// Who started the authorization, typically the requester's IP address, from
    /// <see cref="OAuthAuthorizationOptions.RequesterId"/>; stores limit pending authorizations
    /// per requester.
    /// </summary>
    [JsonPropertyName("requesterId")]
    public string? RequesterId { get; init; }

    /// <summary>The authorization server's issuer, which the callback's <c>iss</c> must equal.</summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>The authorization server's token endpoint.</summary>
    [JsonPropertyName("tokenEndpoint")]
    public required Uri TokenEndpoint { get; init; }

    /// <summary>The authorization server's revocation endpoint, if it has one.</summary>
    [JsonPropertyName("revocationEndpoint")]
    public Uri? RevocationEndpoint { get; init; }

    /// <summary>The redirect URI the authorization was pushed with.</summary>
    [JsonPropertyName("redirectUri")]
    public required string RedirectUri { get; init; }

    /// <summary>The PKCE code verifier.</summary>
    [JsonPropertyName("codeVerifier")]
    public required string CodeVerifier { get; init; }

    /// <summary>The DPoP private key (P-256, PKCS#8) the session's tokens will be bound to.</summary>
    [JsonPropertyName("dpopKey")]
    public required ReadOnlyMemory<byte> DPoPKey { get; init; }

    /// <summary>
    /// The key id of the client key a confidential client authenticated the pushed authorization
    /// with; the token exchange must use the same key. <see langword="null"/> for a public client.
    /// </summary>
    [JsonPropertyName("clientKeyId")]
    public string? ClientKeyId { get; init; }

    /// <summary>
    /// The account the authorization is for, when it started from a handle or DID; the token
    /// response must name the same one.
    /// </summary>
    [JsonPropertyName("did")]
    public Did? Did { get; init; }

    /// <summary>The application's <see cref="OAuthAuthorizationOptions.AppState"/>, handed back at completion.</summary>
    [JsonPropertyName("appState")]
    public string? AppState { get; init; }

    /// <summary>When the authorization was started.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the authorization stops being accepted; stores may drop it from then on.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The state, issuer and account; never the verifier or the key.</summary>
    public override string ToString() =>
        $"{nameof(OAuthPendingAuthorization)} {{ State = {State}, Issuer = {Issuer}, Did = {Did?.Value ?? "(unknown)"} }}";
}

/// <summary>
/// Keeps the authorizations an <see cref="OAuthClient"/> has started until their callbacks
/// arrive.
/// </summary>
/// <remarks>
/// <para>Starting an authorization needs no credentials, so anyone can fill a store: an
/// implementation must bound what it holds, drop entries after their
/// <see cref="OAuthPendingAuthorization.ExpiresAt"/>, and should limit how many one requester
/// (<see cref="OAuthPendingAuthorization.RequesterId"/>) can hold, so a flood from one address
/// displaces only its own entries rather than blocking everyone's logins.</para>
/// <para>Several instances of an application, or one that restarts between a login's start and
/// its callback, need a store they share: <see cref="DistributedCacheOAuthStateStore"/>.</para>
/// </remarks>
public interface IOAuthStateStore
{
    /// <summary>Stores a pending authorization under its <see cref="OAuthPendingAuthorization.State"/>.</summary>
    /// <param name="authorization">The pending authorization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetAsync(OAuthPendingAuthorization authorization, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the pending authorization stored under <paramref name="state"/>: each
    /// one is completed at most once.
    /// </summary>
    /// <param name="state">The <c>state</c> parameter of the callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The pending authorization, or <see langword="null"/> when none is stored under it. One
    /// that has expired may be returned; the client refuses it.
    /// </returns>
    ValueTask<OAuthPendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="IOAuthStateStore"/> in process memory: the default of an
/// <see cref="OAuthClient"/>. Bounded per requester and in total.
/// </summary>
/// <remarks>
/// <para>A requester holding <see cref="MaxPerRequester"/> pending authorizations loses its oldest
/// when it starts another, and past <see cref="Capacity"/> the oldest of all goes. Nothing is ever
/// refused. A login only waits between the redirect and the callback, so the newest attempt is the
/// one a user is on; refusing it instead would let abandoned attempts, or a flood from an address
/// the requester shares, lock the requester out for the whole lifetime of the entries, where
/// eviction only costs attempts that are older. A flood from many addresses at once can push out
/// older pending logins; limit the login rate in front of the application against that.
/// Authorizations with no <see cref="OAuthPendingAuthorization.RequesterId"/> count only toward
/// the total, so a server should pass one (see
/// <see cref="OAuthAuthorizationOptions.RequesterIdFor"/>).</para>
/// <para>Expired entries are dropped in the order they were stored. Pending logins do not survive
/// a restart, nor are they shared with other processes.</para>
/// </remarks>
public sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    /// <summary>The default of <see cref="MaxPerRequester"/>.</summary>
    public const int DefaultMaxPerRequester = 10;

    /// <summary>The default of <see cref="Capacity"/>.</summary>
    public const int DefaultCapacity = 10_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly Dictionary<string, LinkedList<Entry>> _byRequester = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>Creates a store.</summary>
    /// <param name="maxPerRequester">The most pending authorizations one requester can hold.</param>
    /// <param name="capacity">The most pending authorizations held in all.</param>
    /// <param name="timeProvider">The clock expiry is judged by. Defaults to the system clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is less than one.</exception>
    public InMemoryOAuthStateStore(
        int maxPerRequester = DefaultMaxPerRequester, int capacity = DefaultCapacity, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerRequester, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        MaxPerRequester = maxPerRequester;
        Capacity = capacity;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The most pending authorizations one requester can hold.</summary>
    public int MaxPerRequester { get; }

    /// <summary>The most pending authorizations held in all.</summary>
    public int Capacity { get; }

    /// <summary>The number of pending authorizations held, expired ones not yet dropped included.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(OAuthPendingAuthorization authorization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        lock (_lock)
        {
            var now = _time.GetUtcNow();
            while (_order.First is { } oldest && oldest.Value.Authorization.ExpiresAt <= now)
                Remove(oldest.Value);

            if (_entries.TryGetValue(authorization.State, out var existing))
                Remove(existing);

            var entry = new Entry(authorization);

            if (authorization.RequesterId is { } requester)
            {
                if (!_byRequester.TryGetValue(requester, out var own))
                    _byRequester[requester] = own = new LinkedList<Entry>();

                while (own.Count >= MaxPerRequester)
                    Remove(own.First!.Value);

                entry.PerRequester = own.AddLast(entry);
            }

            while (_entries.Count >= Capacity)
                Remove(_order.First!.Value);

            entry.Global = _order.AddLast(entry);
            _entries[authorization.State] = entry;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<OAuthPendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_lock)
        {
            if (!_entries.TryGetValue(state, out var entry))
                return ValueTask.FromResult<OAuthPendingAuthorization?>(null);

            Remove(entry);
            return ValueTask.FromResult<OAuthPendingAuthorization?>(entry.Authorization);
        }
    }

    private void Remove(Entry entry)
    {
        _entries.Remove(entry.Authorization.State);

        if (entry.Global is { } global)
            _order.Remove(global);

        if (entry.PerRequester is { List: { } own } node)
        {
            own.Remove(node);
            if (own.Count == 0)
                _byRequester.Remove(entry.Authorization.RequesterId!);
        }

        entry.Global = null;
        entry.PerRequester = null;
    }

    private sealed class Entry(OAuthPendingAuthorization authorization)
    {
        public OAuthPendingAuthorization Authorization { get; } = authorization;

        public LinkedListNode<Entry>? Global { get; set; }

        public LinkedListNode<Entry>? PerRequester { get; set; }
    }
}

/// <summary>
/// Options for <see cref="DistributedCacheOAuthStateStore"/>.
/// </summary>
public sealed class DistributedCacheOAuthStateStoreOptions
{
    /// <summary>The prefix of the cache keys. Default: <c>atproto:oauth-state:</c>.</summary>
    public string KeyPrefix { get; set; } = "atproto:oauth-state:";

    /// <summary>
    /// Encrypts an entry before it is written to the cache: each one holds the PKCE verifier and
    /// the DPoP private key of a pending login. With ASP.NET Core Data Protection:
    /// <c>Protect = protector.Protect</c> (an <c>IDataProtector</c>). Required, with
    /// <see cref="Unprotect"/>, unless <see cref="StoreSecretsUnencrypted"/> is set.
    /// </summary>
    public Func<byte[], byte[]>? Protect { get; set; }

    /// <summary>
    /// Decrypts what <see cref="Protect"/> wrote: <c>Unprotect = protector.Unprotect</c>. An entry
    /// it cannot decrypt is treated as absent.
    /// </summary>
    public Func<byte[], byte[]>? Unprotect { get; set; }

    /// <summary>
    /// Writes entries without encryption, so anyone who can read the cache reads the PKCE
    /// verifiers and DPoP private keys of pending logins. Only for a cache nobody else can reach,
    /// such as a local development instance. Default: <see langword="false"/>.
    /// </summary>
    public bool StoreSecretsUnencrypted { get; set; }
}

/// <summary>
/// An <see cref="IOAuthStateStore"/> over an <see cref="IDistributedCache"/> (Redis, SQL Server
/// and the like), so a login started on one instance of an application can complete on another,
/// or after a restart.
/// </summary>
/// <remarks>
/// <para>Each entry expires from the cache at its <see cref="OAuthPendingAuthorization.ExpiresAt"/>,
/// which bounds what a flood can leave behind; a distributed cache offers no way to count entries
/// per requester, so limit how fast one address can start logins in front of the application
/// (ASP.NET Core rate limiting, a reverse proxy).</para>
/// <para>Taking an entry is a read followed by a removal, as <see cref="IDistributedCache"/> has no
/// atomic take. Two callbacks with the same state racing on two instances can both read it, which
/// is acceptable: both carry the same authorization code, and the authorization server exchanges
/// a code once, so at most one of them gets tokens; and a state leaked to someone else is still
/// useless without the browser binding a web front end checks (the Blazor integration's is a
/// cookie). <see cref="InMemoryOAuthStateStore"/> takes atomically.</para>
/// <para>Entries are keyed by a hash of the state and encrypted with
/// <see cref="DistributedCacheOAuthStateStoreOptions.Protect"/>; the constructor refuses to store
/// them in the clear unless <see cref="DistributedCacheOAuthStateStoreOptions.StoreSecretsUnencrypted"/>
/// says so.</para>
/// </remarks>
public sealed class DistributedCacheOAuthStateStore : IOAuthStateStore
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheOAuthStateStoreOptions _options;

    /// <summary>Creates a store over <paramref name="cache"/>.</summary>
    /// <param name="cache">The cache.</param>
    /// <param name="options">
    /// Options: <see cref="DistributedCacheOAuthStateStoreOptions.Protect"/> and
    /// <see cref="DistributedCacheOAuthStateStoreOptions.Unprotect"/>, or the explicit
    /// <see cref="DistributedCacheOAuthStateStoreOptions.StoreSecretsUnencrypted"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Only one of <c>Protect</c> and <c>Unprotect</c> is set, or neither is and
    /// <c>StoreSecretsUnencrypted</c> is not set either.
    /// </exception>
    public DistributedCacheOAuthStateStore(IDistributedCache cache, DistributedCacheOAuthStateStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        if ((options.Protect is null) != (options.Unprotect is null))
            throw new ArgumentException("Protect and Unprotect must be set together.", nameof(options));

        if (options.Protect is null && !options.StoreSecretsUnencrypted)
        {
            throw new ArgumentException(
                "Pending logins hold DPoP private keys and PKCE verifiers: set Protect and Unprotect (for example to " +
                "an IDataProtector's), or StoreSecretsUnencrypted for a cache nobody else can read.",
                nameof(options));
        }

        _cache = cache;
        _options = options;
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(OAuthPendingAuthorization authorization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(authorization);
        if (_options.Protect is { } protect)
            bytes = protect(bytes);

        await _cache.SetAsync(
            Key(authorization.State),
            bytes,
            new DistributedCacheEntryOptions { AbsoluteExpiration = authorization.ExpiresAt },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<OAuthPendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var key = Key(state);
        var bytes = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
            return null;

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        try
        {
            if (_options.Unprotect is { } unprotect)
                bytes = unprotect(bytes);

            var authorization = JsonSerializer.Deserialize<OAuthPendingAuthorization>(bytes);

            // The key is a hash; the entry must be the one the state names, not a collision or a
            // copy placed under another key.
            return authorization is not null && string.Equals(authorization.State, state, StringComparison.Ordinal)
                ? authorization
                : null;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    private string Key(string state) =>
        _options.KeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
