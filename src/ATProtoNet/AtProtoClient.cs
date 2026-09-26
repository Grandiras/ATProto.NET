using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.AgeAssurance;
using ATProtoNet.Lexicon.App.Bsky.Bookmark;
using ATProtoNet.Lexicon.App.Bsky.Draft;
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
using ATProtoNet.Lexicon.App.Bsky.Unspecced;
using ATProtoNet.Lexicon.App.Bsky.Video;
using ATProtoNet.Lexicon.Com.AtProto.Admin;
using ATProtoNet.Lexicon.Com.AtProto.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Lexicon.Com.AtProto.Temp;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Tools.Ozone;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet;

/// <summary>
/// The main AT Protocol client. Build custom AT Protocol applications,
/// or interact with Bluesky and any atproto-compatible service.
/// </summary>
/// <remarks>
/// <para>Construct it with <see cref="AtProtoClient(AtProtoClientOptions?, HttpClient?, IAtProtoSessionStore?, ILogger{AtProtoClient}?)"/>
/// or register it via dependency injection with <c>services.AddAtProto()</c>.</para>
/// <para>After construction, call <see cref="LoginAsync"/> to authenticate, then use
/// <see cref="GetCollection{T}()"/> for typed CRUD on your custom Lexicon records,
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
/// var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://my-pds.example.com" });
///
/// await client.LoginAsync("alice.example.com", "app-password");
///
/// var todos = client.GetCollection&lt;TodoItem&gt;();
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
    private int _disposed;

    // ──────────────────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a client.
    /// </summary>
    /// <param name="options">
    /// The client options; <see langword="null"/> uses the defaults, which address
    /// <c>https://bsky.social</c>.
    /// </param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> to send with; <see langword="null"/> uses one the client owns.
    /// A supplied one is never disposed or modified, so it may be shared.
    /// </param>
    /// <param name="sessionStore">
    /// Where to persist the session, if anywhere. The client writes every session it installs or
    /// refreshes and removes it on sign-out; it does not dispose the store.
    /// </param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentException"><see cref="AtProtoClientOptions.InstanceUrl"/> is not an absolute URL.</exception>
    public AtProtoClient(
        AtProtoClientOptions? options = null,
        HttpClient? httpClient = null,
        IAtProtoSessionStore? sessionStore = null,
        ILogger<AtProtoClient>? logger = null)
        : this(options ?? new AtProtoClientOptions(), httpClient, sessionStore, logger, TimeProvider.System)
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
        Temp = new TempClient(_xrpc);
        Lexicon = new LexiconClient(_xrpc);

        Bsky = new BlueskyClients(
            new ActorClient(_xrpc),
            new FeedClient(_xrpc),
            new GraphClient(_xrpc),
            new LabelerClient(_xrpc),
            new NotificationClient(_xrpc),
            new VideoClient(_xrpc),
            new AgeAssuranceClient(_xrpc),
            new BookmarkClient(_xrpc),
            new DraftClient(_xrpc),
            new EmbedClient(_xrpc),
            new UnspeccedClient(_xrpc));

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
            options.BackgroundRefresh,
            options.RefreshCoordinator);
        _xrpc.SessionHandler = _sessions;
        Bsky.Bind(this);
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

    /// <summary>
    /// com.atproto.temp.* — methods upstream marks temporary: handle availability, the signup
    /// queue, OAuth scope references.
    /// </summary>
    public TempClient Temp { get; }

    /// <summary>
    /// com.atproto.lexicon.* — Lexicon resolution through the service. To resolve and verify
    /// schemas locally, use <see cref="LexiconResolver"/>.
    /// </summary>
    public LexiconClient Lexicon { get; }

    /// <summary>app.bsky.* — Bluesky social application APIs.</summary>
    public BlueskyClients Bsky { get; }

    /// <summary>chat.bsky.* — Bluesky direct message / chat APIs (proxied to chat service).</summary>
    public ChatClients Chat { get; }

    /// <summary>tools.ozone.* — Ozone moderation service APIs.</summary>
    public OzoneClient Ozone { get; }

    /// <summary>site.standard.* — Standard.site long-form publishing APIs.</summary>
    public StandardSiteClient Site { get; }

    // ──────────────────────────────────────────────────────────
    //  Custom Lexicon support
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get a strongly-typed <see cref="RecordCollection{T}"/> for a record type that names its
    /// collection. This is the primary API for building custom AT Protocol applications.
    /// </summary>
    /// <typeparam name="T">Your record type, declaring its collection through <see cref="IAtProtoRecord"/>.</typeparam>
    /// <returns>A typed collection providing Create, Get, Find, Put, Delete, List, and Enumerate operations.</returns>
    /// <example>
    /// <code>
    /// var todos = client.GetCollection&lt;TodoItem&gt;();
    /// await todos.CreateAsync(new TodoItem { Title = "Example" });
    /// </code>
    /// </example>
    public RecordCollection<T> GetCollection<T>() where T : class, IAtProtoRecord =>
        new(this, T.Collection);

    /// <summary>
    /// Get a strongly-typed <see cref="RecordCollection{T}"/> for a collection chosen at run time.
    /// </summary>
    /// <typeparam name="T">
    /// The record type: an <see cref="AtProtoRecord"/>, or any serializable class.
    /// </typeparam>
    /// <param name="collection">The Lexicon NSID for the collection (e.g., "com.example.todo.item").</param>
    /// <returns>A typed collection providing Create, Get, Find, Put, Delete, List, and Enumerate operations.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="T"/> implements <see cref="IAtProtoRecord"/> and declares a different
    /// collection.
    /// </exception>
    /// <example>
    /// <code>
    /// var notes = client.GetCollection&lt;Note&gt;(Nsid.Parse(settings.NotesCollection));
    /// </code>
    /// </example>
    public RecordCollection<T> GetCollection<T>(Nsid collection) where T : class
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (RecordPaths.DeclaredCollection<T>() is { } declared && declared != collection)
        {
            throw new ArgumentException(
                $"{typeof(T).Name} is a {declared} record and cannot be stored in {collection}.",
                nameof(collection));
        }

        return new RecordCollection<T>(this, collection);
    }

    /// <summary>
    /// The XRPC transport the sub-clients send through: this client's service and session.
    /// Build a sub-client for a Lexicon the SDK does not ship on it, so its calls sign in,
    /// refresh and sign out with this client.
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed class TodoClient(IXrpcTransport transport)
    /// {
    ///     private static readonly Nsid ListItems = Nsid.Parse("com.example.todo.listItems");
    ///
    ///     public Task&lt;ListItemsOutput&gt; ListItemsAsync(int? limit = null, CancellationToken ct = default) =>
    ///         transport.QueryAsync&lt;ListItemsOutput&gt;(ListItems, new XrpcParams().Add("limit", limit), cancellationToken: ct);
    /// }
    ///
    /// var todo = new TodoClient(client.Transport);
    /// </code>
    /// </example>
    public IXrpcTransport Transport => _xrpc;

    /// <summary>
    /// Call a custom XRPC query (HTTP GET) endpoint defined by your Lexicon.
    /// </summary>
    /// <typeparam name="TOut">The expected output type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.listItems").</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    /// <exception cref="XrpcResponseFormatException">The response is not a <typeparamref name="TOut"/>.</exception>
    /// <example>
    /// <code>
    /// var result = await client.QueryAsync&lt;ListResult&gt;(
    ///     Nsid.Parse("com.example.todo.listItems"),
    ///     new XrpcParams { { "limit", 25 }, { "cursor", cursor } });
    /// </code>
    /// </example>
    public Task<TOut> QueryAsync<TOut>(
        Nsid nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Transport.QueryAsync<TOut>(nsid, parameters, options, cancellationToken);

    /// <summary>
    /// Call a custom XRPC query (HTTP GET) with its parameters given as an object: an anonymous
    /// type or a dictionary.
    /// </summary>
    /// <typeparam name="TOut">The expected output type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.listItems").</param>
    /// <param name="parameters">
    /// The parameters: each public property (or dictionary entry) is one, a sequence value is
    /// sent as a repeated key, timestamps go out as ISO 8601 UTC and enums by their JSON names.
    /// </param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The properties are read by reflection, so this overload is not trim-safe; the
    /// <see cref="XrpcParams"/> overload is.
    /// </remarks>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    /// <exception cref="XrpcResponseFormatException">The response is not a <typeparamref name="TOut"/>.</exception>
    [RequiresUnreferencedCode(XrpcParams.AnonymousParametersWarning)]
    public Task<TOut> QueryAsync<TOut>(
        Nsid nsid,
        object parameters,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Transport.QueryAsync<TOut>(nsid, XrpcParams.From(parameters), options, cancellationToken);

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) endpoint defined by your Lexicon and read its
    /// output.
    /// </summary>
    /// <typeparam name="TIn">The input type.</typeparam>
    /// <typeparam name="TOut">The expected output type.</typeparam>
    /// <param name="nsid">The method NSID (e.g., "com.example.todo.updateStatus").</param>
    /// <param name="input">The input, serialized as JSON; <see langword="null"/> sends none.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    /// <exception cref="XrpcResponseFormatException">The response is not a <typeparamref name="TOut"/>.</exception>
    /// <example>
    /// <code>
    /// var result = await client.ProcedureAsync&lt;UpdateStatusInput, StatusResult&gt;(
    ///     Nsid.Parse("com.example.todo.updateStatus"),
    ///     new UpdateStatusInput { Rkey = "abc", Status = "done" });
    /// </code>
    /// </example>
    public Task<TOut> ProcedureAsync<TIn, TOut>(
        Nsid nsid,
        TIn input,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Transport.ProcedureAsync<TIn, TOut>(nsid, input, parameters, options, cancellationToken);

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) that takes an input, ignoring any output.
    /// </summary>
    /// <typeparam name="TIn">The input type.</typeparam>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="input">The input, serialized as JSON; <see langword="null"/> sends none.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    public Task ProcedureAsync<TIn>(
        Nsid nsid,
        TIn input,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Transport.ProcedureAsync(nsid, input, parameters, options, cancellationToken);

    /// <summary>
    /// Call a custom XRPC procedure (HTTP POST) that takes no input, ignoring any output.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Optional per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Preferred over <see cref="ProcedureAsync{TIn}"/> when the second argument is an
    /// <see cref="XrpcParams"/>, which is never a procedure's input.
    /// </remarks>
    /// <exception cref="XrpcException">The service answered with an XRPC error.</exception>
    [OverloadResolutionPriority(1)]
    public Task ProcedureAsync(
        Nsid nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Transport.ProcedureAsync(nsid, parameters, options, cancellationToken);

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
    public void SetLabelers(params IEnumerable<string> labelerDids) => _xrpc.SetLabelers(labelerDids);

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
            "No session store is configured. Pass one to the AtProtoClient constructor.");

        var session = await store.GetAsync(did, cancellationToken);
        if (session is null)
            return false;

        await _sessions.InstallAsync(session, oauthClient, persist: false, cancellationToken);
        return true;
    }

    /// <summary>
    /// Installs a session read from the session store, as <see cref="TryRestoreSessionAsync"/>
    /// does, with its DPoP key already loaded: the server integration's client factory keeps the
    /// imported key of each account rather than importing it for every request.
    /// </summary>
    /// <param name="session">The stored session.</param>
    /// <param name="oauthClient">For an <see cref="OAuthSession"/>, the <see cref="OAuthClient"/> that issued it.</param>
    /// <param name="dpop">
    /// For an <see cref="OAuthSession"/>, a key object for its <see cref="OAuthSession.DPoPKey"/>,
    /// which the client takes over; <see langword="null"/> imports the key.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task InstallStoredSessionAsync(
        AtProtoSession session, OAuthClient? oauthClient, DPoPProofGenerator? dpop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        await _sessions.InstallAsync(session, oauthClient, persist: false, cancellationToken, dpop);
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
    //  Private helpers
    // ──────────────────────────────────────────────────────────

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>The logger, for the helpers that act on the client's behalf.</summary>
    internal ILogger Logger => _logger;

    /// <summary>
    /// The installed session, for a call that acts on the signed-in account; read once, so the
    /// caller works with one account even when the session changes meanwhile.
    /// </summary>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    internal AtProtoSession RequireSession() =>
        _sessions.Session ?? throw new XrpcAuthenticationException(
            XrpcErrors.AuthenticationRequired,
            "This call acts on the signed-in account, and no session is installed. Sign in, or install a session, first.",
            HttpStatusCode.Unauthorized,
            nsid: null);

    /// <summary>The DID of the installed session's account.</summary>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    internal Did RequireDid() => RequireSession().Did;

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
public sealed partial class BlueskyClients
{
    internal BlueskyClients(
        ActorClient actor,
        FeedClient feed,
        GraphClient graph,
        LabelerClient labeler,
        NotificationClient notification,
        VideoClient video,
        AgeAssuranceClient ageAssurance,
        BookmarkClient bookmark,
        DraftClient draft,
        EmbedClient embed,
        UnspeccedClient unspecced)
    {
        Actor = actor;
        Feed = feed;
        Graph = graph;
        Labeler = labeler;
        Notification = notification;
        Video = video;
        AgeAssurance = ageAssurance;
        Bookmark = bookmark;
        Draft = draft;
        Embed = embed;
        Unspecced = unspecced;
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

    /// <summary>app.bsky.ageassurance.* — age assurance state and regional configuration.</summary>
    public AgeAssuranceClient AgeAssurance { get; }

    /// <summary>app.bsky.bookmark.* — the account's private bookmarks.</summary>
    public BookmarkClient Bookmark { get; }

    /// <summary>app.bsky.draft.* — the account's private post drafts.</summary>
    public DraftClient Draft { get; }

    /// <summary>app.bsky.embed.* — resolving records into external embed views.</summary>
    public EmbedClient Embed { get; }

    /// <summary>
    /// app.bsky.unspecced.* — endpoints the Bluesky app uses before they are specified, which
    /// may change without notice.
    /// </summary>
    public UnspeccedClient Unspecced { get; }
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
    /// Coordinates this client's refreshes with the other clients that share its session store,
    /// so an account's single-use refresh token is spent once (see
    /// <see cref="ISessionRefreshCoordinator"/>). Used only when the client has a session store.
    /// Default: <see langword="null"/>, for a client that is the only one acting for its account.
    /// </summary>
    /// <remarks>
    /// Give every client that shares the store the same coordinator. The server integration's
    /// client factory does this for its per-request clients.
    /// </remarks>
    public ISessionRefreshCoordinator? RefreshCoordinator { get; set; }
}
