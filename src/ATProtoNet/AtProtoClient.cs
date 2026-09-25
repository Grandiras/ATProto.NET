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
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using ATProtoNet.Lexicon.Chat.Bsky.Notification;
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
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Tools.Ozone;
using ATProtoNet.Models;
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
/// <para>The client is thread-safe: any number of requests may run concurrently, session
/// changes (sign-in, refresh, sign-out) are serialized, and concurrent callers that find the
/// access token expired share one refresh. See <c>docs/session-management.md</c> for the full
/// contract, including what the client disposes and what it leaves to you.</para>
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
    private readonly SessionManager _sessions;
    private readonly ILogger<AtProtoClient> _logger;
    private readonly string? _relayUrl;
    private int _disposed;

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
    /// <param name="options">The client options.</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> to send with; <see langword="null"/> uses one the client owns.
    /// A supplied one is never disposed or modified, so it may be shared.
    /// </param>
    /// <param name="sessionStore">
    /// Where to persist the session, if anywhere. The client writes every session it installs or
    /// refreshes and removes it on sign-out; it does not dispose the store.
    /// </param>
    /// <param name="logger">An optional logger.</param>
    public AtProtoClient(
        AtProtoClientOptions options,
        HttpClient? httpClient,
        IAtProtoSessionStore? sessionStore,
        ILogger<AtProtoClient>? logger)
        : this(options, httpClient, sessionStore, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Create a new client with full configuration and a clock, for tests.
    /// </summary>
    internal AtProtoClient(
        AtProtoClientOptions options,
        HttpClient? httpClient,
        IAtProtoSessionStore? sessionStore,
        ILogger<AtProtoClient>? logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.InstanceUrl);
        ArgumentNullException.ThrowIfNull(options.RateLimit);

        if (!Uri.TryCreate(options.InstanceUrl, UriKind.Absolute, out var instanceUrl))
            throw new ArgumentException($"'{options.InstanceUrl}' is not an absolute URL.", nameof(options));

        _logger = logger ?? NullLogger<AtProtoClient>.Instance;

        // A supplied HttpClient is only sent through: its BaseAddress and default headers are
        // neither read nor written, so one client can serve any number of AtProtoClients.
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? AtProtoHttp.CreateClient();

        // The configured URL is trusted as given (plain HTTP to a private host included);
        // SetServiceUrl, fed at runtime from DID documents and OAuth sessions, is stricter.
        _xrpc = new XrpcClient(
            _httpClient,
            AtProtoHttp.ValidateServiceUrl(instanceUrl, nameof(options), allowInsecure: true),
            _logger)
        {
            UserAgent = options.UserAgent,
            RateLimit = options.RateLimit,
        };

        Server = new ServerClient(_xrpc);
        Repo = new RepoClient(_xrpc);
        Identity = new IdentityClient(_xrpc);
        Sync = new SyncClient(_xrpc);
        Admin = new AdminClient(_xrpc);
        Label = new LabelClient(_xrpc);
        Moderation = new ModerationClient(_xrpc);
        Space = new SpaceClient(_xrpc);
        SimpleSpace = new SimpleSpaceClient(_xrpc);

        Bsky = new BlueskyClients(
            new ActorClient(_xrpc),
            new FeedClient(_xrpc),
            new GraphClient(_xrpc),
            new LabelerClient(_xrpc),
            new NotificationClient(_xrpc),
            new VideoClient(_xrpc));

        // Chat sub-clients (automatically proxied to chat service)
        Chat = new ChatClients(
            new ConvoClient(_xrpc),
            new ChatActorClient(_xrpc),
            new GroupClient(_xrpc),
            new ChatNotificationClient(_xrpc),
            new ChatModerationClient(_xrpc));

        Ozone = new OzoneClient(_xrpc);
        Site = new StandardSiteClient(Repo);

        _sessions = new SessionManager(
            _xrpc,
            Server,
            sessionStore,
            change => SessionChanged?.Invoke(this, change),
            _logger,
            timeProvider,
            options.AutoRefreshSession,
            options.BackgroundRefresh);
        _xrpc.SessionHandler = _sessions;

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

    /// <summary>
    /// com.atproto.space.* — the permissioned data protocol: spaces, permissioned repos,
    /// and their sync.
    /// </summary>
    public SpaceClient Space { get; }

    /// <summary>
    /// com.atproto.simplespace.* — the space-management implementation every PDS supports.
    /// </summary>
    public SimpleSpaceClient SimpleSpace { get; }

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
    public Streaming.FirehoseClient CreateFirehoseClient() =>
        new(RequireRelayUrl(), _logger);

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
        int maxReconnectAttempts = 10) =>
        new(RequireRelayUrl(), _logger, reconnectDelay, maxReconnectAttempts);

    private string RequireRelayUrl() =>
        string.IsNullOrEmpty(_relayUrl)
            ? throw new InvalidOperationException(
                "No relay URL configured. Set AtProtoClientOptions.RelayUrl or use AtProtoClientBuilder.WithRelayUrl().")
            : _relayUrl;

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
    /// var todos = client.GetCollection&lt;TodoItem&gt;(Nsid.Parse("com.example.todo.item"));
    /// await todos.CreateAsync(new TodoItem { Title = "Example" });
    /// </code>
    /// </example>
    public RecordCollection<T> GetCollection<T>(Nsid collection) where T : class
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new RecordCollection<T>(this, collection);
    }

    /// <summary>
    /// Call a custom XRPC query (HTTP GET) endpoint defined by your Lexicon.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.listItems").</param>
    /// <param name="parameters">
    /// Optional query parameters as an anonymous object or a dictionary. A sequence value is
    /// sent as a repeated key; timestamps go out as ISO 8601 UTC and enums by their JSON names.
    /// </param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    /// <exception cref="XrpcResponseFormatException">The response is not a <typeparamref name="T"/>.</exception>
    /// <example>
    /// <code>
    /// var result = await client.QueryAsync&lt;ListResult&gt;(
    ///     Nsid.Parse("com.example.todo.listItems"),
    ///     new { limit = 25, cursor = "abc" });
    /// </code>
    /// </example>
    public Task<T> QueryAsync<T>(
        Nsid nsid,
        object? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        return _xrpc.QueryAsync<T>(nsid.Value, XrpcParams.From(parameters), options, cancellationToken);
    }

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) endpoint defined by your Lexicon.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.updateStatus").</param>
    /// <param name="body">The request body, serialized as JSON; <see langword="null"/> sends none.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    /// <exception cref="XrpcResponseFormatException">The response is not a <typeparamref name="T"/>.</exception>
    /// <example>
    /// <code>
    /// var result = await client.ProcedureAsync&lt;StatusResult&gt;(
    ///     Nsid.Parse("com.example.todo.updateStatus"),
    ///     new { rkey = "abc", status = "done" });
    /// </code>
    /// </example>
    public Task<T> ProcedureAsync<T>(
        Nsid nsid,
        object? body = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(nsid);
        return _xrpc.ProcedureAsync<T>(nsid.Value, body, parameters: null, options, cancellationToken);
    }

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) that returns no response body.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="body">The request body, serialized as JSON; <see langword="null"/> sends none.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    public Task ProcedureAsync(
        Nsid nsid,
        object? body = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        return _xrpc.ProcedureAsync(nsid.Value, body, parameters: null, options, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Session state
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// The installed session — a <see cref="PasswordSession"/> or an <see cref="OAuthSession"/> —
    /// or <see langword="null"/> when signed out. Each refresh replaces it with a new value.
    /// </summary>
    public AtProtoSession? Session => _sessions.Session;

    /// <summary>Whether a session is installed.</summary>
    public bool IsAuthenticated => Session is not null;

    /// <summary>The DID of the authenticated account, or null.</summary>
    public Did? Did => Session?.Did;

    /// <summary>The handle of the authenticated account, or null.</summary>
    public Handle? Handle => Session?.Handle;

    /// <summary>
    /// Raised after the session changes: installed (<see cref="AtProtoSessionChange.Created"/>),
    /// refreshed, expired because its refresh token was refused, or signed out.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously on the thread that made the change, after the client has
    /// released its session lock, so a handler may call back into the client. An exception a
    /// handler throws is logged, not propagated. To persist sessions, prefer an
    /// <see cref="IAtProtoSessionStore"/>, which the client awaits before it moves on.
    /// </remarks>
    public event EventHandler<AtProtoSessionChangedEventArgs>? SessionChanged;

    /// <summary>
    /// The latest repository revision (TID) received from the service via the
    /// <c>Atproto-Repo-Rev</c> response header. Indicates how up-to-date
    /// the service is with the authenticated account's repository. A header value that is not a
    /// TID reads as <see langword="null"/>.
    /// </summary>
    public Tid? LatestRepoRev => Tid.TryParse(_xrpc.LatestRepoRev, out var rev) ? rev : null;

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
    /// <remarks>
    /// This is a client-wide default, applied to authenticated and unauthenticated calls alike
    /// (but not to the session calls — sign-in, refresh, sign-out — which address the PDS
    /// itself). To vary it per call on a client shared between callers, pass
    /// <see cref="XrpcCallOptions.Proxy"/> instead of changing the default.
    /// </remarks>
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
    /// <remarks>
    /// A client-wide default, sent with or without a session. To vary it per call, pass
    /// <see cref="XrpcCallOptions.AcceptLabelers"/> instead.
    /// </remarks>
    /// <param name="labelerDids">
    /// The DIDs of labeler services to subscribe to, each optionally followed by the
    /// <c>;redact</c> parameter, as the header carries them.
    /// </param>
    public void SetLabelers(IEnumerable<string> labelerDids) => _xrpc.SetLabelers(labelerDids);

    /// <summary>
    /// Sets the subscribed labeler DIDs from string parameters.
    /// </summary>
    /// <param name="labelerDids">
    /// The DIDs of labeler services to subscribe to, each optionally followed by the
    /// <c>;redact</c> parameter, as the header carries them.
    /// </param>
    public void SetLabelers(params string[] labelerDids) => _xrpc.SetLabelers(labelerDids);

    /// <summary>
    /// Clears the subscribed labeler DIDs, removing the <c>atproto-accept-labelers</c> header.
    /// </summary>
    public void ClearLabelers() => _xrpc.ClearLabelers();

    // ──────────────────────────────────────────────────────────
    //  Authentication
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sign in with a password or app password (<c>com.atproto.server.createSession</c>) and
    /// install the session.
    /// </summary>
    /// <param name="identifier">The account's handle, DID or email address.</param>
    /// <param name="password">The password or app password.</param>
    /// <param name="authFactorToken">The emailed second-factor token, when the account needs one.</param>
    /// <param name="allowTakendown">
    /// Let a taken-down account sign in (the Lexicon's <c>allowTakendown</c>), to a session the
    /// service limits to migrating or exporting the account. Without it the service refuses such
    /// an account with <see cref="XrpcErrors.AccountTakedown"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed session.</returns>
    /// <remarks>
    /// The request goes to <see cref="ServiceUrl"/>. When that is an entryway such as
    /// <c>bsky.social</c>, the service answers with the account's DID document, and the client
    /// moves to the PDS it names (HTTPS services only).
    /// </remarks>
    /// <exception cref="XrpcException">
    /// The service refused the sign-in; <see cref="XrpcErrors.AuthFactorTokenRequired"/> asks for
    /// <paramref name="authFactorToken"/>.
    /// </exception>
    public async Task<PasswordSession> LoginAsync(
        string identifier,
        string password,
        string? authFactorToken = null,
        bool allowTakendown = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ThrowIfDisposed();
        _logger.LogInformation("Logging in as {Identifier}", identifier);

        var response = await Server.CreateSessionAsync(
            identifier, password, authFactorToken, allowTakendown, cancellationToken);

        var session = new PasswordSession
        {
            Did = response.Did,
            Handle = response.Handle,
            ServiceEndpoint = SessionManager.ResolveServiceEndpoint(response.DidDoc, response.Did, _xrpc.ServiceUrl),
            AccessJwt = response.AccessJwt,
            RefreshJwt = response.RefreshJwt,
            ExpiresAt = SessionManager.ReadJwtExpiry(response.AccessJwt),
            Email = response.Email,
            EmailConfirmed = response.EmailConfirmed,
            EmailAuthFactor = response.EmailAuthFactor,
            Active = response.Active,
            Status = response.Status,
        };

        await _sessions.InstallAsync(session, oauthClient: null, persist: true, cancellationToken);
        _logger.LogInformation("Logged in successfully as {Handle} ({Did})", session.Handle, session.Did);
        return session;
    }

    /// <summary>
    /// Create an account (<c>com.atproto.server.createAccount</c>) and install the session the
    /// service returns for it.
    /// </summary>
    /// <param name="request">The account to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new account's session.</returns>
    /// <remarks>
    /// <see cref="ServerClient.CreateAccountAsync"/> creates the account without signing in; this
    /// is that call plus <see cref="ApplySessionAsync"/>.
    /// </remarks>
    public async Task<PasswordSession> CreateAccountAndLoginAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        var response = await Server.CreateAccountAsync(request, cancellationToken);

        var session = new PasswordSession
        {
            Did = response.Did,
            Handle = response.Handle,
            ServiceEndpoint = SessionManager.ResolveServiceEndpoint(response.DidDoc, response.Did, _xrpc.ServiceUrl),
            AccessJwt = response.AccessJwt,
            RefreshJwt = response.RefreshJwt,
            ExpiresAt = SessionManager.ReadJwtExpiry(response.AccessJwt),
            Email = request.Email,
        };

        await _sessions.InstallAsync(session, oauthClient: null, persist: true, cancellationToken);
        _logger.LogInformation("Created account {Handle} ({Did})", session.Handle, session.Did);
        return session;
    }

    /// <summary>
    /// Install a session you already hold — from <see cref="OAuthClient.CompleteAuthorizationAsync"/>,
    /// or saved earlier — as it is, without contacting the service.
    /// </summary>
    /// <param name="session">The session. It replaces any installed session, of either kind.</param>
    /// <param name="oauthClient">
    /// For an <see cref="OAuthSession"/>, the <see cref="OAuthClient"/> that issued it, which
    /// refreshes and revokes it. When omitted, the one given with the previous OAuth session is
    /// kept; without any, the session works until its access token expires. The client does not
    /// take ownership of it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The client points itself at <see cref="AtProtoSession.ServiceEndpoint"/> and writes the
    /// session to its session store, if it has one. An access token that has expired is
    /// refreshed on the first request.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The session's service endpoint is not HTTPS (or loopback), or its DPoP key is not a P-256
    /// PKCS#8 key. The client is left as it was.
    /// </exception>
    public async Task ApplySessionAsync(
        AtProtoSession session,
        OAuthClient? oauthClient = null,
        CancellationToken cancellationToken = default) =>
        await _sessions.InstallAsync(session, oauthClient, persist: true, cancellationToken);

    /// <summary>
    /// Install a saved session and check it with the service (<c>com.atproto.server.getSession</c>),
    /// refreshing it if the access token has expired.
    /// </summary>
    /// <param name="session">The saved session.</param>
    /// <param name="oauthClient">For an <see cref="OAuthSession"/>, the <see cref="OAuthClient"/> that issued it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session now installed: refreshed if it had to be, and for a password session with
    /// the account details the service reported.
    /// </returns>
    /// <exception cref="XrpcAuthenticationException">
    /// The service rejected the session (after a refresh, where one applied): it has been removed
    /// from the client and the store, and <see cref="SessionChanged"/> reports it
    /// <see cref="AtProtoSessionChange.Expired"/>.
    /// </exception>
    /// <exception cref="OAuthException">
    /// An OAuth session's refresh failed. On <c>invalid_grant</c> it has been removed, as above.
    /// </exception>
    /// <remarks>
    /// Any other failure, such as the service being unreachable, is thrown with the session left
    /// installed and stored, so it can be used once the service answers again.
    /// </remarks>
    public async Task<AtProtoSession> ResumeSessionAsync(
        AtProtoSession session,
        OAuthClient? oauthClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _logger.LogInformation("Resuming session for {Did}", session.Did);

        var installed = await _sessions.InstallAsync(session, oauthClient, persist: true, cancellationToken);

        // Through the ordinary pipeline, so an expired access token is refreshed and the call resent.
        GetSessionResponse account;
        try
        {
            account = await Server.GetSessionAsync(cancellationToken);
        }
        catch (XrpcAuthenticationException ex)
        {
            // Still rejected (InvalidToken, or refused again after a refresh): the session is dead.
            await _sessions.ExpireAsync(installed, ex);
            throw;
        }

        var current = await _sessions.UpdateAccountAsync(account, cancellationToken)
            ?? throw new InvalidOperationException("The session was signed out while it was being resumed.");

        _logger.LogInformation("Session resumed successfully for {Handle}", current.Handle);
        return current;
    }

    /// <summary>
    /// Install the session of <paramref name="did"/> from the session store, if it holds one,
    /// without contacting the service.
    /// </summary>
    /// <param name="did">The account to restore.</param>
    /// <param name="oauthClient">For an <see cref="OAuthSession"/>, the <see cref="OAuthClient"/> that issued it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a session was found and installed.</returns>
    /// <exception cref="InvalidOperationException">The client has no session store.</exception>
    public async Task<bool> TryRestoreSessionAsync(
        Did did,
        OAuthClient? oauthClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        ThrowIfDisposed();

        var store = _sessions.Store ?? throw new InvalidOperationException(
            "No session store is configured. Pass one to the constructor or AtProtoClientBuilder.WithSessionStore.");

        var session = await store.GetAsync(did, cancellationToken);
        if (session is null)
            return false;

        await _sessions.InstallAsync(session, oauthClient, persist: false, cancellationToken);
        return true;
    }

    /// <summary>
    /// Refresh the session's tokens now: a password session through
    /// <c>com.atproto.server.refreshSession</c>, an OAuth session through its authorization
    /// server. Calls made concurrently share one refresh.
    /// </summary>
    /// <remarks>
    /// With <see cref="AtProtoClientOptions.AutoRefreshSession"/> on (the default) this is
    /// rarely needed: the client refreshes before the access token expires, and again when the
    /// service reports it expired.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No session is installed.</exception>
    /// <exception cref="XrpcAuthenticationException">
    /// The PDS refused the refresh token; the session has expired and been removed.
    /// </exception>
    /// <exception cref="OAuthException">
    /// The authorization server refused the refresh. On <c>invalid_grant</c> the session has
    /// expired and been removed.
    /// </exception>
    public Task RefreshSessionAsync(CancellationToken cancellationToken = default) =>
        _sessions.RefreshAsync(cancellationToken);

    /// <summary>
    /// Sign out: remove the session from the client and its store, then end it at the service —
    /// <c>com.atproto.server.deleteSession</c> for a password session, token revocation
    /// (RFC 7009) for an OAuth session.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the wait for the session lock and the call to the service; the local teardown and
    /// the store removal complete regardless.
    /// </param>
    /// <remarks>
    /// The local teardown always happens, and <see cref="SessionChanged"/> reports
    /// <see cref="AtProtoSessionChange.Removed"/>. A failure after that — the service
    /// unreachable, the store refusing the removal, an OAuth session installed without the
    /// <see cref="OAuthClient"/> that could revoke it (<see cref="InvalidOperationException"/>) —
    /// is then thrown, because a session left active at the service is worth knowing about. A
    /// token the service already considers invalid is not a failure.
    /// </remarks>
    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _sessions.LogoutAsync(cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Dynamic PDS
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Points the client at another service — typically the user's PDS — at runtime. Call this
    /// before <see cref="LoginAsync"/> when the user selects a different PDS; installing a
    /// session moves the client to the session's service for you.
    /// </summary>
    /// <remarks>
    /// Safe on a client that has already sent requests, and on an <see cref="HttpClient"/> shared
    /// with other clients: the URL is held by this client, not written to the HttpClient.
    /// </remarks>
    /// <param name="serviceUrl">The new service URL (e.g., <c>https://pds.example.com</c>).</param>
    /// <exception cref="ArgumentException">
    /// The URL is not HTTPS and not a loopback address: the session's tokens are sent wherever
    /// it points.
    /// </exception>
    public void SetServiceUrl(Uri serviceUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceUrl);
        _xrpc.SetServiceUrl(serviceUrl);
        _logger.LogInformation("Switched service to {ServiceUrl}", _xrpc.ServiceUrl);
    }

    /// <summary>
    /// The service this client sends requests to: <see cref="AtProtoClientOptions.InstanceUrl"/>
    /// until <see cref="SetServiceUrl"/> or an installed session changes it.
    /// </summary>
    public Uri ServiceUrl => _xrpc.ServiceUrl;

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
        IEnumerable<Facet>? facets = null,
        EmbedBase? embed = null,
        ReplyRef? reply = null,
        IEnumerable<string>? langs = null,
        SelfLabels? labels = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var post = new PostRecord
        {
            Text = text,
            Facets = facets?.ToList(),
            Embed = embed,
            Reply = reply,
            Langs = langs?.ToList(),
            Labels = labels,
            CreatedAt = AtDatetime.Now(),
        };

        return await Repo.CreateRecordAsync(
            _sessions.Session!.Did, PostCollection, post, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Like a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">The CID of the post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateRecordResponse> LikeAsync(
        AtUri uri, Cid cid, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var like = new LikeRecord
        {
            Subject = new StrongRef { Uri = uri, Cid = cid },
            CreatedAt = AtDatetime.Now(),
        };

        return await Repo.CreateRecordAsync(
            _sessions.Session!.Did, LikeCollection, like, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unlike a post (delete the like record).
    /// </summary>
    /// <param name="likeUri">The AT-URI of the like record (from PostViewerState.Like).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnlikeAsync(AtUri likeUri, CancellationToken cancellationToken = default)
    {
        await DeleteByUriAsync(likeUri, cancellationToken);
    }

    /// <summary>
    /// Repost a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">The CID of the post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateRecordResponse> RepostAsync(
        AtUri uri, Cid cid, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var repost = new RepostRecord
        {
            Subject = new StrongRef { Uri = uri, Cid = cid },
            CreatedAt = AtDatetime.Now(),
        };

        return await Repo.CreateRecordAsync(
            _sessions.Session!.Did, RepostCollection, repost, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Undo a repost.
    /// </summary>
    /// <param name="repostUri">The AT-URI of the repost record (from PostViewerState.Repost).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UndoRepostAsync(AtUri repostUri, CancellationToken cancellationToken = default)
    {
        await DeleteByUriAsync(repostUri, cancellationToken);
    }

    /// <summary>
    /// Follow an actor.
    /// </summary>
    /// <param name="did">The DID of the actor to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateRecordResponse> FollowAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var follow = new FollowRecord
        {
            Subject = did,
            CreatedAt = AtDatetime.Now(),
        };

        return await Repo.CreateRecordAsync(
            _sessions.Session!.Did, FollowCollection, follow, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unfollow an actor.
    /// </summary>
    /// <param name="followUri">The AT-URI of the follow record (from ViewerState.Following).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnfollowAsync(AtUri followUri, CancellationToken cancellationToken = default)
    {
        await DeleteByUriAsync(followUri, cancellationToken);
    }

    /// <summary>
    /// Delete a post.
    /// </summary>
    /// <param name="postUri">The AT-URI of the post to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeletePostAsync(AtUri postUri, CancellationToken cancellationToken = default)
    {
        await DeleteByUriAsync(postUri, cancellationToken);
    }

    /// <summary>
    /// Update the authenticated user's profile: read the current record, let
    /// <paramref name="update"/> edit it, and write it back.
    /// </summary>
    /// <param name="update">
    /// Edits the current profile in place, for example <c>p =&gt; p.DisplayName = "Alice"</c>.
    /// Setting a property to <see langword="null"/> removes the field. It may run more than once
    /// (see remarks), each time on a freshly read record.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the written profile record.</returns>
    /// <remarks>
    /// <para>Every field <paramref name="update"/> leaves alone is written back unchanged,
    /// including fields this SDK version does not model (kept in
    /// <see cref="LexObject.ExtensionData"/>).</para>
    /// <para>The write is guarded by <c>swapRecord</c>, so a concurrent edit from another client is
    /// never silently overwritten. If one lands between the read and the write, the whole
    /// read-edit-write is retried, up to three attempts in total, after which the
    /// <see cref="XrpcErrors.InvalidSwap"/> <see cref="XrpcException"/> is rethrown. When the account has no
    /// profile yet, <paramref name="update"/> receives an empty record with <c>createdAt</c> set,
    /// and the write carries no swap guard.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client is not authenticated.</exception>
    /// <exception cref="XrpcResponseFormatException">The stored profile is not a valid profile record.</exception>
    public async Task<RecordRef> UpdateProfileAsync(
        Action<ProfileRecord> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        EnsureAuthenticated();
        var did = _sessions.Session!.Did;

        for (var attempt = 1; ; attempt++)
        {
            var (profile, cid) = await GetProfileRecordAsync(did, cancellationToken);
            update(profile);

            try
            {
                var written = await Repo.PutRecordAsync(
                    did, ProfileCollection, RecordKey.Self, profile,
                    swapRecord: cid,
                    cancellationToken: cancellationToken);

                return RecordRef.From("com.atproto.repo.putRecord", written.Uri, written.Cid);
            }
            catch (XrpcException ex) when (ex.Is(XrpcErrors.InvalidSwap)
                                           && attempt < MaxProfileUpdateAttempts)
            {
                _logger.LogDebug(
                    "Profile changed concurrently (attempt {Attempt} of {Max}); re-reading",
                    attempt, MaxProfileUpdateAttempts);
            }
        }
    }

    private static readonly Nsid PostCollection = Nsid.Parse("app.bsky.feed.post");
    private static readonly Nsid LikeCollection = Nsid.Parse("app.bsky.feed.like");
    private static readonly Nsid RepostCollection = Nsid.Parse("app.bsky.feed.repost");
    private static readonly Nsid FollowCollection = Nsid.Parse("app.bsky.graph.follow");
    private static readonly Nsid ProfileCollection = Nsid.Parse("app.bsky.actor.profile");
    private const int MaxProfileUpdateAttempts = 3;

    /// <summary>
    /// Reads the account's profile record and the CID to swap against, or a fresh record and
    /// <see langword="null"/> when there is none.
    /// </summary>
    private async Task<(ProfileRecord Profile, Cid? Cid)> GetProfileRecordAsync(
        Did did, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await Repo.GetRecordAsync<ProfileRecord>(
                did, ProfileCollection, RecordKey.Self, cancellationToken: cancellationToken);

            return (existing.Value, existing.Cid);
        }
        catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
        {
            return (new ProfileRecord { CreatedAt = AtDatetime.Now() }, null);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    internal void EnsureAuthenticated()
    {
        if (_sessions.Session is null)
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
    }

    /// <summary>
    /// Deletes the record an AT-URI points at. Backs the undo-style convenience
    /// methods (unlike, unfollow, undo repost, delete post), which differ only in
    /// which URI the caller hands over.
    /// </summary>
    private async Task DeleteByUriAsync(AtUri uri, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        await Repo.DeleteRecordAsync(uri, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Disposal
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Releases the client, first waiting for a token exchange already under way to finish and
    /// be stored (bounded by the exchange's 30-second limit). This is the preferred way to
    /// dispose it.
    /// </summary>
    /// <remarks>
    /// The session stays valid at the service (dispose is not sign-out; call
    /// <see cref="LogoutAsync"/> for that) and in the session store. The client disposes what it
    /// created — its own <see cref="HttpClient"/>, the DPoP key object it built from an OAuth
    /// session — and nothing it was given: a supplied <see cref="HttpClient"/>,
    /// <see cref="OAuthClient"/> or <see cref="IAtProtoSessionStore"/> stays usable.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _sessions.DisposeAsync();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <summary>
    /// Releases the client without waiting. A token exchange already under way still finishes
    /// and is stored in the background, since abandoning it could leave the store with a spent
    /// refresh token; the DPoP key it uses is released once it is done.
    /// </summary>
    /// <remarks>Prefer <see cref="DisposeAsync"/>; see there for what is and is not disposed.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _sessions.Dispose();
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
/// Their requests are automatically proxied to the chat service via the <c>atproto-proxy</c>
/// header, except <see cref="Moderation"/>'s. Requires the <c>transition:chat.bsky</c> OAuth scope.
/// </summary>
public sealed class ChatClients
{
    internal ChatClients(
        ConvoClient convo,
        ChatActorClient actor,
        GroupClient group,
        ChatNotificationClient notification,
        ChatModerationClient moderation)
    {
        Convo = convo;
        Actor = actor;
        Group = group;
        Notification = notification;
        Moderation = moderation;
    }

    /// <summary>chat.bsky.convo.* — conversations, direct and group, messages, reactions.</summary>
    public ConvoClient Convo { get; }

    /// <summary>chat.bsky.actor.* — chat status and account management.</summary>
    public ChatActorClient Actor { get; }

    /// <summary>chat.bsky.group.* — group conversations, members, join links and join requests.</summary>
    public GroupClient Group { get; }

    /// <summary>chat.bsky.notification.* — chat notification preferences.</summary>
    public ChatNotificationClient Notification { get; }

    /// <summary>chat.bsky.moderation.* — conversation lookups and chat access, for moderation services.</summary>
    public ChatModerationClient Moderation { get; }
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
    /// <para>This is always the service the client starts with, even when it is given an
    /// <see cref="HttpClient"/> that has a <see cref="HttpClient.BaseAddress"/>: the SDK
    /// ignores that property.</para>
    /// <para>It can be changed at runtime with <see cref="AtProtoClient.SetServiceUrl"/>, and is
    /// changed automatically when applying an OAuth session.</para>
    /// </remarks>
    public string InstanceUrl { get; set; } = "https://bsky.social";

    /// <summary>
    /// The <c>User-Agent</c> header sent with every request. Default:
    /// <c>ATProtoNet/&lt;version&gt;</c>. Set it to identify your application; set it to
    /// <see langword="null"/> to send none of the SDK's own, leaving any default of the
    /// <see cref="HttpClient"/> in place.
    /// </summary>
    /// <remarks>
    /// The header is set on each request rather than on the <see cref="HttpClient"/>, so
    /// clients sharing one <see cref="HttpClient"/> can each send their own.
    /// </remarks>
    public string? UserAgent { get; set; } = AtProtoHttp.DefaultUserAgent;

    /// <summary>
    /// How HTTP 429 (Too Many Requests) is retried: how many times, and the longest wait
    /// accepted before the call throws <see cref="XrpcRateLimitException"/> instead.
    /// </summary>
    public XrpcRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// Whether the client refreshes the session by itself: before a request when the access
    /// token is about to expire, and once more when the service answers that it has
    /// (<c>ExpiredToken</c>, or a DPoP <c>invalid_token</c> challenge), resending the request.
    /// Default: true.
    /// </summary>
    /// <remarks>
    /// With it off, requests carry whatever token is installed, and an expired one fails with
    /// <see cref="XrpcAuthenticationException"/> until <see cref="AtProtoClient.RefreshSessionAsync"/>
    /// is called.
    /// </remarks>
    public bool AutoRefreshSession { get; set; } = true;

    /// <summary>
    /// Whether to also refresh on a timer shortly before the access token expires, even when no
    /// request is being made, so an idle client's session (and its stored copy) stays current.
    /// Needs <see cref="AutoRefreshSession"/>. Default: false — refreshing on demand covers every
    /// request.
    /// </summary>
    public bool BackgroundRefresh { get; set; }

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
