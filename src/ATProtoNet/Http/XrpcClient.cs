using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Http;

/// <summary>
/// The XRPC transport behind the Lexicon sub-clients: builds requests, applies the session's
/// credentials, retries DPoP nonce challenges and rate limits, and maps failures to
/// <see cref="XrpcException"/>.
/// </summary>
/// <remarks>
/// <para>It addresses one service, <see cref="ServiceUrl"/>, with absolute request URIs, and puts
/// every header on the request itself. The <see cref="HttpClient"/> is never mutated — not its
/// <see cref="HttpClient.BaseAddress"/>, not its <see cref="HttpClient.DefaultRequestHeaders"/> —
/// so any number of transports, hosts and sessions can share one client, and the service can
/// change after the client has sent requests.</para>
/// <para>The session state (tokens, DPoP key and nonce, client-wide proxy and labeler headers)
/// is held in fields replaced as a whole, so a request in flight on another thread sees either
/// the old or the new value, never a mix. The service URL and the session credentials are one
/// such value: a call reads them once, and every attempt of it goes to that service with those
/// credentials, so a session installed meanwhile is never sent to the previous one's service.</para>
/// <para>When a <see cref="SessionHandler"/> is attached, session-authenticated calls consult it:
/// before sending, so it can refresh a token about to expire, and once after the service rejects
/// the token, so it can refresh and have the call resent with the same account's new tokens.</para>
/// </remarks>
internal sealed class XrpcClient
{
    private static readonly MediaTypeHeaderValue JsonMediaType = new("application/json");

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private readonly object _targetLock = new();
    private volatile XrpcTarget _target;
    private volatile string? _dpopNonce;
    private volatile string? _adminCredential;
    private volatile string? _proxyHeader;
    private volatile string? _labelerHeader;
    private volatile string? _latestRepoRev;
    private volatile RateLimitInfo? _latestRateLimitInfo;

    /// <summary>
    /// Creates a transport for <paramref name="serviceUrl"/> over <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">The client to send with. Never mutated, so it may be shared.</param>
    /// <param name="serviceUrl">The service to address. Taken as configured; see <see cref="SetServiceUrl"/>.</param>
    /// <param name="logger">An optional logger.</param>
    internal XrpcClient(HttpClient httpClient, Uri serviceUrl, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serviceUrl);

        _httpClient = httpClient;
        _target = new XrpcTarget(AtProtoHttp.NormalizeBaseUrl(serviceUrl), Credentials: null);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The <c>User-Agent</c> sent with every request; <see langword="null"/> sends none of the SDK's own.</summary>
    internal string? UserAgent { get; init; } = AtProtoHttp.DefaultUserAgent;

    /// <summary>How 429 responses are retried.</summary>
    internal XrpcRateLimitOptions RateLimit { get; init; } = new();

    /// <summary>The serializer options for request and response bodies.</summary>
    internal JsonSerializerOptions JsonOptions { get; init; } = AtProtoJsonDefaults.Options;

    /// <summary>The clock for rate-limit arithmetic and back-off delays.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>The service requests are addressed to.</summary>
    internal Uri ServiceUrl => _target.ServiceUrl;

    /// <summary>
    /// The latest repository revision (TID) received via the <c>Atproto-Repo-Rev</c> response
    /// header, for read-after-write awareness.
    /// </summary>
    internal string? LatestRepoRev => _latestRepoRev;

    /// <summary>The latest <c>RateLimit-*</c> headers received, updated after every response that carries them.</summary>
    internal RateLimitInfo? LatestRateLimitInfo => _latestRateLimitInfo;

    /// <summary>Whether PDS admin credentials are set.</summary>
    internal bool HasAdminCredentials => _adminCredential is not null;

    /// <summary>
    /// Keeps the session's credentials fresh: consulted by every call authenticated with them.
    /// </summary>
    internal IXrpcSessionHandler? SessionHandler { get; set; }

