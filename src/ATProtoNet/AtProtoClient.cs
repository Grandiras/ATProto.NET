using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Com.AtProto.Admin;
using ATProtoNet.Lexicon.Com.AtProto.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
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
    private Session? _session;
    private OAuthSessionResult? _oauthSession;
    private Timer? _refreshTimer;
    private bool _disposed;

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
            new NotificationClient(_xrpc, bskyLogger));

        if (options.AutoRefreshSession)
            _refreshTimer = new Timer(OnRefreshTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
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
        return await _xrpc.QueryAsync<T>($"xrpc/{nsid}", dict, cancellationToken);
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
            return await _xrpc.ProcedureAsync<object, T>($"xrpc/{nsid}", body, cancellationToken: cancellationToken);
        return await _xrpc.ProcedureAsync<T>($"xrpc/{nsid}", cancellationToken: cancellationToken);
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
            await _xrpc.ProcedureAsync<object>($"xrpc/{nsid}", body, cancellationToken: cancellationToken);
        else
            await _xrpc.ProcedureAsync($"xrpc/{nsid}", cancellationToken: cancellationToken);
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
    /// Refresh the current session tokens.
    /// </summary>
    public async Task RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_session?.RefreshJwt is null)
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
        if (_session is null && _oauthSession is null) return;

        _logger.LogInformation("Logging out {Did}", _session?.Did ?? _oauthSession?.Did);

        try
        {
            if (_session is not null)
                await Server.DeleteSessionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete session on server");
        }

        _xrpc.ClearTokens();
        _session = null;
        _oauthSession?.Dispose();
        _oauthSession = null;
        StopRefreshTimer();

        await _sessionStore.ClearAsync(cancellationToken);
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
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyOAuthSessionAsync(
        OAuthSessionResult oauthSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauthSession);

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
        try
        {
            await RefreshSessionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic session refresh failed");
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

        _refreshTimer?.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_refreshTimer is not null)
            await _refreshTimer.DisposeAsync();
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
        NotificationClient notification)
    {
        Actor = actor;
        Feed = feed;
        Graph = graph;
        Notification = notification;
    }

    /// <summary>app.bsky.actor.* — profiles, preferences, search.</summary>
    public ActorClient Actor { get; }

    /// <summary>app.bsky.feed.* — timelines, posts, likes, reposts.</summary>
    public FeedClient Feed { get; }

    /// <summary>app.bsky.graph.* — follows, blocks, mutes, lists.</summary>
    public GraphClient Graph { get; }

    /// <summary>app.bsky.notification.* — notifications.</summary>
    public NotificationClient Notification { get; }
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
}
