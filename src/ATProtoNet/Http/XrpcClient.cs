using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Http;

/// <summary>
/// Low-level XRPC HTTP client for making AT Protocol API calls.
/// Handles query (GET) and procedure (POST) requests following XRPC conventions.
/// </summary>
public sealed class XrpcClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _accessToken;
    private string? _refreshToken;
    private Auth.OAuth.DPoPProofGenerator? _dpop;
    private string? _dpopNonce;
    private bool _useDPoP;
    private string? _latestRepoRev;
    private RateLimitInfo? _latestRateLimitInfo;
    private string? _proxyHeader;
    private List<string>? _labelerDids;
    private string? _adminCredential;

    /// <summary>
    /// The base URL of the XRPC service (e.g., https://bsky.social).
    /// </summary>
    public Uri BaseUrl => _httpClient.BaseAddress!;

    /// <summary>
    /// The latest repository revision (TID) received from the service via the
    /// <c>Atproto-Repo-Rev</c> response header. This indicates how up-to-date
    /// the service is with the authenticated account's repository.
    /// </summary>
    /// <remarks>
    /// Clients can compare this value against a known revision after a write
    /// to detect whether the service has caught up (read-after-write awareness).
    /// </remarks>
    public string? LatestRepoRev => _latestRepoRev;

    /// <summary>
    /// The latest rate limit information parsed from HTTP response headers.
    /// Updated after every XRPC request.
    /// </summary>
    public RateLimitInfo? LatestRateLimitInfo => _latestRateLimitInfo;

    /// <summary>
    /// Maximum number of automatic retries when receiving HTTP 429 (Too Many Requests).
    /// Default is 3. Set to 0 to disable automatic retry.
    /// </summary>
    public int MaxRateLimitRetries { get; set; } = 3;

    /// <summary>
    /// Whether this client currently has authentication credentials.
    /// </summary>
    public bool IsAuthenticated => _accessToken is not null;

    /// <summary>
    /// Whether this client currently has PDS admin credentials.
    /// </summary>
    public bool HasAdminCredentials => _adminCredential is not null;

    /// <summary>
    /// Creates a new XrpcClient with the specified HttpClient and logger.
    /// </summary>
    public XrpcClient(HttpClient httpClient, ILogger logger, JsonSerializerOptions? jsonOptions = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = jsonOptions ?? AtProtoJsonDefaults.Options;
    }

    /// <summary>
    /// Sets the authentication tokens for subsequent requests.
    /// </summary>
    public void SetTokens(string accessToken, string? refreshToken = null)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    /// <summary>
    /// Sets OAuth DPoP-bound tokens for subsequent requests.
    /// When DPoP is configured, requests use <c>Authorization: DPoP &lt;token&gt;</c>
    /// and include a DPoP proof JWT header.
    /// </summary>
    /// <param name="accessToken">The DPoP-bound access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="dpop">The DPoP proof generator for this session.</param>
    /// <param name="dpopNonce">The current DPoP nonce from the Resource Server.</param>
    public void SetOAuthTokens(string accessToken, string? refreshToken, Auth.OAuth.DPoPProofGenerator dpop, string? dpopNonce = null)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _dpop = dpop;
        _dpopNonce = dpopNonce;
        _useDPoP = true;
    }

    /// <summary>
    /// Updates the DPoP nonce for the Resource Server.
    /// </summary>
    public void UpdateDPoPNonce(string nonce)
    {
        _dpopNonce = nonce;
    }

    /// <summary>
    /// Changes the base URL of this client (for dynamic PDS selection).
    /// Requires HTTPS unless the URL is a localhost/loopback address (for development).
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the URL is not HTTPS and not a loopback address.</exception>
    public void SetBaseUrl(string url)
    {
        var uri = new Uri(url.TrimEnd('/') + "/");

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && !uri.IsLoopback)
        {
            throw new ArgumentException(
                "PDS URL must use HTTPS. HTTP is only allowed for localhost during development.",
                nameof(url));
        }

        _httpClient.BaseAddress = uri;
    }

    /// <summary>
    /// Sets PDS admin credentials for subsequent requests, sent as HTTP Basic
    /// authentication (<c>Authorization: Basic base64(user:password)</c>).
    /// </summary>
    /// <remarks>
    /// This is the scheme the reference Bluesky PDS expects on <c>com.atproto.admin.*</c>
    /// endpoints, where the user is <c>admin</c> and the password is the server's
    /// <c>PDS_ADMIN_PASSWORD</c>. Session tokens take priority: when a session is also
    /// set via <see cref="SetTokens"/> or <see cref="SetOAuthTokens"/>, that token is
    /// sent instead, so an admin client should not carry a user session.
    /// </remarks>
    /// <param name="password">The PDS admin password.</param>
    /// <param name="user">The admin user name. Defaults to <c>"admin"</c>.</param>
    public void SetAdminCredentials(string password, string user = "admin")
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        _adminCredential = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));
    }

    /// <summary>
    /// Clears the PDS admin credentials.
    /// </summary>
    public void ClearAdminCredentials()
    {
        _adminCredential = null;
    }

    /// <summary>
    /// Clears the authentication tokens.
    /// </summary>
    public void ClearTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _dpop = null;
        _dpopNonce = null;
        _useDPoP = false;
    }

    /// <summary>
    /// Sets the default <c>atproto-proxy</c> header value for subsequent requests.
    /// When set, all XRPC requests will include this header, instructing the PDS
    /// to proxy the request to the specified service.
    /// </summary>
    /// <param name="proxyHeader">
    /// The proxy header value: a DID with a service endpoint fragment
    /// (e.g., <c>did:web:api.bsky.app#bsky_appview</c>).
    /// Use <see cref="ServiceProxy.Build"/> to construct this value.
    /// </param>
    public void SetProxy(string proxyHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyHeader);
        _proxyHeader = proxyHeader;
    }

    /// <summary>
    /// Clears the default <c>atproto-proxy</c> header.
    /// </summary>
    public void ClearProxy()
    {
        _proxyHeader = null;
    }

    /// <summary>
    /// Sets the subscribed labeler DIDs for the <c>atproto-accept-labelers</c> header.
    /// When set, all XRPC requests will include this header so the server knows which
    /// labelers' labels to include in responses.
    /// </summary>
    /// <param name="labelerDids">
    /// The DIDs of labeler services to subscribe to (e.g., <c>did:plc:ar7c4by46qjdydhdevvrndac</c>).
    /// </param>
    public void SetLabelers(IEnumerable<string> labelerDids)
    {
        _labelerDids = labelerDids.ToList();
    }

    /// <summary>
    /// Clears the subscribed labeler DIDs, removing the <c>atproto-accept-labelers</c> header.
    /// </summary>
    public void ClearLabelers()
    {
        _labelerDids = null;
    }

    /// <summary>
    /// Gets the current refresh token, if available.
    /// </summary>
    internal string? RefreshToken => _refreshToken;

    /// <summary>
    /// Performs an XRPC query (HTTP GET) and deserializes the response.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="nsid">The NSID of the endpoint (e.g., "com.atproto.repo.getRecord").</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public Task<TResponse> QueryAsync<TResponse>(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, nsid, parameters, content: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Performs an XRPC query (HTTP GET) with no response body.
    /// </summary>
    public Task QueryAsync(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, nsid, parameters, content: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with a JSON body and deserializes the response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request body.</typeparam>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="body">The request body.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public Task<TResponse> ProcedureAsync<TRequest, TResponse>(
        string nsid,
        TRequest body,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, nsid, parameters, JsonBody(body), cancellationToken: cancellationToken);

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with a JSON body and no response.
    /// </summary>
    public Task ProcedureAsync<TRequest>(
        string nsid,
        TRequest body,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, nsid, parameters, JsonBody(body), cancellationToken: cancellationToken);

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with no body and no response.
    /// </summary>
    public Task ProcedureAsync(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, nsid, parameters, content: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with no body but with a response.
    /// </summary>
    public Task<TResponse> ProcedureAsync<TResponse>(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default)
        where TResponse : class =>
        SendAsync<TResponse>(HttpMethod.Post, nsid, parameters, content: null, cancellationToken: cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Internal proxy-aware overloads (for chat sub-clients)
    // ──────────────────────────────────────────────────────────

    internal Task<TResponse> QueryAsync<TResponse>(
        string nsid,
        string proxyHeader,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, nsid, parameters, content: null, proxyHeader, cancellationToken);

    internal Task<TResponse> ProcedureAsync<TRequest, TResponse>(
        string nsid,
        TRequest body,
        string proxyHeader,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, nsid, parameters, JsonBody(body), proxyHeader, cancellationToken);

    internal Task ProcedureAsync(
        string nsid,
        string proxyHeader,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, nsid, parameters, content: null, proxyHeader, cancellationToken);

    /// <summary>
    /// Issues a request and discards the response body.
    /// </summary>
    /// <remarks>
    /// <paramref name="content"/> is a factory rather than a single instance because
    /// <see cref="SendWithDPoPRetryAsync"/> may replay the request, and an
    /// <see cref="HttpContent"/> cannot be sent twice. Null means a bodyless request.
    /// </remarks>
    private async Task SendAsync(
        HttpMethod method,
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters,
        Func<HttpContent>? content,
        string? proxyHeader = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(
            method, nsid, parameters, content, proxyHeader, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Issues a request and deserializes the response body.
    /// </summary>
    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters,
        Func<HttpContent>? content,
        string? proxyHeader = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(
            method, nsid, parameters, content, proxyHeader, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

    private Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters,
        Func<HttpContent>? content,
        string? proxyHeader,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC {Method}{Proxied}: {Url}",
            method.Method, proxyHeader is null ? "" : " (proxied)", url);

        return SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(method, url) { Content = content?.Invoke() },
            cancellationToken,
            proxyOverride: proxyHeader);
    }

    private Func<HttpContent> JsonBody<TRequest>(TRequest body) =>
        () => JsonContent.Create(body, options: _jsonOptions);

    /// <summary>
    /// Uploads a blob (binary data) to the server.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response into.</typeparam>
    /// <param name="nsid">The endpoint NSID (typically "com.atproto.repo.uploadBlob").</param>
    /// <param name="data">The blob data stream.</param>
    /// <param name="mimeType">The MIME type of the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response containing the blob reference.</returns>
    public async Task<TResponse> UploadBlobAsync<TResponse>(
        string nsid,
        Stream data,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid);

        _logger.LogDebug("XRPC Upload: POST {Url} ({MimeType})", url, mimeType);

        using var response = await SendWithDPoPRetryAsync(
            () =>
            {
                if (data.CanSeek) data.Position = 0;
                return new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StreamContent(data)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue(mimeType) }
                    },
                };
            },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

    /// <summary>
    /// Downloads a blob from the server.
    /// </summary>
    /// <param name="nsid">The endpoint NSID.</param>
    /// <param name="parameters">Query parameters (e.g., did, cid).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response stream and content type.</returns>
    public async Task<(Stream Stream, string? ContentType)> DownloadBlobAsync(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Download: GET {Url}", url);

        var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;

        return (stream, contentType);
    }

    /// <summary>
    /// Makes a raw request with the specified content, using the refresh token for authentication.
    /// Used for token refresh operations.
    /// </summary>
    internal async Task<TResponse> ProcedureWithRefreshTokenAsync<TResponse>(
        string nsid,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var url = BuildUrl(nsid);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (_refreshToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _refreshToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

    private static string BuildUrl(string nsid, IEnumerable<KeyValuePair<string, string?>>? parameters = null)
    {
        var url = $"/xrpc/{nsid}";

        if (parameters is null)
            return url;

        // Emit each key=value pair separately so XRPC array parameters
        // (e.g., uris=a&uris=b) round-trip as repeated keys rather than
        // a single comma-joined value. Order is preserved from the input.
        var first = true;
        var sb = new System.Text.StringBuilder();
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;
            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.Length == 0 ? url : url + sb;
    }

    private void ApplyAuthHeader(HttpRequestMessage request, string? proxyOverride = null)
    {
        if (_accessToken is null)
        {
            // Admin (Basic) auth for com.atproto.admin.* against a self-hosted PDS.
            if (_adminCredential is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _adminCredential);
            }

            return;
        }

        if (_useDPoP && _dpop is not null)
        {
            // OAuth DPoP: Authorization: DPoP <token> + DPoP proof header
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", _accessToken);

            var url = new Uri(_httpClient.BaseAddress!, request.RequestUri!).ToString();
            var method = request.Method.Method;
            var proof = _dpop.GenerateProofWithAccessToken(method, url, _dpopNonce, _accessToken);
            request.Headers.TryAddWithoutValidation("DPoP", proof);
        }
        else
        {
            // Legacy Bearer token auth
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        // Apply atproto-proxy header for service proxying (per-request override takes priority)
        var effectiveProxy = proxyOverride ?? _proxyHeader;
        if (effectiveProxy is not null)
        {
            request.Headers.TryAddWithoutValidation("atproto-proxy", effectiveProxy);
        }

        // Apply atproto-accept-labelers header for labeler subscriptions
        if (_labelerDids is { Count: > 0 })
        {
            request.Headers.TryAddWithoutValidation("atproto-accept-labelers",
                string.Join(", ", _labelerDids));
        }
    }

    /// <summary>
    /// Sends an HTTP request with automatic DPoP nonce retry and rate limit handling.
    /// When a server requires a DPoP nonce (responds with 401 + DPoP-Nonce header),
    /// the nonce is captured and the request is retried once with the new nonce.
    /// When a server responds with 429 Too Many Requests, the request is retried
    /// after the delay indicated by Retry-After or RateLimit-Reset headers.
    /// </summary>
    /// <param name="createRequest">Factory that creates a new HttpRequestMessage for each attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="completionOption">HTTP completion option.</param>
    /// <param name="proxyOverride">Value for the <c>atproto-proxy</c> header, if the request targets a specific service.</param>
    /// <returns>The HTTP response (from retry if nonce was required, otherwise from first attempt).</returns>
    private async Task<HttpResponseMessage> SendWithDPoPRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        string? proxyOverride = null)
    {
        var request = createRequest();
        ApplyAuthHeader(request, proxyOverride);

        var response = await _httpClient.SendAsync(request, completionOption, cancellationToken);

        // DPoP nonce retry: if the server requires a nonce we don't have (or ours is stale),
        // it responds with 401 + DPoP-Nonce header. Capture the nonce and retry once.
        if (_useDPoP &&
            response.StatusCode == HttpStatusCode.Unauthorized &&
            response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            var newNonce = nonceValues.FirstOrDefault();
            if (newNonce is not null)
            {
                _logger.LogDebug("DPoP nonce required, retrying request with server-provided nonce");
                _dpopNonce = newNonce;

                response.Dispose();

                var retryRequest = createRequest();
                ApplyAuthHeader(retryRequest, proxyOverride);
                response = await _httpClient.SendAsync(retryRequest, completionOption, cancellationToken);
            }
        }

        // Rate limit retry: if the server responds with 429, retry with backoff.
        for (int attempt = 0;
             attempt < MaxRateLimitRetries &&
             response.StatusCode == HttpStatusCode.TooManyRequests;
             attempt++)
        {
            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning(
                "Rate limited (429). Retry {Attempt}/{Max} after {Delay}ms",
                attempt + 1, MaxRateLimitRetries, (int)delay.TotalMilliseconds);

            response.Dispose();

            await Task.Delay(delay, cancellationToken);

            var retryRequest = createRequest();
            ApplyAuthHeader(retryRequest, proxyOverride);
            response = await _httpClient.SendAsync(retryRequest, completionOption, cancellationToken);
        }

        return response;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Always update DPoP nonce from response headers
        if (_useDPoP && response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            _dpopNonce = nonceValues.First();
        }

        // Track latest repo revision for read-after-write awareness
        if (response.Headers.TryGetValues("Atproto-Repo-Rev", out var revValues))
        {
            var rev = revValues.FirstOrDefault();
            if (rev is not null)
            {
                _latestRepoRev = rev;
            }
        }

        // Rate limit headers are informative on errors too, especially on 429.
        ParseRateLimitHeaders(response);

        if (response.IsSuccessStatusCode)
            return;

        string? responseBody = null;
        XrpcErrorResponse? error = null;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            error = JsonSerializer.Deserialize<XrpcErrorResponse>(responseBody, _jsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not an XRPC error envelope; fall through to the generic message below,
            // which still carries whatever body we managed to read.
        }

        if (error is not null)
        {
            throw new AtProtoHttpException(
                error.Error, error.Message, response.StatusCode, responseBody);
        }

        throw new AtProtoHttpException(
            $"XRPC request failed with status {response.StatusCode}",
            response.StatusCode)
        {
            ResponseBody = responseBody,
        };
    }

    private void ParseRateLimitHeaders(HttpResponseMessage response)
    {
        int? limit = null;
        int? remaining = null;
        DateTimeOffset? reset = null;

        if (response.Headers.TryGetValues("RateLimit-Limit", out var limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out var parsedLimit))
        {
            limit = parsedLimit;
        }

        if (response.Headers.TryGetValues("RateLimit-Remaining", out var remainingValues) &&
            int.TryParse(remainingValues.FirstOrDefault(), out var parsedRemaining))
        {
            remaining = parsedRemaining;
        }

        if (response.Headers.TryGetValues("RateLimit-Reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out var resetUnix))
        {
            reset = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
        }

        if (limit is not null || remaining is not null || reset is not null)
        {
            _latestRateLimitInfo = new RateLimitInfo
            {
                Limit = limit,
                Remaining = remaining,
                Reset = reset,
            };
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        // Prefer Retry-After header (seconds)
        if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            var retryAfter = retryAfterValues.FirstOrDefault();
            if (retryAfter is not null && int.TryParse(retryAfter, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        // Fall back to RateLimit-Reset header (Unix timestamp)
        if (response.Headers.TryGetValues("RateLimit-Reset", out var resetValues))
        {
            var resetStr = resetValues.FirstOrDefault();
            if (resetStr is not null && long.TryParse(resetStr, out var resetUnix))
            {
                var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
                var delay = resetTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    return delay;
            }
        }

        // Exponential backoff fallback: 1s, 2s, 4s, ...
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    /// <summary>
    /// No-op. The <see cref="HttpClient"/> is owned by whoever constructed it —
    /// typically <see cref="AtProtoClient"/> or an <c>IHttpClientFactory</c> — and this
    /// client holds no other unmanaged state. Implemented so callers can safely wrap a
    /// <see cref="XrpcClient"/> in <c>using</c>.
    /// </summary>
    public void Dispose()
    {
    }
}
