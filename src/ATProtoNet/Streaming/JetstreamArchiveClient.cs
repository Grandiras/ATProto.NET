using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// An open <c>getSegment</c> response: the segment bytes plus the metadata a mirror needs.
/// Dispose it to release the underlying HTTP response.
/// </summary>
public sealed class JetstreamSegmentDownload : IDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    internal JetstreamSegmentDownload(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
        ETag = response.Headers.ETag?.Tag.Trim('"');
        ContentLength = response.Content.Headers.ContentLength;
        IsPartial = response.StatusCode == HttpStatusCode.PartialContent;
    }

    /// <summary>The segment bytes, from the requested offset when a range was asked for.</summary>
    public Stream Content { get; }

    /// <summary>The response ETag with its quotes stripped — the segment's xxh3 metadata checksum.</summary>
    public string? ETag { get; }

    /// <summary>The length of this response body, if the server reported one.</summary>
    public long? ContentLength { get; }

    /// <summary>Whether the server honoured the requested byte range (HTTP 206).</summary>
    public bool IsPartial { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Content.Dispose();
        _response.Dispose();
    }
}

/// <summary>
/// Typed client for the Jetstream v2 archive HTTP endpoints: <c>planSnapshot</c>,
/// <c>listSegments</c>, <c>getSegment</c>, and <c>getBlock</c>.
/// </summary>
/// <remarks>
/// <para>These are the endpoints behind Jetstream's <b>replay</b> and <b>snapshot</b> modes.
/// Unlike the live WebSocket tail they are authenticated with an API key
/// (<c>Authorization: Bearer</c>) and, on the Bluesky-hosted instances, metered in
/// <i>response bytes on the wire</i> rather than in requests.</para>
/// <para>Metering shapes the retry behaviour: an exhausted quota is answered with <c>429</c> and a
/// <c>Retry-After</c>, and a download cut off mid-body leaves the bytes already received intact
/// and un-recharged. <see cref="DownloadSegmentAsync"/> therefore resumes with an HTTP
/// <c>Range</c> request from the exact byte offset it stopped at instead of starting over.</para>
/// <para>Segment responses are immutable, ETag'd, and CDN-cacheable — until a compaction rewrites
/// the file to physically drop deleted records, which changes its checksum. A mirror re-lists and
/// compares <see cref="JetstreamSegmentInfo.Checksum"/> rather than assuming a name never changes
/// content.</para>
/// </remarks>
/// <example>
/// <code>
/// using var archive = new JetstreamArchiveClient(JetstreamEndpoints.UsEast, apiKey);
///
/// var plan = await archive.PlanSnapshotAsync(new JetstreamSnapshotRequest
/// {
///     Collections = ["app.bsky.feed.post"],
///     Kinds = ["commit"],
/// });
///
/// foreach (var segment in plan.Segments)
///     Console.WriteLine($"{segment.Name} [{segment.MinSeq}..{segment.MaxSeq}] {segment.Mode}");
/// </code>
/// </example>
public sealed class JetstreamArchiveClient : IDisposable
{
    private const string PlanSnapshotPath = "xrpc/network.bsky.jetstream.planSnapshot";
    private const string ListSegmentsPath = "xrpc/network.bsky.jetstream.listSegments";
    private const string GetSegmentPath = "xrpc/network.bsky.jetstream.getSegment";
    private const string GetBlockPath = "xrpc/network.bsky.jetstream.getBlock";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;
    private readonly string? _apiKey;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>How many times a transient or metered failure is retried. Default: 5.</summary>
    public int MaxRetryAttempts { get; init; } = 5;

