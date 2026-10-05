using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Http;

// The XRPC transport behind the Lexicon sub-clients: builds requests, applies the session's credentials,
// retries DPoP nonce challenges and rate limits, and maps failures to XrpcException.
//
// It addresses one service, ServiceUrl, with absolute request URIs, and puts every header on the request
// itself. The HttpClient is never mutated — not its HttpClient.BaseAddress, not its
// HttpClient.DefaultRequestHeaders — so any number of transports, hosts and sessions can share one
// client, and the service can change after the client has sent requests.
//
// The session state (tokens, DPoP key, client-wide proxy and labeler headers) is held in fields replaced
// as a whole, so a request in flight on another thread sees either the old or the new value, never a
// mix. The service URL and the session credentials are one such value: a call reads them once, and every
// attempt of it goes to that service with those credentials, so a session installed meanwhile is never
// sent to the previous one's service. DPoP nonces are the server's, not the session's, and live in
// NonceCache by origin.
//
// When a SessionHandler is attached, session-authenticated calls consult it: before sending, so it can
// refresh a token about to expire, and once after the service rejects the token, so it can refresh and
// have the call resent with the same account's new tokens.
internal sealed class XrpcClient : IXrpcTransport
{
    private static readonly MediaTypeHeaderValue JsonMediaType = new("application/json");

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private readonly object _targetLock = new();
    private volatile XrpcTarget _target;
    private volatile string? _adminCredential;
    private volatile string? _proxyHeader;
    private volatile string? _labelerHeader;
    private volatile string? _latestRepoRev;
    private volatile RateLimitInfo? _latestRateLimitInfo;

    // Creates a transport for serviceUrl over httpClient.
    //
    // httpClient: The client to send with. Never mutated, so it may be shared.
    //
    // serviceUrl: The service to address. Taken as configured; see SetServiceUrl.
    //
    // logger: An optional logger.
    internal XrpcClient(HttpClient httpClient, Uri serviceUrl, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serviceUrl);