    /// <summary>
    /// Points the transport at another service. Takes effect for the next request, including
    /// on a client that has already sent some.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The URL is not HTTPS and not a loopback address. A service URL set at runtime typically
    /// comes from a DID document or an OAuth session, and the session's tokens go wherever it
    /// points.
    /// </exception>
    internal void SetServiceUrl(Uri url)
    {
        var validated = AtProtoHttp.ValidateServiceUrl(url, nameof(url));
        lock (_targetLock)
            _target = _target with { ServiceUrl = validated };
    }

    /// <summary>Sets Bearer session tokens (app-password sessions).</summary>
    internal void SetTokens(string accessToken, string? refreshToken = null) =>
        SetSession(serviceUrl: null, new XrpcCredentials(accessToken, refreshToken, DPoP: null), keepDPoPNonce: false);

    /// <summary>
    /// Sets DPoP-bound OAuth tokens. Requests then carry <c>Authorization: DPoP &lt;token&gt;</c>
    /// and a proof signed by <paramref name="dpop"/>.
    /// </summary>
    /// <param name="accessToken">The DPoP-bound access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="dpop">The DPoP key for this session.</param>
    /// <param name="dpopNonce">The resource server's current DPoP nonce, if known.</param>
    internal void SetOAuthTokens(string accessToken, string? refreshToken, DPoPProofGenerator dpop, string? dpopNonce = null)
    {
        ArgumentNullException.ThrowIfNull(dpop);
        SetSession(serviceUrl: null, new XrpcCredentials(accessToken, refreshToken, dpop), keepDPoPNonce: false);
        _dpopNonce = dpopNonce;
    }

    /// <summary>
    /// Installs session credentials, or clears them with <see langword="null"/>, together with
    /// the service they belong to, as one change: no call sees one without the other.
    /// </summary>
    /// <param name="serviceUrl">
    /// The service, already validated with <see cref="AtProtoHttp.ValidateServiceUrl"/> (or equal
    /// to the current one); <see langword="null"/> keeps the current service.
    /// </param>
    /// <param name="credentials">The credentials.</param>
    /// <param name="keepDPoPNonce">
    /// Keep the resource server's DPoP nonce: true when the same key goes on talking to the same
    /// service, as after a token refresh.
    /// </param>
    internal void SetSession(Uri? serviceUrl, XrpcCredentials? credentials, bool keepDPoPNonce)
    {
        lock (_targetLock)
        {
            _target = new XrpcTarget(serviceUrl ?? _target.ServiceUrl, credentials);
            if (!keepDPoPNonce)
                _dpopNonce = null;
        }
    }

    /// <summary>Clears the session tokens.</summary>
    internal void ClearTokens() => SetSession(serviceUrl: null, credentials: null, keepDPoPNonce: false);

