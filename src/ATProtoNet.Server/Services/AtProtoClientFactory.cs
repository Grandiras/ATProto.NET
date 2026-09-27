using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Default implementation of <see cref="IAtProtoClientFactory"/> that creates
/// per-request <see cref="AtProtoClient"/> instances from stored sessions.
/// </summary>
/// <remarks>
/// <para>Each client refreshes its session on demand and writes the rotated tokens back to the
/// store, so the next request's client starts from them. The clients refresh under one
/// <see cref="ISessionRefreshCoordinator"/>, so concurrent requests for one user spend its
/// single-use refresh token once.</para>
/// <para>The factory keeps the imported DPoP key of each account it has recently served, so a
/// request does not pay for importing it again; a session with another key (a new sign-in)
/// replaces it.</para>
/// </remarks>
public sealed class AtProtoClientFactory : IAtProtoClientFactory
{
    /// <summary>The most accounts whose DPoP keys are kept; beyond it the least recently used go.</summary>
    internal const int MaxCachedKeys = 1024;

    private readonly IAtProtoSessionStore _sessionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<OAuthClient?> _oauthClient;
    private readonly ISessionRefreshCoordinator? _refreshCoordinator;
    private readonly AtProtoClientOptions? _clientOptions;
    private readonly ILogger<AtProtoClient> _clientLogger;
    private readonly ILogger<AtProtoClientFactory> _logger;
    private readonly ConcurrentDictionary<Did, CachedKey> _keys = new();
    private int _warnedNoOAuthClient;

    /// <summary>Creates a new <see cref="AtProtoClientFactory"/>.</summary>
    /// <param name="sessionStore">Store of the users' sessions.</param>
    /// <param name="httpClientFactory">
    /// Creates the <see cref="HttpClient"/> the clients send with, the one named
    /// <see cref="AtProtoServiceCollectionExtensions.HttpClientName"/>.
    /// </param>
    /// <param name="oauthClient">
    /// The <see cref="OAuthClient"/> that refreshes and revokes OAuth sessions: the one the hosted
    /// login registers (<c>WithOAuth()</c>), or your own registered in dependency
    /// injection. Without one, a client on an OAuth session works until its access token expires.
    /// </param>
    /// <param name="refreshCoordinator">
    /// Coordinates the per-request clients' refreshes, registered by <c>WithClientFactory()</c>;
    /// share the same instance with anything else that writes to the store. Without one, two
    /// concurrent requests for a user can both spend its refresh token.
    /// </param>
    public AtProtoClientFactory(
        IAtProtoSessionStore sessionStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        OAuthClient? oauthClient = null,
        ISessionRefreshCoordinator? refreshCoordinator = null)
        : this(sessionStore, httpClientFactory, loggerFactory, () => oauthClient, refreshCoordinator, clientOptions: null)
    {
    }

