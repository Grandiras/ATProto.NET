using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tap;

/// <summary>Configures a <see cref="TapClient"/>.</summary>
public sealed class TapClientOptions
{
    /// <summary>The Tap instance's base URL, e.g. <c>http://localhost:2480</c>.</summary>
    public required Uri ServiceUrl { get; init; }

    /// <summary>
    /// The admin password the instance was started with (<c>TAP_ADMIN_PASSWORD</c>), sent as HTTP
    /// Basic auth (<c>admin:password</c>) on every request and on the channel. Null when the
    /// instance has none.
    /// </summary>
    public string? AdminPassword { get; init; }

    /// <summary>
    /// The client for the admin endpoints. Default: a client the <see cref="TapClient"/> owns.
    /// Tap usually runs next to your service, so no address policy is applied.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>How the channel reconnects after its connection drops, and when to give up.</summary>
    public StreamReconnectPolicy Reconnect { get; init; } = new();

    /// <summary>Invoked for every channel message that is not a valid Tap event. It is skipped, and not acknowledged.</summary>
    public Action<Exception>? OnError { get; init; }

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// A client for Tap, Bluesky's Sync 1.1 consumer and backfill service (indigo <c>cmd/tap</c>):
/// its admin endpoints, and its <c>/channel</c> WebSocket of verified record and identity events.
/// </summary>
/// <remarks>
/// <para>Tap connects to the firehose, verifies every commit, backfills the repositories you add
/// and filters collections, and hands you plain JSON events. Add repositories with
/// <see cref="AddReposAsync"/>, then read events from <see cref="OpenChannel"/> and acknowledge
/// each once it is processed. For webhook delivery, see <c>MapTapWebhook</c> in
/// <c>ATProtoNet.Server</c>.</para>
/// <para>The wire protocol and the admin auth are those of the reference client,
/// <c>@atproto/tap</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// using var tap = new TapClient(new Uri("http://localhost:2480"), adminPassword: "secret");
/// await tap.AddReposAsync([Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz")]);
///
/// var channel = tap.OpenChannel();
/// await foreach (var evt in channel.ReadAllAsync(stoppingToken))
/// {
///     if (evt is TapRecordEvent record)
///         Console.WriteLine($"{record.Operation} {record.Uri}");
///     await channel.AckAsync(evt);
/// }
/// </code>
/// </example>
public sealed class TapClient : IDisposable
{
    private readonly TapClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string? _authorization;
    private readonly DuplexStreamConnector _connector;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="serviceUrl">The Tap instance's base URL, e.g. <c>http://localhost:2480</c>.</param>
    /// <param name="adminPassword">The instance's admin password, if it has one.</param>
    public TapClient(Uri serviceUrl, string? adminPassword = null)
        : this(new TapClientOptions { ServiceUrl = serviceUrl, AdminPassword = adminPassword })
    {
    }

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentException">The URL is not an absolute http(s) URL.</exception>
    public TapClient(TapClientOptions options)
        : this(options, StreamSocket.DuplexConnector)
    {
    }

    internal TapClient(TapClientOptions options, DuplexStreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ServiceUrl);
        if (!options.ServiceUrl.IsAbsoluteUri || options.ServiceUrl.Scheme is not ("http" or "https"))
            throw new ArgumentException("Invalid URL, expected http:// or https://", nameof(options));
        ArgumentNullException.ThrowIfNull(options.Reconnect);
        options.Reconnect.Validate();

        _options = options;
        _connector = connector;
        _ownsHttpClient = options.HttpClient is null;
        _httpClient = options.HttpClient ?? new HttpClient();
        _authorization = options.AdminPassword is { Length: > 0 } password ? FormatAdminAuthHeader(password) : null;
    }

    /// <summary>The Tap instance's base URL.</summary>
    public Uri ServiceUrl => _options.ServiceUrl;