    /// <summary>
    /// Sets PDS admin credentials, sent as HTTP Basic authentication — the scheme the reference
    /// PDS expects on <c>com.atproto.admin.*</c>, with user <c>admin</c> and the server's
    /// <c>PDS_ADMIN_PASSWORD</c>. Session tokens take priority, so an admin transport should not
    /// carry a user session.
    /// </summary>
    internal void SetAdminCredentials(string password, string user = "admin")
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        _adminCredential = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));
    }

    /// <summary>Clears the PDS admin credentials.</summary>
    internal void ClearAdminCredentials() => _adminCredential = null;

    /// <summary>Sets the client-wide <c>atproto-proxy</c> header.</summary>
    internal void SetProxy(string proxyHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyHeader);
        _proxyHeader = proxyHeader;
    }

    /// <summary>Clears the client-wide <c>atproto-proxy</c> header.</summary>
    internal void ClearProxy() => _proxyHeader = null;

    /// <summary>Sets the client-wide <c>atproto-accept-labelers</c> header.</summary>
    internal void SetLabelers(IEnumerable<string> labelerDids)
    {
        ArgumentNullException.ThrowIfNull(labelerDids);

        // Joined once here rather than per request: the value changes only with the subscription.
        _labelerHeader = JoinLabelers(labelerDids);
    }

    /// <summary>Clears the client-wide <c>atproto-accept-labelers</c> header.</summary>
    internal void ClearLabelers() => _labelerHeader = null;

    // ──────────────────────────────────────────────────────────
    //  Calls
    // ──────────────────────────────────────────────────────────

    /// <summary>Performs an XRPC query (HTTP GET) and deserializes the JSON response.</summary>
    internal async Task<TResponse> QueryAsync<TResponse>(
        string nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Get, nsid, parameters, options);
        using var deadline = Deadline.Start(request, cancellationToken);
        try
        {
            using var response = await SendAsync(request, deadline.Token);
            return await ReadJsonAsync<TResponse>(response, nsid, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            throw deadline.TimeoutException(ex);
        }
    }

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) and deserializes the JSON response.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="body">The request body, serialized as JSON by its runtime type; <see langword="null"/> for none.</param>
    /// <param name="parameters">Query parameters, if the method takes any.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<TResponse> ProcedureAsync<TResponse>(
        string nsid,
        object? body = null,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Post, nsid, parameters, options) { Content = JsonBody(body) };
        using var deadline = Deadline.Start(request, cancellationToken);
        try
        {
            using var response = await SendAsync(request, deadline.Token);
            return await ReadJsonAsync<TResponse>(response, nsid, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            throw deadline.TimeoutException(ex);
        }
    }

    /// <summary>Performs an XRPC procedure (HTTP POST) whose response body, if any, is ignored.</summary>
    /// <inheritdoc cref="ProcedureAsync{TResponse}" path="/param"/>
    internal Task ProcedureAsync(
        string nsid,
        object? body = null,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAndDiscardAsync(
            new XrpcRequest(HttpMethod.Post, nsid, parameters, options) { Content = JsonBody(body) },
            cancellationToken);

    /// <summary>
    /// Uploads binary data (HTTP POST) and deserializes the JSON response.
    /// </summary>
    /// <remarks>
    /// The body is read from the stream's current position. A retry (DPoP nonce, 429) rewinds
    /// to that position, which needs a seekable stream; a non-seekable one that would need a
    /// retry fails with <see cref="InvalidOperationException"/> instead. The stream is not
    /// disposed.
    /// </remarks>
    internal async Task<TResponse> UploadAsync<TResponse>(
        string nsid,
        Stream data,
        string mimeType,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateUpload(nsid, data, mimeType, parameters, options);
        using var deadline = Deadline.Start(request, cancellationToken);
        try
        {
            using var response = await SendAsync(request, deadline.Token);
            return await ReadJsonAsync<TResponse>(response, nsid, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            throw deadline.TimeoutException(ex);
        }
    }

    /// <summary>
    /// Uploads binary data (HTTP POST) to a procedure without output, ignoring any response body.
    /// The stream is read and replayed as <see cref="UploadAsync{TResponse}"/> describes.
    /// </summary>
    internal Task UploadAsync(
        string nsid,
        Stream data,
        string mimeType,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAndDiscardAsync(CreateUpload(nsid, data, mimeType, parameters, options), cancellationToken);

    private static XrpcRequest CreateUpload(
        string nsid, Stream data, string mimeType, XrpcParams? parameters, XrpcCallOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var contentType = MediaTypeHeaderValue.Parse(mimeType);
        long? start = data.CanSeek ? data.Position : null;

        return new XrpcRequest(HttpMethod.Post, nsid, parameters, options)
        {
            Content = () => new UploadContent(data, start, contentType),
            Replayable = data.CanSeek,
        };
    }

    /// <summary>
    /// Performs an XRPC query whose response is binary, returning it as a stream the caller
    /// disposes. The per-call timeout covers receiving the response headers.
    /// </summary>
    internal async Task<XrpcStreamResponse> DownloadAsync(
        string nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Get, nsid, parameters, options);
        using var deadline = Deadline.Start(request, cancellationToken);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendAsync(request, deadline.Token);
            var content = await response.Content.ReadAsStreamAsync(deadline.Token);
            return new XrpcStreamResponse(response, content);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            response?.Dispose();
            throw deadline.TimeoutException(ex);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Performs one of the calls that manage the session itself (sign-in, sign-up, refresh,
    /// sign-out): addressed to the service directly, and authenticated with the token given
    /// rather than the installed session's, so it never triggers a refresh of its own.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="body">The request body, or <see langword="null"/>.</param>
    /// <param name="bearerToken">
    /// The bearer token to send (a refresh JWT, say), or <see langword="null"/> to send none.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<TResponse> ProcedureWithTokenAsync<TResponse>(
        string nsid, object? body, string? bearerToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(TokenRequest(nsid, body, bearerToken), cancellationToken);
        return await ReadJsonAsync<TResponse>(response, nsid, cancellationToken);
    }

    /// <summary>
    /// <see cref="ProcedureWithTokenAsync{TResponse}"/> for a procedure whose response body, if
    /// any, is ignored.
    /// </summary>
    /// <inheritdoc cref="ProcedureWithTokenAsync{TResponse}" path="/param"/>
    internal async Task ProcedureWithTokenAsync(
        string nsid, object? body, string? bearerToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(TokenRequest(nsid, body, bearerToken), cancellationToken);
    }

    private XrpcRequest TokenRequest(string nsid, object? body, string? bearerToken) =>
        new(HttpMethod.Post, nsid, Parameters: null, Direct)
        {
            Content = JsonBody(body),
            Authentication = XrpcAuthentication.Explicit,
            BearerToken = bearerToken,
        };

    /// <summary>
    /// Per-call options for the calls that manage the session itself — sign-in, refresh,
    /// sign-out, sign-up. They address the account's own PDS, so the client-wide proxy and
    /// labeler defaults, which route other calls to an AppView or labeler, do not apply.
    /// </summary>
    internal static XrpcCallOptions Direct { get; } = new() { IsDirect = true };

    // ──────────────────────────────────────────────────────────
    //  Pipeline
    // ──────────────────────────────────────────────────────────

    private async Task SendAndDiscardAsync(XrpcRequest request, CancellationToken cancellationToken)
    {
        using var deadline = Deadline.Start(request, cancellationToken);
        try
        {
            using var response = await SendAsync(request, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            throw deadline.TimeoutException(ex);
        }
    }

    /// <summary>
    /// Sends a request, answering a DPoP nonce challenge, a rejected session token and 429s by
    /// retrying, and returns the successful response with its body unread. Every failure is
    /// thrown as an <see cref="XrpcException"/>.
    /// </summary>
    /// <remarks>
    /// The response is requested with <see cref="HttpCompletionOption.ResponseHeadersRead"/>,
    /// so its body is deserialized straight from the connection instead of being buffered
    /// first — a large page (a timeline is about 185 KB) would otherwise land on the large
    /// object heap on every call.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(XrpcRequest request, CancellationToken cancellationToken)
    {
        ValidateHeaders(request.Options);

        var nonceRetried = false;
        var sessionRetried = false;
        var rateLimitAttempt = 0;
        var sessionHandler = request.Authentication == XrpcAuthentication.Session ? SessionHandler : null;

        if (sessionHandler is not null && _target.Credentials is not null)
            await sessionHandler.BeforeSendAsync(cancellationToken);

        // Read once, after any refresh above, and used for every attempt: a session installed
        // while this call waits (on a 429, on a refresh) must not receive it, and its tokens must
        // not go to this call's service. Only a recovery for the same account replaces them.
        var target = _target;
        var uri = BuildUri(target.ServiceUrl, request.Nsid, request.Parameters);

        _logger.LogDebug("XRPC {Method} {Uri}", request.Method.Method, uri);

        while (true)
        {
            // The message is not disposed: that would only dispose its content, which HTTP/2
            // may still be streaming when an early response arrives, and neither content type
            // used here holds anything to release.
            var message = CreateMessage(request, uri, target, request.Nsid, out var credentials);
            var usedDPoP = credentials?.DPoP is not null;
            var response = await _httpClient.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            try
            {
                ObserveResponseHeaders(response, usedDPoP);

                if (response.IsSuccessStatusCode)
                    return response;

                // A resource server that wants a (fresh) DPoP nonce answers 401 with the nonce
                // in a header. The nonce is captured above; retry once with it. A token rejected
                // as invalid is a session matter even when a new nonce comes with it: the
                // recovery below resends with that nonce too.
                if (usedDPoP && !nonceRetried &&
                    response.StatusCode == HttpStatusCode.Unauthorized &&
                    response.Headers.Contains("DPoP-Nonce") &&
                    !HasInvalidTokenChallenge(response))
                {
                    nonceRetried = true;
                    await EnsureReplayableAsync(request, response, "answer a DPoP nonce challenge", cancellationToken);
                    _logger.LogDebug("DPoP nonce required for {Nsid}; retrying with the server-provided nonce", request.Nsid);
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                    rateLimitAttempt < RateLimit.MaxRetries &&
                    GetRateLimitDelay(response, rateLimitAttempt) is { } delay)
                {
                    await EnsureReplayableAsync(request, response, "retry after a 429", cancellationToken);
                    rateLimitAttempt++;
                    _logger.LogWarning(
                        "Rate limited (429) on {Nsid}. Retry {Attempt}/{Max} after {Delay}ms",
                        request.Nsid, rateLimitAttempt, RateLimit.MaxRetries, (int)delay.TotalMilliseconds);
                    response.Dispose();
                    await Task.Delay(delay, TimeProvider, cancellationToken);
                    continue;
                }

                var exception = await CreateExceptionAsync(response, request.Nsid, cancellationToken);

                // An expired or invalidated access token: have the session refreshed (or pick up
                // the tokens another call has refreshed meanwhile) and resend, once. The refresh
                // happens even for a body that cannot be resent, so the next call succeeds.
                if (sessionHandler is not null && credentials is not null && !sessionRetried &&
                    IsRejectedCredential(response, exception))
                {
                    sessionRetried = true;

                    if (await sessionHandler.TryRecoverAsync(credentials, cancellationToken) is { } recovered &&
                        request.Replayable)
                    {
                        // The same account on the same service, so the same URI.
                        target = target with { Credentials = recovered };
                        nonceRetried = false;
                        _logger.LogDebug("Session token rejected on {Nsid}; retrying with refreshed credentials", request.Nsid);
                        response.Dispose();
                        continue;
                    }
                }

                throw exception;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Whether a failure rejects the access token in a way a refresh cures: a PDS answers an
    /// expired bearer JWT with <c>ExpiredToken</c>, and a resource server an expired or revoked
    /// DPoP-bound token with a 401 whose challenge carries <c>error="invalid_token"</c>
    /// (RFC 6750 section 3.1, RFC 9449 section 7.1). A nonce challenge is not one of these.
    /// </summary>
    private static bool IsRejectedCredential(HttpResponseMessage response, XrpcException exception) =>
        exception.Is(XrpcErrors.ExpiredToken) || HasInvalidTokenChallenge(response);

    private static bool HasInvalidTokenChallenge(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized ||
            !response.Headers.TryGetValues("WWW-Authenticate", out var challenges))
        {
            return false;
        }

        foreach (var challenge in challenges)
        {
            if (challenge.Contains("error=\"invalid_token\"", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// How long to wait before retrying a 429, or <see langword="null"/> when the service asks
    /// for longer than <see cref="XrpcRateLimitOptions.MaxDelay"/> — the call then fails with
    /// <see cref="XrpcRateLimitException"/> instead of holding the caller for the whole window.
    /// </summary>
    private TimeSpan? GetRateLimitDelay(HttpResponseMessage response, int attempt)
    {
        var maxDelay = RateLimit.MaxDelay;
        TimeSpan delay;

        if (XrpcResponseReader.GetRequestedDelay(response, TimeProvider.GetUtcNow()) is { } requested)
        {
            if (requested > maxDelay)
                return null;
            delay = requested;
        }
        else
        {
            // No wait named: exponential back-off, 1 s, 2 s, 4 s, …, within the cap.
            delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), maxDelay.TotalSeconds));
        }

        // Up to 10 % of jitter, so clients released by the same reset do not return in lockstep.
        return delay + delay * (Random.Shared.NextDouble() * 0.1);
    }

    /// <summary>
    /// Fails a retry that would need to resend a body that cannot be replayed — a non-seekable
    /// upload stream — with a message that says so, rather than sending an empty body.
    /// </summary>
    private async Task EnsureReplayableAsync(
        XrpcRequest request, HttpResponseMessage response, string reason, CancellationToken cancellationToken)
    {
        if (request.Replayable)
            return;

        var failure = await CreateExceptionAsync(response, request.Nsid, cancellationToken);
        throw new InvalidOperationException(
            $"{request.Nsid} needs to be sent again to {reason}, but its body stream cannot be " +
            "rewound. Pass a seekable stream (a FileStream or MemoryStream, for example).",
            failure);
    }

    private async Task<XrpcException> CreateExceptionAsync(
        HttpResponseMessage response, string nsid, CancellationToken cancellationToken)
    {
        var body = await XrpcResponseReader.ReadErrorAsync(response, cancellationToken);
        return XrpcResponseReader.CreateException(response, nsid, body, TimeProvider.GetUtcNow());
    }

    private async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response, string nsid, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken);
            return result ?? throw new XrpcResponseFormatException(
                nsid, $"{nsid} returned null where a {typeof(TResponse).Name} was expected.");
        }
        catch (JsonException ex)
        {
            throw new XrpcResponseFormatException(
                nsid, $"{nsid} returned a body that is not a valid {typeof(TResponse).Name}: {ex.Message}", ex);
        }
    }

    private Func<HttpContent>? JsonBody(object? body) =>
        body is null ? null : () => JsonContent.Create(body, body.GetType(), JsonMediaType, JsonOptions);

    private static Uri BuildUri(Uri serviceUrl, string nsid, XrpcParams? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nsid);

        // XRPC paths are always top-level (the spec forbids a prefix), so this is root-relative.
        var builder = new System.Text.StringBuilder("/xrpc/").Append(nsid);

        if (parameters is not null)
        {
            // One key=value pair per entry, so array parameters (uris=a&uris=b) round-trip as
            // repeated keys rather than a single comma-joined value. Input order is preserved.
            var first = true;
            foreach (var (key, value) in parameters)
            {
                builder.Append(first ? '?' : '&')
                    .Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        return new Uri(serviceUrl, builder.ToString());
    }

    private HttpRequestMessage CreateMessage(
        XrpcRequest request, Uri uri, XrpcTarget target, string nsid, out XrpcCredentials? credentials)
    {
        var message = new HttpRequestMessage(request.Method, uri) { Content = request.Content?.Invoke() };
        var headers = message.Headers;

        if (UserAgent is not null)
            headers.TryAddWithoutValidation("User-Agent", UserAgent);

        try
        {
            credentials = ApplyAuthorization(message, request, target.Credentials);
        }
        catch (ObjectDisposedException)
        {
            // The DPoP key was released between this call reading the session and signing its
            // proof: the session was signed out or replaced by another while the call was in
            // flight, which leaves nothing this call may authenticate as.
            throw new XrpcAuthenticationException(
                XrpcErrors.AuthenticationRequired,
                "The session this call was made with was signed out or replaced while it was in flight.",
                HttpStatusCode.Unauthorized,
                nsid);
        }

        // Service proxying and labeler selection are independent of authentication: a public
        // AppView read with labeler subscriptions carries them with no session at all.
        var options = request.Options;
        if (ResolveProxy(options) is { } proxy)
            headers.TryAddWithoutValidation("atproto-proxy", proxy);

        if (ResolveLabelers(options) is { } labelers)
            headers.TryAddWithoutValidation("atproto-accept-labelers", labelers);

        if (options?.Headers is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                headers.Remove(name);
                headers.TryAddWithoutValidation(name, value);
            }
        }

        return message;
    }

    /// <summary>
    /// The <c>atproto-proxy</c> value for a call: the per-call one when set; otherwise none for a
    /// direct (session) call; otherwise the client-wide default, if any.
    /// </summary>
    private string? ResolveProxy(XrpcCallOptions? options) => options switch
    {
        { Proxy: { } proxy } => proxy,
        { IsDirect: true } => null,
        _ => _proxyHeader,
    };

    /// <summary>
    /// The <c>atproto-accept-labelers</c> value for a call, with the same precedence as
    /// <see cref="ResolveProxy"/>. An empty per-call list sends none.
    /// </summary>
    private string? ResolveLabelers(XrpcCallOptions? options) => options switch
    {
        { AcceptLabelers: { } perCall } => JoinLabelers(perCall),
        { IsDirect: true } => null,
        _ => _labelerHeader,
    };

    /// <summary>
    /// Sets the <c>Authorization</c> header (and a DPoP proof) for the request, and returns the
    /// session credentials it carries, if it carries the session's.
    /// </summary>
    private XrpcCredentials? ApplyAuthorization(
        HttpRequestMessage message, XrpcRequest request, XrpcCredentials? credentials)
    {
        if (request.Authentication == XrpcAuthentication.Explicit)
        {
            if (request.BearerToken is { } token)
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return null;
        }

        if (credentials is null)
        {
            // Admin (Basic) auth for com.atproto.admin.* against a self-hosted PDS.
            if (_adminCredential is { } admin)
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", admin);
            return null;
        }

        if (credentials.DPoP is { } dpop)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credentials.AccessToken);
            message.Headers.TryAddWithoutValidation(
                "DPoP",
                dpop.GenerateProofWithAccessToken(
                    message.Method.Method, message.RequestUri!.ToString(), _dpopNonce, credentials.AccessToken));
            return credentials;
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        return credentials;
    }

    private void ObserveResponseHeaders(HttpResponseMessage response, bool usedDPoP)
    {
        if (usedDPoP && response.Headers.TryGetValues("DPoP-Nonce", out var nonces) &&
            nonces.FirstOrDefault() is { Length: > 0 } nonce)
        {
            _dpopNonce = nonce;
        }

        if (response.Headers.TryGetValues("Atproto-Repo-Rev", out var revs) &&
            revs.FirstOrDefault() is { } rev)
        {
            _latestRepoRev = rev;
        }

        // Informative on errors too, especially on a 429.
        if (XrpcResponseReader.ParseRateLimit(response) is { } rateLimit)
            _latestRateLimitInfo = rateLimit;
    }

    private static void ValidateHeaders(XrpcCallOptions? options)
    {
        if (options?.Headers is null)
            return;

        foreach (var name in options.Headers.Keys)
        {
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "DPoP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The '{name}' header belongs to the session and cannot be set per call.",
                    nameof(options));
            }
        }
    }

    private static string? JoinLabelers(IEnumerable<string> labelerDids)
    {
        var joined = string.Join(", ", labelerDids);
        return joined.Length > 0 ? joined : null;
    }

    // ──────────────────────────────────────────────────────────
    //  Types
    // ──────────────────────────────────────────────────────────

    /// <summary>The service calls go to, and the session credentials that belong to it.</summary>
    /// <param name="ServiceUrl">The service.</param>
    /// <param name="Credentials">The session's credentials, if a session is installed.</param>
    private sealed record XrpcTarget(Uri ServiceUrl, XrpcCredentials? Credentials);

    private enum XrpcAuthentication
    {
        /// <summary>The installed session's credentials, or the admin credentials without one.</summary>
        Session,

        /// <summary>The request's own <see cref="XrpcRequest.BearerToken"/>, or none.</summary>
        Explicit,
    }

    /// <summary>One logical XRPC call, which may be sent more than once.</summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Nsid">The method NSID.</param>
    /// <param name="Parameters">Query parameters.</param>
    /// <param name="Options">Per-call options.</param>
    private sealed record XrpcRequest(HttpMethod Method, string Nsid, XrpcParams? Parameters, XrpcCallOptions? Options)
    {
        /// <summary>
        /// Creates the body for one attempt. A factory rather than an instance because a retry
        /// resends the request, and an <see cref="HttpContent"/> cannot be sent twice.
        /// </summary>
        public Func<HttpContent>? Content { get; init; }

        /// <summary>Whether the body can be produced again for a retry.</summary>
        public bool Replayable { get; init; } = true;

        public XrpcAuthentication Authentication { get; init; } = XrpcAuthentication.Session;

        /// <summary>The token an <see cref="XrpcAuthentication.Explicit"/> request carries, if any.</summary>
        public string? BearerToken { get; init; }
    }

    /// <summary>
    /// Links the caller's token with the per-call <see cref="XrpcCallOptions.Timeout"/>, and
    /// tells the two apart when a cancellation surfaces.
    /// </summary>
    private readonly struct Deadline : IDisposable
    {
        private readonly CancellationTokenSource? _source;
        private readonly CancellationToken _caller;
        private readonly TimeSpan _timeout;
        private readonly string _nsid;

        private Deadline(CancellationTokenSource? source, CancellationToken caller, TimeSpan timeout, string nsid)
        {
            _source = source;
            _caller = caller;
            _timeout = timeout;
            _nsid = nsid;
        }

        public CancellationToken Token => _source?.Token ?? _caller;

        public static Deadline Start(XrpcRequest request, CancellationToken cancellationToken)
        {
            if (request.Options?.Timeout is not { } timeout || timeout == System.Threading.Timeout.InfiniteTimeSpan)
                return new Deadline(null, cancellationToken, default, request.Nsid);

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, "options.Timeout");

            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(timeout);
            return new Deadline(source, cancellationToken, timeout, request.Nsid);
        }

        /// <summary>Whether a cancellation came from this deadline rather than the caller.</summary>
        public bool IsExpired =>
            _source is { IsCancellationRequested: true } && !_caller.IsCancellationRequested;

        public TimeoutException TimeoutException(OperationCanceledException inner) =>
            new($"{_nsid} did not complete within {_timeout.TotalSeconds:0.###} s.", inner);

        public void Dispose() => _source?.Dispose();
    }

    /// <summary>
    /// An upload body over a caller's stream. Each send starts from the position the stream had
    /// when the call began, and the stream is left open for its owner.
    /// </summary>
    private sealed class UploadContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly long? _start;

        public UploadContent(Stream stream, long? start, MediaTypeHeaderValue contentType)
        {
            _stream = stream;
            _start = start;
            Headers.ContentType = contentType;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            if (_start is { } start)
                _stream.Position = start;

            await _stream.CopyToAsync(stream, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_start is { } start)
            {
                length = _stream.Length - start;
                return true;
            }

            length = 0;
            return false;
        }

        // Deliberately does not dispose the caller's stream.
        protected override void Dispose(bool disposing)
        {
        }
    }
}