        _httpClient = httpClient;
        _target = new XrpcTarget(AtProtoHttp.NormalizeBaseUrl(serviceUrl), Credentials: null);
        _logger = logger ?? NullLogger.Instance;
    }

    // The User-Agent sent with every request; null sends none of the SDK's own.
    internal string? UserAgent { get; init; } = AtProtoHttp.DefaultUserAgent;

    // How 429 responses are retried.
    internal XrpcRateLimitOptions RateLimit { get; init; } = new();

    // The serializer options for request and response bodies.
    internal JsonSerializerOptions JsonOptions { get; init; } = AtProtoJsonDefaults.Options;

    // The clock for rate-limit arithmetic and back-off delays.
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    // Where the DPoP nonces of the services this transport talks to are kept: by default the
    // process-wide cache, so a new transport's first request carries a nonce another one already
    // received.
    internal DPoPNonceCache NonceCache { get; init; } = DPoPNonceCache.Shared;

    // The service requests are addressed to.
    internal Uri ServiceUrl => _target.ServiceUrl;

    // The latest repository revision (TID) received via the Atproto-Repo-Rev response header, for
    // read-after-write awareness.
    internal string? LatestRepoRev => _latestRepoRev;

    // The latest RateLimit-* headers received, updated after every response that carries them.
    internal RateLimitInfo? LatestRateLimitInfo => _latestRateLimitInfo;

    // Keeps the session's credentials fresh: consulted by every call authenticated with them.
    internal IXrpcSessionHandler? SessionHandler { get; set; }

    // Points the transport at another service. Takes effect for the next request, including on a client
    // that has already sent some.
    //
    // Throws ArgumentException: The URL is not HTTPS and not a loopback address. A service URL set at
    // runtime typically comes from a DID document or an OAuth session, and the session's tokens go
    // wherever it points.
    internal void SetServiceUrl(Uri url)
    {
        var validated = AtProtoHttp.ValidateServiceUrl(url, nameof(url));
        lock (_targetLock)
            _target = _target with { ServiceUrl = validated };
    }

    // Sets Bearer session tokens (app-password sessions).
    internal void SetTokens(string accessToken, string? refreshToken = null) =>
        SetSession(serviceUrl: null, new XrpcCredentials(accessToken, refreshToken, DPoP: null));

    // Sets DPoP-bound OAuth tokens. Requests then carry Authorization: DPoP <token> and a proof signed
    // by dpop.
    //
    // accessToken: The DPoP-bound access token.
    //
    // dpop: The DPoP key for this session.
    internal void SetOAuthTokens(string accessToken, string? refreshToken, DPoPProofGenerator dpop)
    {
        ArgumentNullException.ThrowIfNull(dpop);
        SetSession(serviceUrl: null, new XrpcCredentials(accessToken, refreshToken, dpop));
    }

    // Sets a space credential. Requests then carry Authorization: Atproto-Space <credential>, the DID they
    // are addressed to, and a signature by key over both.
    //
    // credential: The space credential JWT.
    //
    // key: The key the credential is bound to.
    //
    // spaceAuthority: The space's authority, the audience of every call that names no repo.
    internal void SetSpaceCredential(string credential, AtProtoKey key, Did spaceAuthority)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(spaceAuthority);
        SetSession(
            serviceUrl: null,
            new XrpcCredentials(credential, RefreshToken: null, DPoP: null) { Space = new SpaceRequestSigner(key, spaceAuthority) });
    }

    // Installs session credentials, or clears them with null, together with the service they belong to,
    // as one change: no call sees one without the other.
    //
    // serviceUrl: The service, already validated with AtProtoHttp.ValidateServiceUrl (or equal to the
    // current one); null keeps the current service.
    internal void SetSession(Uri? serviceUrl, XrpcCredentials? credentials)
    {
        lock (_targetLock)
            _target = new XrpcTarget(serviceUrl ?? _target.ServiceUrl, credentials);
    }

    // Clears the session tokens.
    internal void ClearTokens() => SetSession(serviceUrl: null, credentials: null);

    // Sets PDS admin credentials, sent as HTTP Basic authentication — the scheme the reference PDS
    // expects on com.atproto.admin.*, with user admin and the server's PDS_ADMIN_PASSWORD. Session
    // tokens take priority, so an admin transport should not carry a user session.
    internal void SetAdminCredentials(string password, string user = "admin")
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        _adminCredential = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));
    }

    // Sets the client-wide atproto-proxy header.
    internal void SetProxy(string proxyHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyHeader);
        _proxyHeader = proxyHeader;
    }

    // Clears the client-wide atproto-proxy header.
    internal void ClearProxy() => _proxyHeader = null;

    // Sets the client-wide atproto-accept-labelers header.
    internal void SetLabelers(IEnumerable<string> labelerDids)
    {
        ArgumentNullException.ThrowIfNull(labelerDids);

        // Joined once here rather than per request: the value changes only with the subscription.
        _labelerHeader = JoinLabelers(labelerDids);
    }

    // Clears the client-wide atproto-accept-labelers header.
    internal void ClearLabelers() => _labelerHeader = null;

    // ── Calls ────────────────────────────────────────────────

    // Performs an XRPC query (HTTP GET) and deserializes the JSON response.
    internal Task<TResponse> QueryAsync<TResponse>(
        string nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(new XrpcRequest(HttpMethod.Get, nsid, parameters, options), ReadJsonAsync<TResponse>, cancellationToken);

    // Performs an XRPC procedure (HTTP POST) and deserializes the JSON response.
    //
    // nsid: The method NSID.
    //
    // body: The request body, serialized as JSON by its runtime type; null for none.
    //
    // parameters: Query parameters, if the method takes any.
    //
    // options: Per-call options.
    internal Task<TResponse> ProcedureAsync<TResponse>(
        string nsid,
        object? body = null,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new XrpcRequest(HttpMethod.Post, nsid, parameters, options) { Content = JsonBody(body) },
            ReadJsonAsync<TResponse>,
            cancellationToken);

    // Performs an XRPC procedure (HTTP POST) whose response body, if any, is ignored.
    internal Task ProcedureAsync(
        string nsid,
        object? body = null,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new XrpcRequest(HttpMethod.Post, nsid, parameters, options) { Content = JsonBody(body) },
            Discard,
            cancellationToken);

    // Uploads binary data (HTTP POST) and deserializes the JSON response.
    //
    // The body is read from the stream's current position. A retry (DPoP nonce, 429) rewinds to that
    // position, which needs a seekable stream; a non-seekable one that would need a retry fails with
    // InvalidOperationException instead. The stream is not disposed.
    internal async Task<TResponse> UploadAsync<TResponse>(
        string nsid,
        Stream data,
        string mimeType,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        // Awaited, so an invalid argument faults the task, as every other transport call does.
        await SendAsync(CreateUpload(nsid, data, mimeType, parameters, options), ReadJsonAsync<TResponse>, cancellationToken)
            .ConfigureAwait(false);

    // Uploads binary data (HTTP POST) to a procedure without output, ignoring any response body. The
    // stream is read and replayed as UploadAsync describes.
    internal Task UploadAsync(
        string nsid,
        Stream data,
        string mimeType,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(CreateUpload(nsid, data, mimeType, parameters, options), Discard, cancellationToken);

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

    // Performs an XRPC query whose response is binary, returning it as a stream the caller disposes. The
    // per-call timeout covers receiving the response headers.
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
            response = await SendAsync(request, deadline.Token).ConfigureAwait(false);
            var content = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
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

    // Performs one of the calls that manage the session itself (sign-in, sign-up, refresh, sign-out):
    // addressed to the service directly, and authenticated with the token given rather than the
    // installed session's, so it never triggers a refresh of its own.
    //
    // nsid: The method NSID.
    //
    // body: The request body, or null.
    //
    // bearerToken: The bearer token to send (a refresh JWT, say), or null to send none.
    internal async Task<TResponse> ProcedureWithTokenAsync<TResponse>(
        string nsid, object? body, string? bearerToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(TokenRequest(nsid, body, bearerToken), cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, nsid, cancellationToken).ConfigureAwait(false);
    }

    // ProcedureWithTokenAsync for a procedure whose response body, if any, is ignored.
    internal async Task ProcedureWithTokenAsync(
        string nsid, object? body, string? bearerToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(TokenRequest(nsid, body, bearerToken), cancellationToken).ConfigureAwait(false);
    }

    private XrpcRequest TokenRequest(string nsid, object? body, string? bearerToken) =>
        new(HttpMethod.Post, nsid, Parameters: null, Direct)
        {
            Content = JsonBody(body),
            Authentication = XrpcAuthentication.Explicit,
            BearerToken = bearerToken,
        };

    // Per-call options for the calls that manage the session itself — sign-in, refresh, sign-out,
    // sign-up. They address the account's own PDS, so the client-wide proxy and labeler defaults, which
    // route other calls to an AppView or labeler, do not apply.
    internal static XrpcCallOptions Direct { get; } = new() { IsDirect = true };

    // ── IXrpcTransport: the same calls, for sub-clients outside the SDK ──

    Task<TOut> IXrpcTransport.QueryAsync<TOut>(
        Nsid nsid, XrpcParams? parameters, XrpcCallOptions? options, CancellationToken cancellationToken) =>
        QueryAsync<TOut>(NsidOf(nsid), parameters, options, cancellationToken);

    Task<XrpcStreamResponse> IXrpcTransport.DownloadAsync(
        Nsid nsid, XrpcParams? parameters, XrpcCallOptions? options, CancellationToken cancellationToken) =>
        DownloadAsync(NsidOf(nsid), parameters, options, cancellationToken);

    Task<TOut> IXrpcTransport.ProcedureAsync<TIn, TOut>(
        Nsid nsid, TIn input, XrpcParams? parameters, XrpcCallOptions? options, CancellationToken cancellationToken) =>
        ProcedureAsync<TOut>(NsidOf(nsid), input, parameters, options, cancellationToken);

    Task IXrpcTransport.ProcedureAsync<TIn>(
        Nsid nsid, TIn input, XrpcParams? parameters, XrpcCallOptions? options, CancellationToken cancellationToken) =>
        ProcedureAsync(NsidOf(nsid), input, parameters, options, cancellationToken);

    Task IXrpcTransport.ProcedureAsync(
        Nsid nsid, XrpcParams? parameters, XrpcCallOptions? options, CancellationToken cancellationToken) =>
        ProcedureAsync(NsidOf(nsid), body: null, parameters, options, cancellationToken);

    Task<TOut> IXrpcTransport.UploadAsync<TOut>(
        Nsid nsid, Stream data, string mimeType, XrpcParams? parameters, XrpcCallOptions? options,
        CancellationToken cancellationToken) =>
        UploadAsync<TOut>(NsidOf(nsid), data, mimeType, parameters, options, cancellationToken);

    private static string NsidOf(Nsid nsid, [CallerArgumentExpression(nameof(nsid))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(nsid, paramName);
        return nsid.Value;
    }

    // ── Pipeline ─────────────────────────────────────────────

    // Sends a request within its XrpcCallOptions.Timeout and reads the successful response with read,
    // reporting the timeout as a TimeoutException.
    private async Task<T> SendAsync<T>(
        XrpcRequest request,
        Func<HttpResponseMessage, string, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        using var deadline = Deadline.Start(request, cancellationToken);
        try
        {
            using var response = await SendAsync(request, deadline.Token).ConfigureAwait(false);
            return await read(response, request.Nsid, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (deadline.IsExpired)
        {
            throw deadline.TimeoutException(ex);
        }
    }

    // The response of a call without output, whose body is ignored.
    private static readonly Func<HttpResponseMessage, string, CancellationToken, Task<bool>> Discard =
        static (_, _, _) => Task.FromResult(true);

    // Sends a request, answering a DPoP nonce challenge, a rejected session token and 429s by retrying,
    // and returns the successful response with its body unread. Every failure is thrown as an
    // XrpcException.
    //
    // The response is requested with HttpCompletionOption.ResponseHeadersRead, so its body is
    // deserialized straight from the connection instead of being buffered first — a large page (a
    // timeline is about 185 KB) would otherwise land on the large object heap on every call.
    private async Task<HttpResponseMessage> SendAsync(XrpcRequest request, CancellationToken cancellationToken)
    {
        ValidateHeaders(request.Options);

        var nonceRetried = false;
        var sessionRetried = false;
        var rateLimitAttempt = 0;
        var sessionHandler = request.Authentication == XrpcAuthentication.Session ? SessionHandler : null;

        if (sessionHandler is not null && _target.Credentials is not null)
            await sessionHandler.BeforeSendAsync(cancellationToken).ConfigureAwait(false);

        // Read once, after any refresh above, and used for every attempt: a session installed
        // while this call waits (on a 429, on a refresh) must not receive it, and its tokens must
        // not go to this call's service. Only a recovery for the same account replaces them.
        var target = _target;
        var uri = BuildUri(target.ServiceUrl, request.Nsid, request.Parameters);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("XRPC {Method} {Uri}", request.Method.Method, uri);

        while (true)
        {
            // The message is not disposed: that would only dispose its content, which HTTP/2
            // may still be streaming when an early response arrives, and neither content type
            // used here holds anything to release.
            var message = CreateMessage(request, uri, target, request.Nsid, out var credentials);
            var usedDPoP = credentials?.DPoP is not null;
            var response = await _httpClient.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            try
            {
                ObserveResponseHeaders(response, uri, usedDPoP);

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
                    await EnsureReplayableAsync(request, response, "answer a DPoP nonce challenge", cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("DPoP nonce required for {Nsid}; retrying with the server-provided nonce", request.Nsid);
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                    rateLimitAttempt < RateLimit.MaxRetries &&
                    GetRateLimitDelay(response, rateLimitAttempt) is { } delay)
                {
                    await EnsureReplayableAsync(request, response, "retry after a 429", cancellationToken).ConfigureAwait(false);
                    rateLimitAttempt++;
                    _logger.LogWarning(
                        "Rate limited (429) on {Nsid}. Retry {Attempt}/{Max} after {Delay}ms",
                        request.Nsid, rateLimitAttempt, RateLimit.MaxRetries, (int)delay.TotalMilliseconds);
                    response.Dispose();
                    await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var exception = await CreateExceptionAsync(response, request.Nsid, cancellationToken).ConfigureAwait(false);

                // An expired or invalidated access token: have the session refreshed (or pick up
                // the tokens another call has refreshed meanwhile) and resend, once. The refresh
                // happens even for a body that cannot be resent, so the next call succeeds.
                if (sessionHandler is not null && credentials is not null && !sessionRetried &&
                    IsRejectedCredential(response, exception))
                {
                    sessionRetried = true;

                    if (await sessionHandler.TryRecoverAsync(credentials, cancellationToken).ConfigureAwait(false) is { } recovered &&
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

    // Whether a failure rejects the access token in a way a refresh cures: a PDS answers an expired
    // bearer JWT with ExpiredToken, and a resource server an expired or revoked DPoP-bound token with a
    // 401 whose challenge carries error="invalid_token" (RFC 6750 section 3.1, RFC 9449 section 7.1). A
    // nonce challenge is not one of these.
    private static bool IsRejectedCredential(HttpResponseMessage response, XrpcException exception) =>
        exception.Is(XrpcErrors.ExpiredToken) || HasInvalidTokenChallenge(response);

    private static bool HasInvalidTokenChallenge(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Unauthorized &&
        response.Headers.TryGetValues("WWW-Authenticate", out var challenges) &&
        challenges.Any(static challenge => challenge.Contains("error=\"invalid_token\"", StringComparison.OrdinalIgnoreCase));

    // How long to wait before retrying a 429, or null when the service asks for longer than
    // XrpcRateLimitOptions.MaxDelay — the call then fails with XrpcRateLimitException instead of holding
    // the caller for the whole window.
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

    // Fails a retry that would need to resend a body that cannot be replayed — a non-seekable upload
    // stream — with a message that says so, rather than sending an empty body.
    private async Task EnsureReplayableAsync(
        XrpcRequest request, HttpResponseMessage response, string reason, CancellationToken cancellationToken)
    {
        if (request.Replayable)
            return;

        var failure = await CreateExceptionAsync(response, request.Nsid, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"{request.Nsid} needs to be sent again to {reason}, but its body stream cannot be " +
            "rewound. Pass a seekable stream (a FileStream or MemoryStream, for example).",
            failure);
    }

    private async Task<XrpcException> CreateExceptionAsync(
        HttpResponseMessage response, string nsid, CancellationToken cancellationToken)
    {
        var body = await XrpcResponseReader.ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        return XrpcResponseReader.CreateException(response, nsid, body, TimeProvider.GetUtcNow());
    }

    private async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response, string nsid, CancellationToken cancellationToken)
    {
        try
        {
            #pragma warning disable CA2007 // The resource keeps the default context for disposal: ConfigureAwait on it would change its declared type.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            #pragma warning restore CA2007
            var result = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? throw new XrpcResponseFormatException(
                nsid, $"{nsid} returned null where a {typeof(TResponse).Name} was expected.");
        }
        catch (JsonException ex)
        {
            throw new XrpcResponseFormatException(
                nsid, $"{nsid} returned a body that is not a valid {typeof(TResponse).Name}: {ex.Message}", ex);
        }
    }

    // Serialized on first use and resent as is, so the request has a Content-Length rather than going out chunked.
    private Func<HttpContent>? JsonBody(object? body)
    {
        if (body is null)
            return null;

        byte[]? json = null;
        return () => new ByteArrayContent(json ??= JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), JsonOptions))
        {
            Headers = { ContentType = JsonMediaType },
        };
    }

    private static Uri BuildUri(Uri serviceUrl, string nsid, XrpcParams? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nsid);

        // XRPC paths are always top-level (the spec forbids a prefix), so this is root-relative.
        return new Uri(serviceUrl, "/xrpc/" + nsid + parameters?.ToQueryString());
    }

    private HttpRequestMessage CreateMessage(
        XrpcRequest request, Uri uri, XrpcTarget target, string nsid, out XrpcCredentials? credentials)
    {
        // HTTP/2 multiplexes concurrent calls over one connection; a service or handler without it gets 1.1.
        var message = new HttpRequestMessage(request.Method, uri)
        {
            Content = request.Content?.Invoke(),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
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

    // The atproto-proxy value for a call: the per-call one when set; otherwise none for a direct
    // (session) call; otherwise the client-wide default, if any.
    private string? ResolveProxy(XrpcCallOptions? options) => options switch
    {
        { Proxy: { } proxy } => proxy,
        { IsDirect: true } => null,
        _ => _proxyHeader,
    };

    // The atproto-accept-labelers value for a call, with the same precedence as ResolveProxy. An empty
    // per-call list sends none.
    private string? ResolveLabelers(XrpcCallOptions? options) => options switch
    {
        { AcceptLabelers: { } perCall } => JoinLabelers(perCall),
        { IsDirect: true } => null,
        _ => _labelerHeader,
    };

    // Sets the Authorization header (and a DPoP proof) for the request, and returns the session
    // credentials it carries, if it carries the session's.
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

        if (credentials.Space is { } space)
        {
            // A repo operation is addressed to the repo's owner, a space-host operation to the authority.
            var authorization = $"{SpaceHttpSignature.CredentialScheme} {credentials.AccessToken}";
            var audience = request.Parameters?.Get("repo") is { } repo ? Did.Parse(repo) : space.SpaceAuthority;
            var signature = SpaceHttpSignature.SignRequest(space.Key, authorization, audience);
            message.Headers.TryAddWithoutValidation("Authorization", authorization);
            message.Headers.TryAddWithoutValidation(SpaceHttpSignature.AudienceHeader, audience.Value);
            message.Headers.TryAddWithoutValidation("Signature-Input", signature.SignatureInput);
            message.Headers.TryAddWithoutValidation("Signature", signature.Signature);
            return credentials;
        }

        if (credentials.DPoP is { } dpop)
        {
            var uri = message.RequestUri!;
            message.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credentials.AccessToken);
            message.Headers.TryAddWithoutValidation(
                "DPoP", dpop.GenerateProofWithAccessToken(message.Method.Method, uri, NonceCache.Get(uri), credentials.AccessToken));
            return credentials;
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        return credentials;
    }

    private void ObserveResponseHeaders(HttpResponseMessage response, Uri uri, bool usedDPoP)
    {
        if (usedDPoP)
            NonceCache.Observe(uri, response);

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

    // ── Types ────────────────────────────────────────────────

    // The service calls go to, and the session credentials that belong to it.
    //
    // ServiceUrl: The service.
    //
    // Credentials: The session's credentials, if a session is installed.
    private sealed record XrpcTarget(Uri ServiceUrl, XrpcCredentials? Credentials);

    private enum XrpcAuthentication
    {
        // The installed session's credentials, or the admin credentials without one.
        Session,

        // The request's own XrpcRequest.BearerToken, or none.
        Explicit,
    }

    // One logical XRPC call, which may be sent more than once.
    //
    // Method: The HTTP method.
    //
    // Nsid: The method NSID.
    //
    // Parameters: Query parameters.
    //
    // Options: Per-call options.
    private sealed record XrpcRequest(HttpMethod Method, string Nsid, XrpcParams? Parameters, XrpcCallOptions? Options)
    {
        // Creates the body for one attempt. A factory rather than an instance because a retry resends
        // the request, and an HttpContent cannot be sent twice.
        public Func<HttpContent>? Content { get; init; }

        // Whether the body can be produced again for a retry.
        public bool Replayable { get; init; } = true;

        public XrpcAuthentication Authentication { get; init; } = XrpcAuthentication.Session;

        // The token an XrpcAuthentication.Explicit request carries, if any.
        public string? BearerToken { get; init; }
    }

    // Links the caller's token with the per-call XrpcCallOptions.Timeout, and tells the two apart when a
    // cancellation surfaces.
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

        // Whether a cancellation came from this deadline rather than the caller.
        public bool IsExpired =>
            _source is { IsCancellationRequested: true } && !_caller.IsCancellationRequested;

        public TimeoutException TimeoutException(OperationCanceledException inner) =>
            new($"{_nsid} did not complete within {_timeout.TotalSeconds:0.###} s.", inner);

        public void Dispose() => _source?.Dispose();
    }

    // An upload body over a caller's stream. Each send starts from the position the stream had when the
    // call began, and the stream is left open for its owner.
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

            await _stream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
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

// Session credentials as XrpcClient sends them. Immutable: a change of tokens is a new instance, so
// comparing references tells whether the credentials changed.
//
// RefreshToken: The refresh token, if the holder keeps it here.
//
// DPoP: The DPoP key the access token is bound to, for an OAuth session.
internal sealed record XrpcCredentials(string AccessToken, string? RefreshToken, DPoPProofGenerator? DPoP)
{
    // The account the credentials authenticate, when a session installed them.
    public Did? Account { get; init; }

    // The key and authority of a space credential, when these are one.
    public SpaceRequestSigner? Space { get; init; }

    // The service the credentials belong to, when a session installed them.
    public Uri? Service { get; init; }

    // The account and service; never the tokens.
    public override string ToString() =>
        $"XrpcCredentials {{ Account = {Account}, Service = {Service}, DPoP = {DPoP is not null} }}";
}

// What signs a space credential's requests: the key it is bound to, and the space authority that is the
// audience of a call naming no repo.
internal sealed record SpaceRequestSigner(AtProtoKey Key, Did SpaceAuthority);

// Keeps the credentials of an XrpcClient fresh. AtProtoClient implements it over its session.
internal interface IXrpcSessionHandler
{
    // Called before a call authenticated with the session is sent: refreshes the session first if its
    // access token is about to expire. Completes synchronously when it is not.
    //
    // cancellationToken: The call's cancellation token.
    ValueTask BeforeSendAsync(CancellationToken cancellationToken);

    // Called when the service rejected rejected as expired or invalid. Refreshes the session unless
    // another call already replaced those credentials.
    //
    // rejected: The credentials the rejected request carried.
    //
    // cancellationToken: The call's cancellation token.
    //
    // Returns: The credentials to resend with: newer ones of the same account on the same service. Never
    // another account's, which would make the call act as someone else; null when there are none.
    Task<XrpcCredentials?> TryRecoverAsync(XrpcCredentials rejected, CancellationToken cancellationToken);
}
