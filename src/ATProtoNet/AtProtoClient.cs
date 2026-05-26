using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.App.Bsky.Video;
using ATProtoNet.Lexicon.Com.AtProto.Admin;
using ATProtoNet.Lexicon.Com.AtProto.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Tools.Ozone;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet;

/// <summary>
/// The main AT Protocol client. Build custom AT Protocol applications,
/// or interact with Bluesky and any atproto-compatible service.
/// </summary>
/// <remarks>
/// <para>Create an instance using <see cref="AtProtoClientBuilder"/> or register via
/// dependency injection with <c>services.AddAtProto()</c>.</para>
/// <para>After construction, call <see cref="LoginAsync"/> to authenticate, then use
/// <see cref="GetCollection{T}"/> for typed CRUD on your custom Lexicon records,
/// or access protocol-level sub-clients directly.</para>
/// <para>One ATProto account can be used across many applications — each app
/// defines its own Lexicon schemas and stores records in the user's PDS.</para>
/// </remarks>
/// <example>
/// <code>
/// // Custom app example — one account, your own data
/// var client = new AtProtoClientBuilder()
///     .WithInstanceUrl("https://my-pds.example.com")
///     .Build();
///
/// await client.LoginAsync("alice.example.com", "app-password");
///
/// var todos = client.GetCollection&lt;TodoItem&gt;("com.example.todo.item");
/// var created = await todos.CreateAsync(new TodoItem { Title = "Buy milk" });
/// await foreach (var item in todos.EnumerateAsync())
///     Console.WriteLine(item.Value.Title);
/// </code>
/// </example>
public sealed class AtProtoClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly XrpcClient _xrpc;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<AtProtoClient> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private static readonly TimeSpan _refreshTimerDeadline = TimeSpan.FromSeconds(30);
    private readonly string? _relayUrl;
    private Session? _session;
    private OAuthSessionResult? _oauthSession;
    private OAuthClient? _oauthClient;
    private IAtProtoTokenStore? _oauthTokenStore;
    private Timer? _refreshTimer;
    // Marked volatile so the timer callback (running on a thread-pool thread)
    // sees Dispose's _disposed=true write without a memory barrier, and so the
    // post-lock recheck inside OnRefreshTimerElapsed is reliable on weakly-
    // ordered CPUs (ARM/Apple Silicon).
    private volatile bool _disposed;

    // ──────────────────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new client for the given PDS/service URL.
    /// Prefer using <see cref="AtProtoClientBuilder"/> for full configuration.
    /// </summary>
    public AtProtoClient(AtProtoClientOptions options)
        : this(options, null, null, null)
    {
    }

    /// <summary>
    /// Create a new client with full configuration.
    /// </summary>
    public AtProtoClient(
        AtProtoClientOptions options,
        HttpClient? httpClient,
        ISessionStore? sessionStore,
        ILogger<AtProtoClient>? logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger ?? NullLogger<AtProtoClient>.Instance;
        _sessionStore = sessionStore ?? new InMemorySessionStore();

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        _httpClient.BaseAddress ??= new Uri(options.InstanceUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
            $"ATProtoNet/{typeof(AtProtoClient).Assembly.GetName().Version}");

        _xrpc = new XrpcClient(_httpClient, _logger, AtProtoJsonDefaults.Options);

        // Initialize sub-clients
        Server = new ServerClient(_xrpc, _logger);
        Repo = new RepoClient(_xrpc, _logger);
        Identity = new IdentityClient(_xrpc, _logger);
        Sync = new SyncClient(_xrpc, _logger);
        Admin = new AdminClient(_xrpc, _logger);
        Label = new LabelClient(_xrpc, _logger);
        Moderation = new ModerationClient(_xrpc, _logger);

        // Bluesky sub-clients
        var bskyLogger = _logger;
        Bsky = new BlueskyClients(
            new ActorClient(_xrpc, bskyLogger),
            new FeedClient(_xrpc, bskyLogger),
            new GraphClient(_xrpc, bskyLogger),
            new LabelerClient(_xrpc, bskyLogger),
            new NotificationClient(_xrpc, bskyLogger),
            new VideoClient(_xrpc, bskyLogger));

        // Chat sub-clients (automatically proxied to chat service)
        Chat = new ChatClients(
            new ConvoClient(_xrpc, _logger),
            new ChatActorClient(_xrpc, _logger));

        // Ozone moderation service
        Ozone = new OzoneClient(_xrpc, _logger);

        // Standard.site — long-form publishing
        Site = new StandardSiteClient(_xrpc, _logger, Repo);

        if (options.AutoRefreshSession)
            _refreshTimer = new Timer(OnRefreshTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _relayUrl = options.RelayUrl;
    }

    // ──────────────────────────────────────────────────────────
    //  Sub-client properties
    // ──────────────────────────────────────────────────────────

    /// <summary>com.atproto.server.* — session and account management.</summary>
    public ServerClient Server { get; }

    /// <summary>com.atproto.repo.* — record CRUD operations.</summary>
    public RepoClient Repo { get; }

    /// <summary>com.atproto.identity.* — DID/handle resolution.</summary>
    public IdentityClient Identity { get; }

    /// <summary>com.atproto.sync.* — repository sync and blob download.</summary>
    public SyncClient Sync { get; }

    /// <summary>com.atproto.admin.* — admin operations (requires admin auth).</summary>
    public AdminClient Admin { get; }

    /// <summary>com.atproto.label.* — label querying.</summary>
    public LabelClient Label { get; }

    /// <summary>com.atproto.moderation.* — moderation reporting.</summary>
    public ModerationClient Moderation { get; }

    /// <summary>app.bsky.* — Bluesky social application APIs.</summary>
    public BlueskyClients Bsky { get; }

    /// <summary>chat.bsky.* — Bluesky direct message / chat APIs (proxied to chat service).</summary>
    public ChatClients Chat { get; }

    /// <summary>tools.ozone.* — Ozone moderation service APIs.</summary>
    public OzoneClient Ozone { get; }

    /// <summary>site.standard.* — Standard.site long-form publishing APIs.</summary>
    public StandardSiteClient Site { get; }

    // ──────────────────────────────────────────────────────────
    //  Firehose / Relay
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new <see cref="Streaming.FirehoseClient"/> using the configured relay URL.
    /// </summary>
    /// <returns>A new <see cref="Streaming.FirehoseClient"/>. Caller is responsible for disposal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no relay URL is configured (set <see cref="AtProtoClientOptions.RelayUrl"/>).
    /// </exception>
    public Streaming.FirehoseClient CreateFirehoseClient()
    {
        if (string.IsNullOrEmpty(_relayUrl))
            throw new InvalidOperationException(
                "No relay URL configured. Set AtProtoClientOptions.RelayUrl or use AtProtoClientBuilder.WithRelayUrl().");

        return new Streaming.FirehoseClient(_relayUrl, _logger);
    }

    /// <summary>
    /// Create a new <see cref="Streaming.FirehoseConsumer"/> using the configured relay URL.
    /// The consumer handles automatic reconnection and cursor management.
    /// </summary>
    /// <param name="reconnectDelay">Delay between reconnection attempts. Default: 5 seconds.</param>
    /// <param name="maxReconnectAttempts">Max reconnection attempts. Default: 10. Use -1 for unlimited.</param>
    /// <returns>A new <see cref="Streaming.FirehoseConsumer"/>. Caller is responsible for disposal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no relay URL is configured (set <see cref="AtProtoClientOptions.RelayUrl"/>).
    /// </exception>
    public Streaming.FirehoseConsumer CreateFirehoseConsumer(
        TimeSpan? reconnectDelay = null,
        int maxReconnectAttempts = 10)
    {
        if (string.IsNullOrEmpty(_relayUrl))
            throw new InvalidOperationException(
                "No relay URL configured. Set AtProtoClientOptions.RelayUrl or use AtProtoClientBuilder.WithRelayUrl().");

        return new Streaming.FirehoseConsumer(_relayUrl, _logger, reconnectDelay, maxReconnectAttempts);
    }

    // ──────────────────────────────────────────────────────────
    //  Custom Lexicon support
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get a strongly-typed <see cref="RecordCollection{T}"/> for a custom Lexicon record type.
    /// This is the primary API for building custom AT Protocol applications.
    /// </summary>
    /// <typeparam name="T">Your record type (can extend <see cref="AtProtoRecord"/> or be any serializable class).</typeparam>
    /// <param name="collection">The Lexicon NSID for the collection (e.g., "com.example.todo.item").</param>
    /// <returns>A typed collection providing Create, Get, Put, Delete, List, and Enumerate operations.</returns>
    /// <example>
    /// <code>
    /// var todos = client.GetCollection&lt;TodoItem&gt;("com.example.todo.item");
    /// await todos.CreateAsync(new TodoItem { Title = "Example" });
    /// </code>
    /// </example>
    public RecordCollection<T> GetCollection<T>(string collection) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        return new RecordCollection<T>(this, collection);
    }

    /// <summary>
    /// Call a custom XRPC query (HTTP GET) endpoint defined by your Lexicon.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.listItems").</param>
    /// <param name="parameters">Optional query parameters as an anonymous object, Dictionary, or IDictionary&lt;string, string?&gt;.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// var result = await client.QueryAsync&lt;ListResult&gt;(
    ///     "com.example.todo.listItems",
    ///     new { limit = 25, cursor = "abc" });
    /// </code>
    /// </example>
    public async Task<T> QueryAsync<T>(
        string nsid,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var dict = XrpcQueryBuilder.ToDictionary(parameters);
        return await _xrpc.QueryAsync<T>(nsid, dict, cancellationToken);
    }

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) endpoint defined by your Lexicon.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.updateStatus").</param>
    /// <param name="body">The request body, serialized as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// var result = await client.ProcedureAsync&lt;StatusResult&gt;(
    ///     "com.example.todo.updateStatus",
    ///     new { rkey = "abc", status = "done" });
    /// </code>
    /// </example>
    public async Task<T> ProcedureAsync<T>(
        string nsid,
        object? body = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (body is not null)
            return await _xrpc.ProcedureAsync<object, T>(nsid, body, cancellationToken: cancellationToken);
        return await _xrpc.ProcedureAsync<T>(nsid, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) that returns no response body.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="body">The request body, serialized as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcedureAsync(
        string nsid,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        if (body is not null)
            await _xrpc.ProcedureAsync<object>(nsid, body, cancellationToken: cancellationToken);
        else
            await _xrpc.ProcedureAsync(nsid, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Session state
    // ──────────────────────────────────────────────────────────

    /// <summary>The current session, or null if not authenticated.</summary>
    public Session? Session => _session;

    /// <summary>Whether the client currently has an active session.</summary>
    public bool IsAuthenticated => _session is not null;

    /// <summary>The DID of the authenticated user, or null.</summary>
    public string? Did => _session?.Did;

    /// <summary>The handle of the authenticated user, or null.</summary>
    public string? Handle => _session?.Handle;

    /// <summary>
    /// The latest repository revision (TID) received from the service via the
    /// <c>Atproto-Repo-Rev</c> response header. Indicates how up-to-date
    /// the service is with the authenticated account's repository.
    /// </summary>
    public string? LatestRepoRev => _xrpc.LatestRepoRev;

    /// <summary>
    /// The latest rate limit information parsed from HTTP response headers.
    /// Updated after every XRPC request.
    /// </summary>
    public RateLimitInfo? LatestRateLimitInfo => _xrpc.LatestRateLimitInfo;

    // ──────────────────────────────────────────────────────────
    //  Service Proxying
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the default <c>atproto-proxy</c> header for all subsequent XRPC requests.
    /// When set, the PDS will proxy requests to the specified service.
    /// </summary>
    /// <param name="proxyHeader">
    /// The proxy header value: a DID with a service endpoint fragment
    /// (e.g., <c>did:web:api.bsky.app#bsky_appview</c>).
    /// Use <see cref="Http.ServiceProxy"/> to construct or use pre-built constants.
    /// </param>
    public void SetProxy(string proxyHeader) => _xrpc.SetProxy(proxyHeader);

    /// <summary>
    /// Clears the default <c>atproto-proxy</c> header.
    /// </summary>
    public void ClearProxy() => _xrpc.ClearProxy();

    /// <summary>
    /// Sets the subscribed labeler DIDs. When set, all XRPC requests include the
    /// <c>atproto-accept-labelers</c> header so the server returns labels from these labelers.
    /// </summary>
    /// <param name="labelerDids">The DIDs of labeler services to subscribe to.</param>
    public void SetLabelers(IEnumerable<string> labelerDids) => _xrpc.SetLabelers(labelerDids);

    /// <summary>
    /// Sets the subscribed labeler DIDs from string parameters.
    /// </summary>
    /// <param name="labelerDids">The DIDs of labeler services to subscribe to.</param>
    public void SetLabelers(params string[] labelerDids) => _xrpc.SetLabelers(labelerDids);

    /// <summary>
    /// Clears the subscribed labeler DIDs, removing the <c>atproto-accept-labelers</c> header.
    /// </summary>
    public void ClearLabelers() => _xrpc.ClearLabelers();

    // ──────────────────────────────────────────────────────────
    //  Authentication
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticate with a handle/email and password (or app password).
    /// </summary>
    /// <param name="identifier">Handle or email address.</param>
    /// <param name="password">Password or app password.</param>
    /// <param name="authFactorToken">Optional 2FA token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticated session.</returns>
    public async Task<Session> LoginAsync(
        string identifier,
        string password,
        string? authFactorToken = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Logging in as {Identifier}", identifier);

        var response = await Server.CreateSessionAsync(
            identifier, password, authFactorToken, cancellationToken);

        var session = new Session
        {
            Did = response.Did,
            Handle = response.Handle,
            AccessJwt = response.AccessJwt,
            RefreshJwt = response.RefreshJwt,
            Email = response.Email,
            EmailConfirmed = response.EmailConfirmed,
            EmailAuthFactor = response.EmailAuthFactor,
            Active = response.Active,
            Status = response.Status,
        };

        await ApplySessionAsync(session);
        _logger.LogInformation("Logged in successfully as {Handle} ({Did})", session.Handle, session.Did);
        return session;
    }

    /// <summary>
    /// Resume a session from a previously stored session.
    /// This validates the session by calling getSession, and refreshes tokens if needed.
    /// </summary>
    /// <param name="session">A previously saved session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResumeSessionAsync(
        Session session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _logger.LogInformation("Resuming session for {Did}", session.Did);

        // Apply tokens first so we can make the API call
        _xrpc.SetTokens(session.AccessJwt, session.RefreshJwt);

        try
        {
            // Validate the access token
            var current = await Server.GetSessionAsync(cancellationToken);
            _logger.LogInformation("Session resumed successfully for {Handle}", current.Handle);

            var updatedSession = new Session
            {
                Did = session.Did,
                Handle = current.Handle,
                AccessJwt = session.AccessJwt,
                RefreshJwt = session.RefreshJwt,
                Email = current.Email,
                EmailConfirmed = current.EmailConfirmed,
                EmailAuthFactor = session.EmailAuthFactor,
                DidDoc = session.DidDoc,
                Active = session.Active,
                Status = session.Status,
            };

            await ApplySessionAsync(updatedSession);
        }
        catch (AtProtoHttpException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest
                                               && ex.ErrorType == "ExpiredToken")
        {
            _logger.LogInformation("Access token expired, attempting refresh");
            await RefreshSessionAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Refresh the current session tokens. Routes OAuth-bound sessions through the
    /// OAuth token endpoint (requires a registered <see cref="OAuthClient"/> — see
    /// <see cref="ApplyOAuthSessionAsync"/>) and legacy app-password sessions through
    /// <c>com.atproto.server.refreshSession</c>.
    /// </summary>
    public async Task RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshSessionUnlockedAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Performs the actual refresh work without touching <see cref="_refreshLock"/>.
    /// Public callers should go through <see cref="RefreshSessionAsync"/>; the timer
    /// callback acquires the lock itself with a non-blocking wait so it can skip
    /// when a refresh is already in progress.
    /// </summary>
    private async Task RefreshSessionUnlockedAsync(CancellationToken cancellationToken)
    {
        if (_oauthSession is not null)
        {
            if (_oauthClient is null)
                throw new InvalidOperationException(
                    "Cannot refresh OAuth session: no OAuthClient was registered. " +
                    "Pass an OAuthClient to ApplyOAuthSessionAsync, or refresh manually.");

            _logger.LogDebug("Refreshing OAuth session for {Did}", _oauthSession.Did);

            var tokens = await _oauthClient.RefreshTokensAsync(_oauthSession, cancellationToken);
            var refreshedAt = DateTimeOffset.UtcNow;

            // Persist BEFORE mutating in-memory state. The auth server has
            // already invalidated the old refresh token at this point — if the
            // store write fails and we'd already mutated memory, the current
            // process would silently continue with new tokens while the durable
            // store keeps the dead old ones. A later process or sibling instance
            // would then reload the dead token and log the user out with no
            // visible signal that this refresh succeeded server-side.
            //
            // Writing the store first makes the failure mode loud: the in-memory
            // session is still pointing at the (now-dead) old refresh token, so
            // the next request fails fast with an unmistakable invalid_grant
            // rather than silently corrupting persistence.
            if (_oauthTokenStore is not null)
            {
                var updated = BuildRefreshedTokenData(_oauthSession, tokens, refreshedAt);
                await _oauthTokenStore.StoreAsync(_oauthSession.Did, updated, cancellationToken);
            }

            // Store write succeeded (or no store wired). Safe to update memory.
            _oauthSession.AccessToken = tokens.AccessToken;
            if (tokens.RefreshToken is not null)
                _oauthSession.RefreshToken = tokens.RefreshToken;
            _oauthSession.TokenObtainedAt = refreshedAt;

            _xrpc.SetOAuthTokens(
                _oauthSession.AccessToken,
                _oauthSession.RefreshToken,
                _oauthSession.DPoP,
                _oauthSession.ResourceServerDpopNonce);

            if (_session is not null)
            {
                _session = new Session
                {
                    Did = _session.Did,
                    Handle = _session.Handle,
                    AccessJwt = _oauthSession.AccessToken,
                    RefreshJwt = _oauthSession.RefreshToken ?? string.Empty,
                    Email = _session.Email,
                    EmailConfirmed = _session.EmailConfirmed,
                    EmailAuthFactor = _session.EmailAuthFactor,
                    DidDoc = _session.DidDoc,
                    Active = _session.Active,
                    Status = _session.Status,
                };
                await _sessionStore.SaveAsync(_session, cancellationToken);
            }

            if (tokens.ExpiresIn is { } expiresIn && expiresIn > 0)
                StartRefreshTimer(TimeSpan.FromSeconds(Math.Max(expiresIn - 60, 30)));

            _logger.LogDebug("OAuth session refreshed successfully");
            return;
        }

        if (_session?.RefreshJwt is null or "")
            throw new InvalidOperationException("No session to refresh. Call LoginAsync first.");

        _logger.LogDebug("Refreshing session for {Did}", _session.Did);

        var response = await Server.RefreshSessionAsync(cancellationToken);

        var refreshedSession = new Session
        {
            Did = _session.Did,
            Handle = response.Handle,
            AccessJwt = response.AccessJwt,
            RefreshJwt = response.RefreshJwt,
            Email = _session.Email,
            EmailConfirmed = _session.EmailConfirmed,
            EmailAuthFactor = _session.EmailAuthFactor,
            DidDoc = _session.DidDoc,
            Active = _session.Active,
            Status = _session.Status,
        };

        await ApplySessionAsync(refreshedSession);
        _logger.LogDebug("Session refreshed successfully");
    }

    /// <summary>
    /// Log out and destroy the current session.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        // Serialize the entire logout against any concurrent refresh or
        // ApplyOAuthSessionAsync. Holding _refreshLock across the network call
        // is intentional — the alternative (drop-the-lock-for-the-network-call)
        // races a concurrent Apply that completes between DeleteSession and
        // the post-network re-acquisition, then nulls the new session's state
        // out from under the user. Callers concerned about a slow PDS pinning
        // logout should pass a CancellationToken with their preferred timeout.
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is null && _oauthSession is null) return;

            var loggingOutDid = _session?.Did ?? _oauthSession?.Did;
            _logger.LogInformation("Logging out {Did}", loggingOutDid);

            try
            {
                if (_session is not null)
                    await Server.DeleteSessionAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to delete session on server");
            }

            // Purge the persisted OAuth record (refresh token + DPoP key) so
            // the user's credentials don't remain at rest beyond the documented
            // session lifetime. Best-effort — a store outage shouldn't block
            // the in-process state teardown that follows.
            if (_oauthTokenStore is not null && loggingOutDid is not null)
            {
                try
                {
                    await _oauthTokenStore.RemoveAsync(loggingOutDid, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to remove persisted OAuth tokens for {Did}; " +
                        "stored tokens may outlive the in-process session.", loggingOutDid);
                }
            }

            _xrpc.ClearTokens();
            _session = null;
            _oauthSession?.Dispose();
            _oauthSession = null;
            _oauthClient = null;
            // Drop the token-store reference so a subsequent ApplyOAuthSessionAsync
            // for a different user doesn't inherit it implicitly; the next caller
            // must pass tokenStore explicitly (or accept no persistent rotation).
            _oauthTokenStore = null;
            StopRefreshTimer();

            await _sessionStore.ClearAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Dynamic PDS
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Changes the target PDS URL at runtime. Call this before <see cref="LoginAsync"/> or
    /// <see cref="ApplyOAuthSessionAsync"/> when the user selects a different PDS.
    /// </summary>
    /// <param name="pdsUrl">The new PDS URL (e.g., "https://pds.example.com").</param>
    public void SetPdsUrl(string pdsUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdsUrl);
        _logger.LogInformation("Switching PDS to {PdsUrl}", pdsUrl);
        _xrpc.SetBaseUrl(pdsUrl);
    }

    /// <summary>
    /// Gets the current PDS base URL.
    /// </summary>
    public string PdsUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "https://bsky.social";

    // ──────────────────────────────────────────────────────────
    //  OAuth Authentication
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Apply an OAuth session obtained from <see cref="OAuthClient.CompleteAuthorizationAsync"/>.
    /// Sets up DPoP-bound tokens and points the client at the correct PDS.
    /// </summary>
    /// <param name="oauthSession">The completed OAuth session.</param>
    /// <param name="oauthClient">
    /// The <see cref="OAuthClient"/> that issued the session. Required for token
    /// refresh; without it, <see cref="RefreshSessionAsync"/> will throw rather than
    /// fall through to the legacy refresh endpoint with an empty bearer token.
    /// </param>
    /// <param name="tokenStore">
    /// Optional durable store to receive rotated tokens after each successful
    /// OAuth refresh. When provided, the rotated access/refresh tokens are
    /// written back to <paramref name="tokenStore"/> alongside the in-memory
    /// session so other processes and per-request clients see the latest
    /// refresh token. Without this, a rotated refresh token is invalidated
    /// before the next request reads the stale value from the store and the
    /// user is silently logged out.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyOAuthSessionAsync(
        OAuthSessionResult oauthSession,
        OAuthClient? oauthClient = null,
        IAtProtoTokenStore? tokenStore = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauthSession);

        // Serialize against any in-flight refresh — without this lock the timer
        // callback can dereference _oauthSession/_oauthClient/_xrpc tokens while
        // Apply swaps them out, producing torn state or writing refresh results
        // onto a freshly-installed session it never targeted.
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Only overwrite when the caller actually supplied a value. A
            // shorthand re-Apply like `ApplyOAuthSessionAsync(session)` (the
            // pattern in docs/oauth.md) would otherwise silently null out the
            // refresh client and token store from a prior full Apply, and the
            // next timer-driven refresh would throw "no OAuthClient was
            // registered" — silently logging the user out an hour later.
            // Callers that truly want to clear these can call LogoutAsync first
            // (which nulls them deterministically).
            if (oauthClient is not null) _oauthClient = oauthClient;
            if (tokenStore is not null) _oauthTokenStore = tokenStore;

            _logger.LogInformation("Applying OAuth session for {Did} on PDS {PdsUrl}",
                oauthSession.Did, oauthSession.PdsUrl);

            // Point the XRPC client at the user's PDS
            _xrpc.SetBaseUrl(oauthSession.PdsUrl);

            // Set DPoP-bound tokens
            _xrpc.SetOAuthTokens(
                oauthSession.AccessToken,
                oauthSession.RefreshToken,
                oauthSession.DPoP,
                oauthSession.ResourceServerDpopNonce);

            // Dispose the previous session's DPoP key BEFORE swapping. Without
            // this, a re-Apply (account switch, factory reuse) leaks the prior
            // ECDsa instance — only the GC finalizer would release the native
            // handle. Per-request factory clients are unaffected (Dispose runs
            // at request end), but long-lived Blazor hosts accumulate handles.
            // Skip disposing the same instance (idempotent re-Apply).
            if (!ReferenceEquals(_oauthSession, oauthSession))
                _oauthSession?.Dispose();

            _oauthSession = oauthSession;

            // Create a session object for backward compatibility
            _session = new Session
            {
                Did = oauthSession.Did,
                Handle = oauthSession.Handle,
                AccessJwt = oauthSession.AccessToken,
                RefreshJwt = oauthSession.RefreshToken ?? string.Empty,
            };

            await _sessionStore.SaveAsync(_session, cancellationToken);

            // Schedule token refresh
            if (oauthSession.ExpiresIn.HasValue)
            {
                var refreshIn = TimeSpan.FromSeconds(Math.Max(oauthSession.ExpiresIn.Value - 60, 30));
                StartRefreshTimer(refreshIn);
            }
            else
            {
                StartRefreshTimer(TimeSpan.FromMinutes(4));
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Builds an <see cref="AtProtoTokenData"/> snapshot using the unchanged
    /// session metadata (DPoP key, PDS URL, issuer, handle) combined with the
    /// freshly-rotated tokens from a refresh response. Used by the OAuth
    /// refresh path to persist the new state to <see cref="IAtProtoTokenStore"/>
    /// BEFORE mutating the in-memory session.
    /// </summary>
    private static AtProtoTokenData BuildRefreshedTokenData(
        OAuthSessionResult session, OAuthTokenResponse tokens, DateTimeOffset refreshedAt)
    {
        return new AtProtoTokenData
        {
            Did = session.Did,
            Handle = session.Handle,
            IsHandleVerified = session.IsHandleVerified,
            AccessToken = tokens.AccessToken,
            // Refresh responses MAY omit refresh_token to mean "reuse the prior
            // one"; keep the existing one in that case.
            RefreshToken = tokens.RefreshToken ?? session.RefreshToken,
            PdsUrl = session.PdsUrl,
            Issuer = session.Issuer,
            TokenEndpoint = session.TokenEndpoint,
            DPoPPrivateKey = session.DPoP.ExportPrivateKey(),
            AuthServerDpopNonce = session.AuthServerDpopNonce,
            ResourceServerDpopNonce = session.ResourceServerDpopNonce,
            TokenObtainedAt = refreshedAt,
            // ExpiresIn / Scope may be rotated by the AS — prefer fresh values.
            ExpiresIn = tokens.ExpiresIn ?? session.ExpiresIn,
            Scope = tokens.Scope ?? session.Scope,
        };
    }

    /// <summary>
    /// The current OAuth session, if authenticated via OAuth.
    /// </summary>
    public OAuthSessionResult? OAuthSession => _oauthSession;

    // ──────────────────────────────────────────────────────────
    //  High-level convenience methods
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a text post. For richer posts, use <see cref="RepoClient.CreateRecordAsync"/>.
    /// </summary>
    /// <param name="text">The post text.</param>
    /// <param name="facets">Optional rich-text facets.</param>
    /// <param name="embed">Optional embed (images, link card, quote, video).</param>
    /// <param name="reply">Optional reply reference.</param>
    /// <param name="langs">Optional language tags (BCP-47).</param>
    /// <param name="labels">Optional self-labels for content warnings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The URI and CID of the created post.</returns>
    public async Task<CreateRecordResponse> PostAsync(
        string text,
        List<Facet>? facets = null,
        EmbedBase? embed = null,
        ReplyRef? reply = null,
        List<string>? langs = null,
        SelfLabels? labels = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var post = new PostRecord
        {
            Text = text,
            Facets = facets,
            Embed = embed,
            Reply = reply,
            Langs = langs,
            Labels = labels,
            CreatedAt = AtProtoJsonDefaults.NowTimestamp(),
        };

        return await Repo.CreateRecordAsync(
            _session!.Did, "app.bsky.feed.post", post, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Like a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">The CID of the post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateRecordResponse> LikeAsync(
        string uri, string cid, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var like = new LikeRecord
        {
            Subject = new StrongRef { Uri = uri, Cid = cid },
            CreatedAt = AtProtoJsonDefaults.NowTimestamp(),
        };

        return await Repo.CreateRecordAsync(
            _session!.Did, "app.bsky.feed.like", like, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unlike a post (delete the like record).
    /// </summary>
    /// <param name="likeUri">The AT-URI of the like record (from PostViewerState.Like).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnlikeAsync(string likeUri, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var parsed = AtUri.Parse(likeUri);
        await Repo.DeleteRecordAsync(
            parsed.Repo, parsed.Collection!, parsed.RecordKey!, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Repost a post.
    /// </summary>
    public async Task<CreateRecordResponse> RepostAsync(
        string uri, string cid, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var repost = new RepostRecord
        {
            Subject = new StrongRef { Uri = uri, Cid = cid },
            CreatedAt = AtProtoJsonDefaults.NowTimestamp(),
        };

        return await Repo.CreateRecordAsync(
            _session!.Did, "app.bsky.feed.repost", repost, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Undo a repost.
    /// </summary>
    public async Task UndoRepostAsync(string repostUri, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var parsed = AtUri.Parse(repostUri);
        await Repo.DeleteRecordAsync(
            parsed.Repo, parsed.Collection!, parsed.RecordKey!, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Follow an actor.
    /// </summary>
    /// <param name="did">The DID of the actor to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateRecordResponse> FollowAsync(
        string did, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var follow = new FollowRecord
        {
            Subject = did,
            CreatedAt = AtProtoJsonDefaults.NowTimestamp(),
        };

        return await Repo.CreateRecordAsync(
            _session!.Did, "app.bsky.graph.follow", follow, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unfollow an actor.
    /// </summary>
    /// <param name="followUri">The AT-URI of the follow record (from ViewerState.Following).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnfollowAsync(string followUri, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var parsed = AtUri.Parse(followUri);
        await Repo.DeleteRecordAsync(
            parsed.Repo, parsed.Collection!, parsed.RecordKey!, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a post.
    /// </summary>
    /// <param name="postUri">The AT-URI of the post to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeletePostAsync(string postUri, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var parsed = AtUri.Parse(postUri);
        await Repo.DeleteRecordAsync(
            parsed.Repo, parsed.Collection!, parsed.RecordKey!, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update the authenticated user's profile.
    /// </summary>
    /// <param name="displayName">New display name (null = no change).</param>
    /// <param name="description">New description/bio (null = no change).</param>
    /// <param name="avatar">New avatar blob (null = no change).</param>
    /// <param name="banner">New banner blob (null = no change).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateProfileAsync(
        string? displayName = null,
        string? description = null,
        BlobRef? avatar = null,
        BlobRef? banner = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        // Read current profile record
        GetRecordResponse? existing = null;
        try
        {
            existing = await Repo.GetRecordAsync(
                _session!.Did, "app.bsky.actor.profile", "self", cancellationToken: cancellationToken);
        }
        catch (AtProtoHttpException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest
                                               && ex.ErrorType == "RecordNotFound")
        {
            // No existing profile; will create
        }

        ProfileRecord? current = null;
        if (existing?.Value is { } val)
            current = val.Deserialize<ProfileRecord>(AtProtoJsonDefaults.Options);

        var updated = new ProfileRecord
        {
            DisplayName = displayName ?? current?.DisplayName,
            Description = description ?? current?.Description,
            Avatar = avatar ?? current?.Avatar,
            Banner = banner ?? current?.Banner,
            CreatedAt = current?.CreatedAt ?? AtProtoJsonDefaults.NowTimestamp(),
        };

        await Repo.PutRecordAsync(
            _session!.Did, "app.bsky.actor.profile", "self", updated,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────

    private async Task ApplySessionAsync(Session session)
    {
        _session = session;
        _xrpc.SetTokens(session.AccessJwt, session.RefreshJwt);
        await _sessionStore.SaveAsync(session);

        // Schedule a refresh 5 minutes before the access token expires.
        // Access tokens are typically valid for ~2 hours.
        StartRefreshTimer(TimeSpan.FromMinutes(115));
    }

    private void StartRefreshTimer(TimeSpan delay)
    {
        _refreshTimer?.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async void OnRefreshTimerElapsed(object? state)
    {
        // Cheap pre-check: bail if Dispose has already run. Volatile read so the
        // result is current across cores. The post-lock recheck below covers
        // the case where Dispose interleaves between this check and the wait.
        if (_disposed) return;

        // WaitAsync(0) can throw ObjectDisposedException if Dispose ran between
        // the timer firing and this code; treat that as "client is gone, drop".
        bool acquired;
        try
        {
            acquired = await _refreshLock.WaitAsync(0);
        }
        catch (ObjectDisposedException) { return; }

        if (!acquired)
        {
            _logger.LogDebug("Session refresh already in progress, skipping");
            return;
        }

        try
        {
            // Recheck after acquiring the lock — Dispose could have raced
            // between our pre-check and the wait, and we don't want to refresh
            // (and persist) tokens for a client whose shutdown has been signaled.
            if (_disposed) return;

            // Bound the timer-driven refresh so a slow/unresponsive token endpoint
            // doesn't pin _refreshLock forever, blocking foreground LogoutAsync /
            // ApplyOAuthSessionAsync that share the lock.
            using var cts = new CancellationTokenSource(_refreshTimerDeadline);
            await RefreshSessionUnlockedAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic session refresh failed");
        }
        finally
        {
            // Dispose could have raced ahead — guard the release.
            try { _refreshLock.Release(); }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }
        }
    }

    internal void EnsureAuthenticated()
    {
        if (_session is null)
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
    }

    // ──────────────────────────────────────────────────────────
    //  Disposal
    // ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drain any in-flight timer callbacks BEFORE disposing _refreshLock or
        // _oauthSession — Timer.Dispose() (no-arg) returns immediately without
        // waiting for callbacks, leaving a fire-in-progress refresh holding the
        // lock and dereferencing the session we're about to dispose. The
        // WaitHandle overload signals when all callbacks have drained.
        if (_refreshTimer is not null)
        {
            using var waitHandle = new System.Threading.ManualResetEvent(false);
            if (_refreshTimer.Dispose(waitHandle))
            {
                // Cap the wait at the timer-callback deadline (callback bounds
                // its own work with the same value), then fall through. The
                // callback's Release is wrapped in try/catch so even if it lands
                // after the lock is disposed, it won't escape — but a callback
                // that exceeds its own deadline is investigation-worthy, so log.
                if (!waitHandle.WaitOne(_refreshTimerDeadline + TimeSpan.FromSeconds(5)))
                {
                    _logger.LogWarning(
                        "Refresh-timer callback did not drain within {Timeout}s during Dispose; " +
                        "proceeding with lock/session teardown. Late callback completion is " +
                        "guarded but may produce harmless ObjectDisposedException log noise.",
                        (_refreshTimerDeadline + TimeSpan.FromSeconds(5)).TotalSeconds);
                }
            }
        }

        _oauthSession?.Dispose();
        _refreshLock.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Timer.DisposeAsync waits for in-flight callbacks, so the lock and
        // session are safe to dispose afterward.
        if (_refreshTimer is not null)
            await _refreshTimer.DisposeAsync();
        _oauthSession?.Dispose();
        _refreshLock.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

/// <summary>
/// Groups the Bluesky application sub-clients.
/// </summary>
public sealed class BlueskyClients
{
    internal BlueskyClients(
        ActorClient actor,
        FeedClient feed,
        GraphClient graph,
        LabelerClient labeler,
        NotificationClient notification,
        VideoClient video)
    {
        Actor = actor;
        Feed = feed;
        Graph = graph;
        Labeler = labeler;
        Notification = notification;
        Video = video;
    }

    /// <summary>app.bsky.actor.* — profiles, preferences, search.</summary>
    public ActorClient Actor { get; }

    /// <summary>app.bsky.feed.* — timelines, posts, likes, reposts.</summary>
    public FeedClient Feed { get; }

    /// <summary>app.bsky.graph.* — follows, blocks, mutes, lists.</summary>
    public GraphClient Graph { get; }

    /// <summary>app.bsky.labeler.* — labeler service information and definitions.</summary>
    public LabelerClient Labeler { get; }

    /// <summary>app.bsky.notification.* — notifications.</summary>
    public NotificationClient Notification { get; }

    /// <summary>app.bsky.video.* — video upload, processing, limits.</summary>
    public VideoClient Video { get; }
}

/// <summary>
/// Groups the Bluesky Chat sub-clients.
/// All requests are automatically proxied to the chat service via the <c>atproto-proxy</c> header.
/// Requires the <c>transition:chat.bsky</c> OAuth scope.
/// </summary>
public sealed class ChatClients
{
    internal ChatClients(ConvoClient convo, ChatActorClient actor)
    {
        Convo = convo;
        Actor = actor;
    }

    /// <summary>chat.bsky.convo.* — conversations, messages, reactions.</summary>
    public ConvoClient Convo { get; }

    /// <summary>chat.bsky.actor.* — chat account management.</summary>
    public ChatActorClient Actor { get; }
}

/// <summary>
/// Configuration options for <see cref="AtProtoClient"/>.
/// </summary>
public sealed class AtProtoClientOptions
{
    /// <summary>
    /// The base URL of the PDS or service instance.
    /// Default: "https://bsky.social"
    /// </summary>
    /// <remarks>
    /// With OAuth, this can be overridden dynamically via <see cref="AtProtoClient.SetPdsUrl"/>
    /// or automatically when applying an OAuth session.
    /// </remarks>
    public string InstanceUrl { get; set; } = "https://bsky.social";

    /// <summary>
    /// Whether to automatically refresh the session before the access token expires.
    /// Default: true.
    /// </summary>
    public bool AutoRefreshSession { get; set; } = true;

    /// <summary>
    /// OAuth configuration options. When set, enables OAuth authentication support.
    /// </summary>
    public OAuthOptions? OAuth { get; set; }

    /// <summary>
    /// The WebSocket URL of the relay service for firehose subscriptions.
    /// Default: "wss://bsky.network".
    /// Set to a custom URL to use a different relay, or <c>null</c> to disable
    /// the convenience <see cref="AtProtoClient.CreateFirehoseClient"/> and
    /// <see cref="AtProtoClient.CreateFirehoseConsumer"/> methods.
    /// </summary>
    public string? RelayUrl { get; set; } = "wss://bsky.network";
}