/// <summary>
/// Session credentials as <see cref="XrpcClient"/> sends them. Immutable: a change of tokens is a
/// new instance, so comparing references tells whether the credentials changed.
/// </summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="RefreshToken">The refresh token, if the holder keeps it here.</param>
/// <param name="DPoP">The DPoP key the access token is bound to, for an OAuth session.</param>
internal sealed record XrpcCredentials(string AccessToken, string? RefreshToken, DPoPProofGenerator? DPoP)
{
    /// <summary>The account the credentials authenticate, when a session installed them.</summary>
    public Did? Account { get; init; }

    /// <summary>The service the credentials belong to, when a session installed them.</summary>
    public Uri? Service { get; init; }

    /// <summary>The account and service; never the tokens.</summary>
    public override string ToString() =>
        $"XrpcCredentials {{ Account = {Account}, Service = {Service}, DPoP = {DPoP is not null} }}";
}

/// <summary>
/// Keeps the credentials of an <see cref="XrpcClient"/> fresh. <see cref="AtProtoClient"/>
/// implements it over its session.
/// </summary>
internal interface IXrpcSessionHandler
{
    /// <summary>
    /// Called before a call authenticated with the session is sent: refreshes the session first
    /// if its access token is about to expire. Completes synchronously when it is not.
    /// </summary>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    ValueTask BeforeSendAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called when the service rejected <paramref name="rejected"/> as expired or invalid.
    /// Refreshes the session unless another call already replaced those credentials.
    /// </summary>
    /// <param name="rejected">The credentials the rejected request carried.</param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    /// <returns>
    /// The credentials to resend with: newer ones of the same account on the same service. Never
    /// another account's, which would make the call act as someone else; <see langword="null"/>
    /// when there are none.
    /// </returns>
    Task<XrpcCredentials?> TryRecoverAsync(XrpcCredentials rejected, CancellationToken cancellationToken);
}
