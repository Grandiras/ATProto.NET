using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
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

    /// <summary>
    /// The base URL of the XRPC service (e.g., https://bsky.social).
    /// </summary>
    public Uri BaseUrl => _httpClient.BaseAddress!;

    /// <summary>
    /// Whether this client currently has authentication credentials.
    /// </summary>
    public bool IsAuthenticated => _accessToken is not null;

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
    public async Task<TResponse> QueryAsync<TResponse>(
        string nsid,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Query: GET {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

    /// <summary>
    /// Performs an XRPC query (HTTP GET) with no response body.
    /// </summary>
    public async Task QueryAsync(
        string nsid,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Query: GET {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

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
    public async Task<TResponse> ProcedureAsync<TRequest, TResponse>(
        string nsid,
        TRequest body,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Procedure: POST {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: _jsonOptions),
            },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with a JSON body and no response.
    /// </summary>
    public async Task ProcedureAsync<TRequest>(
        string nsid,
        TRequest body,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Procedure: POST {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: _jsonOptions),
            },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with no body and no response.
    /// </summary>
    public async Task ProcedureAsync(
        string nsid,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Procedure: POST {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Performs an XRPC procedure (HTTP POST) with no body but with a response.
    /// </summary>
    public async Task<TResponse> ProcedureAsync<TResponse>(
        string nsid,
        IDictionary<string, string?>? parameters = null,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var url = BuildUrl(nsid, parameters);

        _logger.LogDebug("XRPC Procedure: POST {Url}", url);

        using var response = await SendWithDPoPRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Failed to deserialize response from {nsid}");
    }

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
        IDictionary<string, string?>? parameters = null,
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

    private string BuildUrl(string nsid, IDictionary<string, string?>? parameters = null)
    {
        var url = $"/xrpc/{nsid}";

        if (parameters is { Count: > 0 })
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            foreach (var (key, value) in parameters)
            {
                if (value is not null)
                    query[key] = value;
            }
            url = $"{url}?{query}";
        }

        return url;
    }

    private void ApplyAuthHeader(HttpRequestMessage request)
    {
        if (_accessToken is null) return;

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
    }

    /// <summary>
    /// Sends an HTTP request with automatic DPoP nonce retry.
    /// When a server requires a DPoP nonce (responds with 401 + DPoP-Nonce header),
    /// the nonce is captured and the request is retried once with the new nonce.
    /// </summary>
    /// <param name="createRequest">Factory that creates a new HttpRequestMessage for each attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="completionOption">HTTP completion option.</param>
    /// <returns>The HTTP response (from retry if nonce was required, otherwise from first attempt).</returns>
    private async Task<HttpResponseMessage> SendWithDPoPRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var request = createRequest();
        ApplyAuthHeader(request);

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
                ApplyAuthHeader(retryRequest);
                response = await _httpClient.SendAsync(retryRequest, completionOption, cancellationToken);
            }
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

        if (response.IsSuccessStatusCode)
            return;

        string? responseBody = null;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorResponse = JsonSerializer.Deserialize<XrpcErrorResponse>(responseBody, _jsonOptions);

            if (errorResponse is not null)
            {
                throw new AtProtoHttpException(
                    errorResponse.Error,
                    errorResponse.Message,
                    response.StatusCode,
                    responseBody);
            }
        }
        catch (AtProtoHttpException)
        {
            throw;
        }
        catch (Exception)
        {
            // Could not parse error response as JSON
        }

        throw new AtProtoHttpException(
            $"XRPC request failed with status {response.StatusCode}",
            response.StatusCode)
        {
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // HttpClient is managed externally (via HttpClientFactory)
    }
}