    /// <summary>
    /// Creates a factory that resolves its <see cref="OAuthClient"/> when a client first needs
    /// one, and gives its clients the transport settings of <paramref name="clientOptions"/>, as
    /// the dependency injection registration does.
    /// </summary>
    /// <param name="sessionStore">Store of the users' sessions.</param>
    /// <param name="httpClientFactory">Creates the clients' <see cref="HttpClient"/>.</param>
    /// <param name="oauthClient">Resolves the <see cref="OAuthClient"/> on first use.</param>
    /// <param name="refreshCoordinator">Coordinates the clients' refreshes.</param>
    /// <param name="clientOptions">
    /// The registration's options, whose <see cref="AtProtoClientOptions.UserAgent"/> and
    /// <see cref="AtProtoClientOptions.RateLimit"/> the clients take. The rest describe one
    /// account's session and do not apply: a client addresses its user's PDS and refreshes on
    /// demand under <paramref name="refreshCoordinator"/>.
    /// </param>
    internal AtProtoClientFactory(
        IAtProtoSessionStore sessionStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        Func<OAuthClient?> oauthClient,
        ISessionRefreshCoordinator? refreshCoordinator,
        AtProtoClientOptions? clientOptions)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _oauthClient = oauthClient;
        _refreshCoordinator = refreshCoordinator;
        _clientOptions = clientOptions;
        _clientLogger = loggerFactory.CreateLogger<AtProtoClient>();
        _logger = loggerFactory.CreateLogger<AtProtoClientFactory>();
    }

    /// <summary>The coordinator the clients refresh under.</summary>
    internal ISessionRefreshCoordinator? RefreshCoordinator => _refreshCoordinator;

    /// <summary>How many accounts' DPoP keys are cached.</summary>
    internal int CachedKeyCount => _keys.Count;

    /// <inheritdoc/>
    public async Task<AtProtoClient?> CreateClientForUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Only the user the OAuth login signed in: a service auth caller carries the same did
        // claim, and must not get the account's stored session.
        if (OAuthUser.DidOf(user) is not { } did)
            return null;

        var session = await _sessionStore.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            // Signed out: whatever key material is cached for the account goes too.
            ForgetKey(did, keyBytes: null);
            return null;
        }

        // Refreshes on demand and persists rotated tokens to the same store, so a refresh made
        // by this request is what the next request's client reads.
        var client = new AtProtoClient(
            CreateClientOptions(),
            _httpClientFactory.CreateClient(AtProtoServiceCollectionExtensions.HttpClientName),
            _sessionStore,
            _clientLogger);

        // A session this client signs out, or finds refused, takes its cached key with it.
        client.SessionChanged += (_, change) =>
        {
            if (change.Change is AtProtoSessionChange.Removed or AtProtoSessionChange.Expired &&
                change.Previous is OAuthSession ended)
            {
                ForgetKey(ended.Did, ended.DPoPKey);
            }
        };

        try
        {
            var key = session is OAuthSession oauth ? KeyFor(oauth) : null;
            var oauthClient = session is OAuthSession ? _oauthClient() : null;
            await client.InstallStoredSessionAsync(session, oauthClient, key, cancellationToken).ConfigureAwait(false);

            if (session is OAuthSession && oauthClient is null && Interlocked.Exchange(ref _warnedNoOAuthClient, 1) == 0)
            {
                _logger.LogWarning(
                    "No OAuthClient is registered, so per-request clients cannot refresh OAuth sessions once their " +
                    "access tokens expire. Register the hosted login (WithOAuth) or your own OAuthClient.");
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    /// <summary>
    /// A client's options: the registration's transport settings, and the refresh behaviour every
    /// per-request client needs whatever the registered client's (on demand, no timer, under the
    /// factory's coordinator).
    /// </summary>
    private AtProtoClientOptions CreateClientOptions()
    {
        var options = new AtProtoClientOptions { RefreshCoordinator = _refreshCoordinator };
        if (_clientOptions is { } configured)
        {
            options.UserAgent = configured.UserAgent;
            options.RateLimit = configured.RateLimit;
        }

        return options;
    }

    /// <summary>
    /// A key object for the session's DPoP key, which the client may dispose: a view of the key
    /// cached for the account, imported when the account has none or a different one.
    /// </summary>
    /// <returns>The key, or <see langword="null"/> for one that does not import (the client then reports it).</returns>
    private DPoPProofGenerator? KeyFor(OAuthSession session)
    {
        var now = Environment.TickCount64;
        if (_keys.TryGetValue(session.Did, out var cached) && cached.KeyBytes.AsSpan().SequenceEqual(session.DPoPKey.Span))
        {
            cached.LastUsed = now;
            try
            {
                return cached.Prototype.CreateView();
            }
            catch (ObjectDisposedException)
            {
                // Forgotten by a sign-out meanwhile; the session read since is a new one.
            }
        }

        DPoPProofGenerator prototype;
        try
        {
            prototype = new DPoPProofGenerator(session.DPoPKey.ToArray());
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;
        }

        // A replaced or evicted key is not disposed: clients of earlier requests may still sign
        // with it. It is released when they are done with it and it is collected.
        _keys[session.Did] = new CachedKey(session.DPoPKey.ToArray(), prototype) { LastUsed = now };
        if (_keys.Count > MaxCachedKeys)
            EvictLeastRecentlyUsed();

        return prototype.CreateView();
    }

    /// <summary>
    /// Drops the account's cached DPoP key once its session has ended (signed out, or refused),
    /// and disposes it: a client of that session still holding it stops signing with it.
    /// </summary>
    /// <param name="did">The account.</param>
    /// <param name="keyBytes">
    /// The ended session's key; a cached key of another (newer) session is kept.
    /// <see langword="null"/> drops whatever is cached.
    /// </param>
    internal void ForgetKey(Did did, ReadOnlyMemory<byte>? keyBytes)
    {
        if (!_keys.TryGetValue(did, out var cached) ||
            (keyBytes is { } ended && !cached.KeyBytes.AsSpan().SequenceEqual(ended.Span)))
        {
            return;
        }

        if (_keys.TryRemove(new KeyValuePair<Did, CachedKey>(did, cached)))
            cached.Prototype.Dispose();
    }

    private void EvictLeastRecentlyUsed()
    {
        // A quarter at a time, so a full cache is not sorted on every new account.
        var excess = _keys.Count - MaxCachedKeys * 3 / 4;
        foreach (var (did, _) in _keys.OrderBy(pair => pair.Value.LastUsed).Take(excess).ToList())
            _keys.TryRemove(did, out _);
    }

    /// <summary>An account's imported DPoP key, and the bytes it was imported from.</summary>
    private sealed class CachedKey(byte[] keyBytes, DPoPProofGenerator prototype)
    {
        public byte[] KeyBytes { get; } = keyBytes;

        public DPoPProofGenerator Prototype { get; } = prototype;

        public long LastUsed
        {
            get => Volatile.Read(ref _lastUsed);
            set => Volatile.Write(ref _lastUsed, value);
        }

        private long _lastUsed;
    }
}