    /// <summary>
    /// The <c>Authorization</c> header value for Tap's admin auth: HTTP Basic with the user
    /// <c>admin</c>, as <c>formatAdminAuthHeader</c> in <c>@atproto/tap</c> builds it.
    /// </summary>
    /// <param name="password">The admin password.</param>
    public static string FormatAdminAuthHeader(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + password));
    }

    /// <summary>
    /// Starts tracking repositories (<c>POST /repos/add</c>): Tap backfills each from its PDS, then
    /// streams its live events. Adding one already tracked changes nothing.
    /// </summary>
    /// <param name="dids">The repositories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TapException">Tap refused the request.</exception>
    public Task AddReposAsync(IEnumerable<Did> dids, CancellationToken cancellationToken = default) =>
        PostDidsAsync("/repos/add", dids, "add repos", cancellationToken);

    /// <summary>
    /// Stops tracking repositories (<c>POST /repos/remove</c>) and deletes Tap's metadata for them.
    /// Events already queued for delivery are still delivered.
    /// </summary>
    /// <param name="dids">The repositories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TapException">Tap refused the request.</exception>
    public Task RemoveReposAsync(IEnumerable<Did> dids, CancellationToken cancellationToken = default) =>
        PostDidsAsync("/repos/remove", dids, "remove repos", cancellationToken);

    /// <summary>
    /// Resolves a DID to its document through Tap's identity cache (<c>GET /resolve/:did</c>).
    /// </summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or null when the DID does not resolve.</returns>
    /// <exception cref="TapException">Tap failed the request.</exception>
    public async Task<DidDocument?> ResolveDidAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        using var response = await SendAsync(HttpMethod.Get, $"/resolve/{did}", content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, "resolve DID", cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, "resolve DID",
            json => json.Deserialize<DidDocument>(AtProtoJsonDefaults.Options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads what Tap knows about a repository it tracks (<c>GET /info/:did</c>).
    /// </summary>
    /// <param name="did">The repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The repository's state, revision and record count.</returns>
    /// <exception cref="TapException">
    /// Tap failed the request, or does not track the repository (<see cref="TapException.StatusCode"/> 404).
    /// </exception>
    public async Task<TapRepoInfo> GetRepoInfoAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        using var response = await SendAsync(HttpMethod.Get, $"/info/{did}", content: null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "get repo info", cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, "get repo info", TapRepoInfo.Read, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a channel: Tap's <c>/channel</c> WebSocket, which delivers events to acknowledge.
    /// </summary>
    /// <remarks>
    /// Several channels may be open at once; Tap shares events out among them, keeping each
    /// repository's events in order.
    /// </remarks>
    public TapChannel OpenChannel()
    {
        var builder = new UriBuilder(_options.ServiceUrl)
        {
            Scheme = _options.ServiceUrl.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/channel",
            Query = string.Empty,
        };

        return new TapChannel(
            builder.Uri,
            new StreamSocketOptions(Authorization: _authorization),
            _connector,
            _options.Reconnect,
            _options.OnError,
            _options.Logger ?? NullLogger.Instance);
    }

    private async Task PostDidsAsync(string path, IEnumerable<Did> dids, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dids);

        // {"dids": [...]}, as @atproto/tap sends it.
        var body = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("dids");
            foreach (var did in dids)
                writer.WriteStringValue(did.Value);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var content = new ReadOnlyMemoryContent(body.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, what, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.ServiceUrl, path)) { Content = content };
        if (_authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", _authorization);

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TapException($"Could not reach Tap at {_options.ServiceUrl.GetLeftPart(UriPartial.Authority)}: {ex.Message}", null, ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new TapException(
            $"Failed to {what}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}" +
            (string.IsNullOrWhiteSpace(body) ? "." : $": {Truncate(body)}"),
            response.StatusCode);
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response, string what, Func<JsonElement, T?> read, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return read(document.RootElement.Clone()) ?? throw new TapException($"Failed to {what}: Tap answered null.", response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new TapException($"Failed to {what}: Tap's answer is malformed: {ex.Message}", response.StatusCode, ex);
        }
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "…";

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

/// <summary>
/// One connection to a Tap instance's <c>/channel</c> WebSocket: the events Tap delivers, and
/// their acknowledgements.
/// </summary>
/// <remarks>
/// <para>Tap delivers each event at least once, and resends it after a timeout (60 seconds by
/// default) until it is acknowledged. Acknowledge an event once it is processed; one that fails
/// can be left unacknowledged to have Tap retry it. Tap holds a repository's next event back until
/// the one in flight is acknowledged, so every event must be acknowledged sooner or later.</para>
/// <para>An acknowledgement made while the connection is down is sent once it reconnects;
/// acknowledging twice is harmless. With Tap's fire-and-forget mode (<c>TAP_DISABLE_ACKS</c>)
/// acknowledgements are ignored.</para>
/// <para>The channel reconnects after its connection drops, per
/// <see cref="TapClientOptions.Reconnect"/>, until the token is cancelled. A Tap instance that
/// refuses the connection, such as one in webhook mode, ends the enumeration with an
/// <see cref="EventStreamException"/>.</para>
/// </remarks>
public sealed class TapChannel
{
    private readonly Uri _endpoint;
    private readonly StreamSocketOptions _socketOptions;
    private readonly DuplexStreamConnector _connector;
    private readonly StreamReconnectPolicy _reconnect;
    private readonly Action<Exception>? _onError;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<long> _pendingAcks = [];
    private IDuplexStreamSocket? _socket;
    private int _reading;

    internal TapChannel(
        Uri endpoint,
        StreamSocketOptions socketOptions,
        DuplexStreamConnector connector,
        StreamReconnectPolicy reconnect,
        Action<Exception>? onError,
        ILogger logger)
    {
        _endpoint = endpoint;
        _socketOptions = socketOptions;
        _connector = connector;
        _reconnect = reconnect;
        _onError = onError;
        _logger = logger;
    }

    /// <summary>The channel's WebSocket URL.</summary>
    public Uri Endpoint => _endpoint;

    /// <summary>
    /// Reads events until the token is cancelled, reconnecting as the connection drops.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token; cancelling ends the enumeration normally.</param>
    /// <exception cref="EventStreamException">
    /// Tap refused the connection, or every reconnect attempt the policy allows failed.
    /// </exception>
    /// <exception cref="InvalidOperationException">The channel is already being read.</exception>
    public async IAsyncEnumerable<TapEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
            throw new InvalidOperationException("A Tap channel is read by one enumeration at a time; open another for more.");

        try
        {
            var backoff = new ReconnectBackoff(_reconnect, _logger, "Tap channel");
            while (!cancellationToken.IsCancellationRequested)
            {
                Exception? failure = null;
                IDuplexStreamSocket? socket = null;
                try
                {
                    socket = await _connector(_endpoint, _socketOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (socket is not null)
                {
                    await using (socket.ConfigureAwait(false))
                    {
                        await AttachAsync(socket).ConfigureAwait(false);
                        try
                        {
                            while (true)
                            {
                                StreamSocketMessage message;
                                try
                                {
                                    if (await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false) is not { } received)
                                        break;
                                    message = received;
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    failure = ex;
                                    break;
                                }

                                backoff.Reset();

                                TapEvent evt;
                                try
                                {
                                    evt = TapEvent.Parse(message.Data.Span);
                                }
                                catch (FormatException ex)
                                {
                                    // Not acknowledged: Tap resends it, which a newer SDK version may understand.
                                    _logger.LogWarning(ex, "Skipping a Tap channel message that is not a valid event");
                                    _onError?.Invoke(ex);
                                    continue;
                                }

                                yield return evt;
                            }
                        }
                        finally
                        {
                            Interlocked.CompareExchange(ref _socket, null, socket);
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (failure is EventStreamException { IsRetryable: false })
                    ExceptionDispatchInfo.Throw(failure);

                if (!await backoff.WaitAsync(failure, cancellationToken).ConfigureAwait(false))
                    yield break;
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>
    /// Acknowledges an event, so Tap does not send it again: <c>{"type":"ack","id":…}</c>.
    /// </summary>
    /// <param name="evt">The event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the acknowledgement is sent, or queued for the next connection
    /// when there is none: not once Tap has processed it.
    /// </returns>
    public ValueTask AckAsync(TapEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return AckAsync(evt.Id, cancellationToken);
    }

    /// <summary>Acknowledges an event by its id.</summary>
    /// <param name="id">The event's <see cref="TapEvent.Id"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the acknowledgement is sent, or queued for the next connection.
    /// </returns>
    public async ValueTask AckAsync(long id, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _socket) is not { } socket || !await TrySendAckAsync(socket, id, cancellationToken).ConfigureAwait(false))
            {
                lock (_pendingAcks)
                    _pendingAcks.Add(id);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>How many acknowledgements wait for a connection.</summary>
    internal int PendingAcks
    {
        get
        {
            lock (_pendingAcks)
                return _pendingAcks.Count;
        }
    }

    /// <summary>Makes <paramref name="socket"/> the one acknowledgements go to, and sends those that waited for it.</summary>
    private async ValueTask AttachAsync(IDuplexStreamSocket socket)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _socket, socket);

            long[] pending;
            lock (_pendingAcks)
                pending = [.. _pendingAcks];

            foreach (var id in pending)
            {
                if (!await TrySendAckAsync(socket, id, CancellationToken.None).ConfigureAwait(false))
                    break;

                lock (_pendingAcks)
                    _pendingAcks.Remove(id);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async ValueTask<bool> TrySendAckAsync(IDuplexStreamSocket socket, long id, CancellationToken cancellationToken)
    {
        try
        {
            await socket.SendTextAsync(AckMessage(id), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            // The connection died under the send: the next one carries the acknowledgement.
            _logger.LogDebug(ex, "Could not send the acknowledgement of Tap event {Id}; it waits for the next connection", id);
            return false;
        }
    }

    /// <summary>The acknowledgement <c>@atproto/tap</c> sends: <c>{"type":"ack","id":…}</c>.</summary>
    internal static byte[] AckMessage(long id) => Encoding.UTF8.GetBytes($$"""{"type":"ack","id":{{id}}}""");
}

/// <summary>A Tap admin request failed.</summary>
public sealed class TapException : AtProtoException
{
    /// <summary>Creates an exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="statusCode">The HTTP status Tap answered with, if it answered.</param>
    /// <param name="innerException">The cause, if any.</param>
    public TapException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status Tap answered with, or null when it could not be reached.</summary>
    public HttpStatusCode? StatusCode { get; }
}