    /// <summary>Ceiling on a single retry delay, including one the server asked for. Default: 5 minutes.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Create an archive client.
    /// </summary>
    /// <param name="serviceUrl">The Jetstream host URL. <c>ws(s)</c> schemes are converted to
    /// <c>http(s)</c>, so the same value can configure this and
    /// <see cref="JetstreamConsumerOptions.ServiceUrl"/>.</param>
    /// <param name="apiKey">The API key for the metered HTTP endpoints. Null omits the
    /// <c>Authorization</c> header, which only an unmetered self-hosted instance accepts.</param>
    /// <param name="httpClient">An <see cref="HttpClient"/> to send with. When null, one is
    /// created and disposed with this instance.</param>
    /// <param name="logger">Optional logger.</param>
    public JetstreamArchiveClient(
        string serviceUrl,
        string? apiKey = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _baseUri = new Uri(ToHttpUrl(serviceUrl), UriKind.Absolute);
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _apiKey = apiKey;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Build one page of a download plan for the requested DIDs, collections, and kinds.
    /// </summary>
    /// <remarks>
    /// The planner works from bloom filters and per-block summaries, so it has <b>no false
    /// negatives but may return blocks with no matching rows</b> — apply the exact filter to what
    /// you decode. Large plans truncate at a whole segment or block-range boundary and always
    /// admit at least one work unit, so paging with
    /// <c>afterSeq = <see cref="JetstreamSnapshotPlan.PlannedThroughSeq"/></c> always progresses.
    /// </remarks>
    /// <param name="request">The filter and sequence window to plan over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="JetstreamArchiveException">The server refused the request.</exception>
    public async Task<JetstreamSnapshotPlan> PlanSnapshotAsync(
        JetstreamSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await SendWithRetryAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, PlanSnapshotPath))
                {
                    Content = JsonContent.Create(request, options: JsonOptions),
                };
                return message;
            },
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        return await ReadJsonAsync<JetstreamSnapshotPlan>(response, cancellationToken);
    }

    /// <summary>
    /// Enumerate sealed segment files, in ascending index order.
    /// </summary>
    /// <param name="limit">Maximum number of segments to return (1–1000). Null uses the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JetstreamSegmentPage> ListSegmentsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryBuilder();
        if (limit.HasValue)
            query.Add("limit", limit.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(cursor))
            query.Add("cursor", cursor);

        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, ListSegmentsPath + query)),
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        return await ReadJsonAsync<JetstreamSegmentPage>(response, cancellationToken);
    }

    /// <summary>
    /// Enumerate every sealed segment, following the pagination cursor to the end of the archive.
    /// </summary>
    /// <param name="pageSize">Page size to request (1–1000). Null uses the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<JetstreamSegmentInfo> ListAllSegmentsAsync(
        int? pageSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListSegmentsAsync(pageSize, cursor, cancellationToken);
            foreach (var segment in page.Segments)
                yield return segment;

            // A page that returns nothing but still hands back a cursor would loop forever.
            cursor = page.Segments.Count > 0 ? page.Cursor : null;
        } while (!string.IsNullOrEmpty(cursor) && !cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Open a sealed segment file for reading, optionally from a byte offset.
    /// </summary>
    /// <param name="name">The segment filename (e.g. <c>seg_000000002a.jss</c>).</param>
    /// <param name="rangeStart">Byte offset to resume from, sent as an HTTP <c>Range</c> header.
    /// Null downloads the whole file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open response; dispose it when done.</returns>
    /// <exception cref="JetstreamArchiveException">The server refused the request (e.g.
    /// <c>SegmentNotFound</c>).</exception>
    public async Task<JetstreamSegmentDownload> GetSegmentAsync(
        string name,
        long? rangeStart = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var query = new QueryBuilder();
        query.Add("name", name);

        var response = await SendWithRetryAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, GetSegmentPath + query));
                if (rangeStart is > 0)
                    message.Headers.Range = new RangeHeaderValue(rangeStart, null);
                return message;
            },
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        try
        {
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new JetstreamSegmentDownload(response, content);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Download a whole segment into <paramref name="destination"/>, resuming from the exact byte
    /// offset already written whenever the transfer is interrupted.
    /// </summary>
    /// <remarks>
    /// Running out of metered quota mid-download closes the stream cleanly, so the bytes already
    /// received are intact and are not re-charged when the rest is fetched with a <c>Range</c>
    /// request. If the server answers a ranged request with a full <c>200</c> body — a proxy that
    /// dropped the <c>Range</c> header — the destination is truncated and the download restarts,
    /// rather than concatenating two overlapping copies.
    /// </remarks>
    /// <param name="name">The segment filename.</param>
    /// <param name="destination">A writable, seekable stream. Bytes already in it are treated as
    /// a partial download and resumed after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of bytes written by this call.</returns>
    public async Task<long> DownloadSegmentAsync(
        string name,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var written = destination.CanSeek ? destination.Position : 0;
        var buffer = new byte[81920];

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var download = await GetSegmentAsync(name, written > 0 ? written : null, cancellationToken);

                if (written > 0 && !download.IsPartial)
                {
                    // The Range header was ignored; this body starts at zero.
                    destination.Seek(0, SeekOrigin.Begin);
                    destination.SetLength(0);
                    written = 0;
                }

                int read;
                while ((read = await download.Content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                }

                return written;
            }
            catch (Exception ex) when (IsResumable(ex) && attempt < MaxRetryAttempts)
            {
                var delay = RetryDelay(ex, attempt);
                _logger.LogWarning(ex,
                    "Segment {Segment} download interrupted at byte {Offset}; resuming in {Delay}s",
                    name, written, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Download one block of a sealed segment: the raw zstd frame exactly as stored on disk,
    /// without the segment's 8-byte length prefix. Decode it with
    /// <see cref="JetstreamSegmentReader.DecodeBlockFrame"/>.
    /// </summary>
    /// <param name="segment">The segment filename.</param>
    /// <param name="blockIndex">Zero-based block index within the segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="JetstreamArchiveException">The server refused the request (e.g.
    /// <c>SegmentNotFound</c>, <c>BlockNotFound</c>).</exception>
    public async Task<byte[]> GetBlockAsync(
        string segment,
        int blockIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);

        var query = new QueryBuilder();
        query.Add("segment", segment);
        query.Add("blockIndex", blockIndex.ToString(CultureInfo.InvariantCulture));

        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, GetBlockPath + query)),
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Send a request, retrying a metered <c>429</c> for as long as its <c>Retry-After</c> asks and
    /// backing off exponentially on transient transport and 5xx failures.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            JetstreamArchiveException failure;

            try
            {
                using var request = requestFactory();
                if (_apiKey is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                response = await _http.SendAsync(request, completionOption, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return response;

                failure = await BuildFailureAsync(response, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                failure = new JetstreamArchiveException(
                    $"Jetstream archive request failed: {ex.Message}", innerException: ex);
            }
            catch (IOException ex)
            {
                failure = new JetstreamArchiveException(
                    $"Jetstream archive request failed: {ex.Message}", innerException: ex);
            }

            response?.Dispose();

            if (!failure.IsRetryable || attempt >= MaxRetryAttempts)
                throw failure;

            var delay = RetryDelay(failure, attempt);
            _logger.LogWarning("Jetstream archive request failed ({Error}); retrying in {Delay}s",
                failure.Message, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static async Task<JetstreamArchiveException> BuildFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var (error, message) = await ReadErrorAsync(response, cancellationToken);

        TimeSpan? retryAfter = null;
        if (response.Headers.RetryAfter is { } header)
        {
            retryAfter = header.Delta
                ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (retryAfter is { Ticks: < 0 })
                retryAfter = TimeSpan.Zero;
        }

        var description = status switch
        {
            401 => "Jetstream refused the archive request: the API key is missing, malformed, or revoked",
            429 => "Jetstream archive byte quota exhausted",
            _ => "Jetstream refused the archive request",
        };

        return new JetstreamArchiveException(
            $"{description} (HTTP {status}{(error is null ? string.Empty : $", {error}")})" +
            (message is null ? "." : $": {message}"),
            status,
            error,
            retryAfter);
    }

    /// <summary>Whether a mid-download failure can be resumed with a <c>Range</c> request.</summary>
    private static bool IsResumable(Exception ex) => ex switch
    {
        JetstreamArchiveException archive => archive.IsRetryable,
        IOException or HttpRequestException => true,
        _ => false,
    };

    /// <summary>
    /// How long to wait before the next attempt: what the server asked for when it sent
    /// <c>Retry-After</c>, otherwise an exponential backoff, both capped by
    /// <see cref="MaxRetryDelay"/>. The metered quota refills continuously rather than at a
    /// boundary, so a shorter wait than requested would just burn another 429.
    /// </summary>
    private TimeSpan RetryDelay(Exception ex, int attempt)
    {
        var requested = (ex as JetstreamArchiveException)?.RetryAfter;
        var delay = requested ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new JetstreamArchiveException(
                    $"Jetstream returned an empty {typeof(T).Name} body.",
                    (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new JetstreamArchiveException(
                $"Jetstream returned a body that is not a {typeof(T).Name}: {ex.Message}",
                (int)response.StatusCode,
                innerException: ex);
        }
    }

    /// <summary>Read the XRPC error name and message out of a failed response, if it carried them.</summary>
    private static async Task<(string? Error, string? Message)> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (body.ValueKind != JsonValueKind.Object)
                return (null, null);

            return (
                body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null,
                body.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                    ? message.GetString()
                    : null);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            // A proxy error page rather than an XRPC envelope; the status code is the whole story.
            return (null, null);
        }
    }

    internal static string ToHttpUrl(string serviceUrl)
    {
        var url = serviceUrl.TrimEnd('/');
        if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["wss://".Length..];
        else if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url["ws://".Length..];
        return url + "/";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient)
            _http.Dispose();
    }

    /// <summary>Builds a percent-encoded query string, prefixed with <c>?</c> when non-empty.</summary>
    private struct QueryBuilder
    {
        private System.Text.StringBuilder? _query;

        public void Add(string name, string value)
        {
            _query ??= new System.Text.StringBuilder();
            _query.Append(_query.Length == 0 ? '?' : '&');
            _query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        public override readonly string ToString() => _query?.ToString() ?? string.Empty;
    }
}
