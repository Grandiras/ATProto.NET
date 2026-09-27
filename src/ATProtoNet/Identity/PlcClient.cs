using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Streaming;

namespace ATProtoNet.Identity;

/// <summary>Client for a PLC directory (<c>https://plc.directory</c>), the registry behind <c>did:plc</c>.</summary>
/// <remarks>
/// <para>Reads need no authentication. <see cref="ResolveAsync"/> makes this an
/// <see cref="IDidResolver"/> for <c>did:plc</c>; the operation log, audit log, current state,
/// export and export stream serve mirrors, auditors and PDS operators.</para>
/// <para>Requests go to absolute URLs under <see cref="DirectoryUrl"/>; an
/// <see cref="HttpClient.BaseAddress"/> is not used.</para>
/// </remarks>
/// <seealso href="https://web.plc.directory/spec/v0.1/did-plc">did:plc specification</seealso>
public sealed class PlcClient : IDidResolver, IDisposable
{
    /// <summary>The most entries one <see cref="ExportAsync"/> page may ask for.</summary>
    public const int MaxExportCount = 1000;

    // A DID's log grows by one entry per operation, and an export page holds up to 1000; both are
    // far below these, which only bound what a misbehaving directory can make the client buffer.
    private const int MaxLogBytes = 8 * 1024 * 1024;
    private const int MaxExportBytes = 32 * 1024 * 1024;
    private const int MaxStreamMessageBytes = 1024 * 1024;
    private const int MaxErrorBodyBytes = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IdentityResolverOptions _options;

    /// <summary>Creates a client for <see cref="IdentityResolverOptions.PlcDirectoryUrl"/> with its own <see cref="HttpClient"/> under the SDK's identity fetch policy.</summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    /// <exception cref="ArgumentException">
    /// The directory URL is not HTTPS and <see cref="IdentityResolverOptions.AllowPrivateNetworks"/>
    /// is not set.
    /// </exception>
    public PlcClient(IdentityResolverOptions? options = null)
    {
        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        DirectoryUrl = AtProtoHttp.ValidateServiceUrl(
            _options.PlcDirectoryUrl, nameof(options), allowInsecure: _options.AllowPrivateNetworks, allowLoopback: false);
        _httpClient = IdentityNetworkPolicy.CreateClient(_options.AllowPrivateNetworks);
        _ownsHttpClient = true;
    }

    /// <summary>Creates a client for an explicit directory that sends its requests through <paramref name="httpClient"/>.</summary>
    /// <param name="httpClient">
    /// The client to use, which the caller owns. It is used as is, so it is also how one trusted
    /// private mirror is reached without the development opt-out.
    /// </param>
    /// <param name="directoryUrl">The directory's base URL (e.g. <c>https://plc.directory</c>).</param>
    /// <param name="options">
    /// Resolver options, for the request timeout and the document size cap. Defaults apply when
    /// omitted; <see cref="IdentityResolverOptions.PlcDirectoryUrl"/> is not read.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="directoryUrl"/> is not an absolute http(s) URL, or has a query or fragment.
    /// </exception>
    public PlcClient(HttpClient httpClient, Uri directoryUrl, IdentityResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(directoryUrl);

        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        DirectoryUrl = AtProtoHttp.ValidateServiceUrl(directoryUrl, nameof(directoryUrl), allowInsecure: true);
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    /// <summary>The directory's base URL, ending in <c>/</c>.</summary>
    public Uri DirectoryUrl { get; }

    // Opens the export stream's WebSocket. Tests replace it to reach an in-process server.
    internal StreamConnector Connector { get; set; } = StreamSocket.Connector;

    /// <summary>Resolves a <c>did:plc</c> identifier to its DID document.</summary>
    /// <param name="did">The DID (e.g. <c>did:plc:ewvi7nxzyoun6zhxrhs64oiz</c>).</param>
    /// <returns>The document.</returns>
    /// <exception cref="DidResolutionException">
    /// Thrown when the DID is not found (<see cref="DidResolutionErrorKind.NotFound"/>), is
    /// tombstoned (<see cref="DidResolutionErrorKind.Deactivated"/>), or cannot be resolved.
    /// </exception>
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        return IdentityFetch.GetDidDocumentAsync(
            _httpClient, DidUrl(did, null), did, _options.MaxDidDocumentBytes, _options.RequestTimeout, cancellationToken);
    }

    /// <summary>Gets a DID's operation log: the chain of signed operations that produced its current state, nullified ones excluded.</summary>
    /// <returns>The operations, oldest first.</returns>
    /// <exception cref="DidResolutionException">Thrown when the request fails.</exception>
    public Task<IReadOnlyList<PlcOperation>> GetOperationLogAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        return GetJsonAsync<IReadOnlyList<PlcOperation>>(DidUrl(did, "log"), did, MaxLogBytes, cancellationToken);
    }

    /// <summary>Gets a DID's audit log: every operation the directory accepted, with its CID, time and whether a later operation nullified it.</summary>
    /// <returns>The entries, oldest first.</returns>
    /// <exception cref="DidResolutionException">Thrown when the request fails.</exception>
    public Task<IReadOnlyList<PlcAuditEntry>> GetAuditLogAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        return GetJsonAsync<IReadOnlyList<PlcAuditEntry>>(DidUrl(did, "log/audit"), did, MaxLogBytes, cancellationToken);
    }

    /// <summary>Gets the latest operation for a DID.</summary>
    /// <returns>The most recent operation.</returns>
    /// <exception cref="DidResolutionException">Thrown when the request fails.</exception>
    public Task<PlcOperation> GetLastOperationAsync(Did did, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        return GetJsonAsync<PlcOperation>(DidUrl(did, "log/last"), did, _options.MaxDidDocumentBytes, cancellationToken);
    }

    /// <summary>Gets a DID's current PLC state: its rotation keys, verification methods, handles and services, in operation form without <c>type</c>, <c>prev</c> or <c>sig</c>.</summary>
    /// <returns>The current state.</returns>
    /// <exception cref="DidResolutionException">Thrown when the request fails.</exception>
    public Task<PlcOperation> GetPlcDataAsync(Did did, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        return GetJsonAsync<PlcOperation>(DidUrl(did, "data"), did, _options.MaxDidDocumentBytes, cancellationToken);
    }

    /// <summary>Submits a signed PLC operation, registering or updating a DID.</summary>
    /// <param name="operation">
    /// The signed operation, as produced by <see cref="PlcOperationBuilder.Sign"/>. For a
    /// genesis operation the DID is taken from <see cref="PlcSignedOperation.Did"/>.
    /// </param>
    /// <returns>The DID the operation was submitted under.</returns>
    /// <exception cref="DidResolutionException">
    /// Thrown with <see cref="DidResolutionErrorKind.OperationRejected"/> when the directory
    /// rejects the operation.
    /// </exception>
    public Task<Did> SubmitOperationAsync(
        PlcSignedOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SubmitOperationAsync(operation.Did, operation.Operation, cancellationToken);
    }

    /// <summary>Submits a signed PLC operation under an explicit DID. Use this for update operations, where the DID is the existing one rather than a hash of the operation.</summary>
    /// <param name="did">The DID to submit under.</param>
    /// <param name="operation">The signed operation JSON.</param>
    /// <returns>The DID the operation was submitted under.</returns>
    /// <exception cref="DidResolutionException">
    /// Thrown with <see cref="DidResolutionErrorKind.OperationRejected"/> when the directory
    /// rejects the operation.
    /// </exception>
    public async Task<Did> SubmitOperationAsync(
        Did did, JsonObject operation, CancellationToken cancellationToken = default)
    {
        RequirePlc(did);
        ArgumentNullException.ThrowIfNull(operation);

        using var content = new StringContent(operation.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            () => _httpClient.PostAsync(DidUrl(did, null), content, cancellationToken), did).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Directories answer with a human-readable message, and the failure is almost always
            // actionable (bad signature, handle already claimed, rate limited), so it is surfaced.
            string body;
            try
            {
                var bytes = await response.Content.ReadBoundedAsync(MaxErrorBodyBytes, cancellationToken).ConfigureAwait(false);
                body = bytes is { } read ? Encoding.UTF8.GetString(read.Span) : "(an error body over 64 KiB)";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
            {
                body = "(an error body that could not be read)";
            }

            if (body.Length > 1024)
                body = body[..1024];

            throw new DidResolutionException(
                $"The PLC directory rejected the operation for {did} ({(int)response.StatusCode}): {body}",
                DidResolutionErrorKind.OperationRejected, did);
        }

        return did;
    }

    /// <summary>Checks whether the directory answers its health endpoint.</summary>
    /// <returns><see langword="true"/> if the directory answers with a success status.</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await IdentityFetch.GetAsync(
                _httpClient, new Uri(DirectoryUrl, "_health"), "application/json", _options.MaxDidDocumentBytes,
                _options.RequestTimeout, did: null, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess;
        }
        catch (DidResolutionException)
        {
            return false;
        }
    }

    /// <summary>Reads one page of the directory's sequenced export: every operation it accepted, in sequence order.</summary>
    /// <param name="after">
    /// The <see cref="PlcAuditEntry.Seq"/> to continue after. <c>0</c> starts at the beginning.
    /// </param>
    /// <param name="count">
    /// The most entries to return, up to <see cref="MaxExportCount"/>. <see langword="null"/> is
    /// the directory's default.
    /// </param>
    /// <returns>
    /// The entries. Continue with the last entry's <see cref="PlcAuditEntry.Seq"/>; a page shorter
    /// than <paramref name="count"/> has reached the head, from where
    /// <see cref="StreamExportAsync"/> follows new operations live.
    /// </returns>
    /// <exception cref="DidResolutionException">Thrown when the request fails.</exception>
    public async Task<IReadOnlyList<PlcAuditEntry>> ExportAsync(
        long after = 0, int? count = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        if (count is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count.Value, 1, nameof(count));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count.Value, MaxExportCount, nameof(count));
        }

        var query = $"export?after={after.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    (count is { } c ? $"&count={c.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "");
        var url = new Uri(DirectoryUrl, query);

        var result = await IdentityFetch.GetAsync(
            _httpClient, url, "application/jsonlines, application/json", MaxExportBytes, _options.RequestTimeout,
            did: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, url, did: null);

        var entries = new List<PlcAuditEntry>();
        var remaining = result.Body.Span;
        while (!remaining.IsEmpty)
        {
            var newline = remaining.IndexOf((byte)'\n');
            var line = newline < 0 ? remaining : remaining[..newline];
            remaining = newline < 0 ? default : remaining[(newline + 1)..];

            line = line.TrimEnd((byte)'\r');
            if (!line.IsEmpty)
                entries.Add(Deserialize<PlcAuditEntry>(line, url, did: null));
        }

        return entries;
    }

    /// <summary>Follows the directory's export stream (<c>/export/stream</c>): operations as the directory accepts them, over a WebSocket.</summary>
    /// <param name="cursor">
    /// The <see cref="PlcAuditEntry.Seq"/> to resume after. <see langword="null"/> streams only
    /// operations accepted after connecting.
    /// </param>
    /// <param name="cancellationToken">Cancellation token. Cancelling ends the stream.</param>
    /// <returns>The entries, in sequence order.</returns>
    /// <exception cref="EventStreamException">
    /// The stream could not be opened, or the directory closed it with a reason, which is the
    /// <see cref="EventStreamException.Error"/>: <c>OutdatedCursor</c> (the cursor predates its
    /// retention window; catch up with <see cref="ExportAsync"/>), <c>FutureCursor</c> (the cursor
    /// is ahead of the directory) or <c>ConsumerTooSlow</c> (reconnect from the last cursor).
    /// </exception>
    /// <remarks>
    /// The stream ends without an exception when the directory closes it normally or the
    /// connection drops; resume from the last <see cref="PlcAuditEntry.Seq"/> received.
    /// </remarks>
    public async IAsyncEnumerable<PlcAuditEntry> StreamExportAsync(
        long? cursor = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (cursor is not null)
            ArgumentOutOfRangeException.ThrowIfNegative(cursor.Value, nameof(cursor));

        var url = AtProtoHttp.WithScheme(
            new Uri(DirectoryUrl, "export/stream" + (cursor is { } value ? $"?cursor={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "")),
            webSocket: true);

        // The owned client's handler carries the identity fetch policy; a caller's client is used
        // as is, as for every other request.
        using var invoker = _ownsHttpClient
            ? new HttpMessageInvoker(IdentityNetworkPolicy.SharedHandler(_options.AllowPrivateNetworks), disposeHandler: false)
            : null;
        var options = new StreamSocketOptions(Invoker: invoker ?? _httpClient, MaxMessageBytes: MaxStreamMessageBytes);

        var messages = Connector(url, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                try
                {
                    if (!await messages.MoveNextAsync().ConfigureAwait(false))
                        break;
                }
                catch (Exception ex) when (ex is WebSocketException || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    // A dropped connection ends the stream; the caller resumes from its cursor.
                    break;
                }

                yield return Deserialize<PlcAuditEntry>(messages.Current.Data.Span, url, did: null);
            }
        }
        finally
        {
            await messages.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<T> GetJsonAsync<T>(Uri url, Did did, int maxBytes, CancellationToken cancellationToken)
    {
        var result = await IdentityFetch.GetAsync(
            _httpClient, url, "application/json", maxBytes, _options.RequestTimeout, did, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, url, did);
        return Deserialize<T>(result.Body.Span, url, did);
    }

    private static void EnsureSuccess(IdentityFetch.Result result, Uri url, Did? did)
    {
        if (result.IsSuccess)
            return;

        throw result.Status switch
        {
            HttpStatusCode.NotFound => new DidResolutionException(
                $"The PLC directory has no {url.AbsolutePath} for {did}.", DidResolutionErrorKind.NotFound, did),
            HttpStatusCode.Gone => new DidResolutionException(
                $"{did} has been deactivated.", DidResolutionErrorKind.Deactivated, did),
            _ => new DidResolutionException(
                $"The PLC directory answered {url.AbsolutePath} with HTTP {(int)result.Status}.",
                DidResolutionErrorKind.HttpError, did),
        };
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> json, Uri url, Did? did)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, AtProtoJsonDefaults.Options)
                ?? throw new JsonException("The response is null.");
        }
        catch (JsonException ex)
        {
            throw new DidResolutionException(
                $"The PLC directory's answer to {url.AbsolutePath} is malformed: {ex.Message}",
                DidResolutionErrorKind.InvalidDocument, did, ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, Did did)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new DidResolutionException(
                $"Could not reach the PLC directory at {DirectoryUrl.Host}: {ex.Message}",
                IdentityNetworkPolicy.IsBlocked(ex) ? DidResolutionErrorKind.Blocked : DidResolutionErrorKind.NetworkError,
                did, ex);
        }
    }

    // A bare "did:plc:…" parses as an absolute URI with the scheme "did" and would replace the
    // directory entirely, so the DID is anchored as a relative path segment.
    private Uri DidUrl(Did did, string? suffix) =>
        new(DirectoryUrl, suffix is null ? "./" + did.Value : $"./{did.Value}/{suffix}");

    private static void RequirePlc(Did did)
    {
        ArgumentNullException.ThrowIfNull(did);
        if (did.Method != "plc")
            throw new DidResolutionException($"'{did}' is not a did:plc.", DidResolutionErrorKind.UnsupportedMethod, did);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

/// <summary>A PLC operation: a signed state transition for a DID, or, from <see cref="PlcClient.GetPlcDataAsync"/>, the state the latest one produced.</summary>
public sealed class PlcOperation
{
    /// <summary>The operation type: <c>plc_operation</c>, <c>plc_tombstone</c>, or the legacy <c>create</c>. <see langword="null"/> on a state read.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The services, keyed by id without the leading <c>#</c> (e.g. <c>atproto_pds</c>).</summary>
    [JsonPropertyName("services")]
    public IReadOnlyDictionary<string, DidService>? Services { get; init; }

    /// <summary>The <c>alsoKnownAs</c> entries, including the handle as <c>at://handle</c>.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public IReadOnlyList<string>? AlsoKnownAs { get; init; }

    /// <summary>The rotation keys as <c>did:key</c> strings, highest authority first.</summary>
    [JsonPropertyName("rotationKeys")]
    public IReadOnlyList<string>? RotationKeys { get; init; }

    /// <summary>The verification methods as <c>did:key</c> strings, keyed by id without the leading <c>#</c>.</summary>
    [JsonPropertyName("verificationMethods")]
    public IReadOnlyDictionary<string, string>? VerificationMethods { get; init; }

    /// <summary>The CID of the previous operation, or <see langword="null"/> for a genesis operation.</summary>
    [JsonPropertyName("prev")]
    public Cid? Prev { get; init; }

    /// <summary>The signature, as unpadded base64url.</summary>
    [JsonPropertyName("sig")]
    public string? Sig { get; init; }
}

/// <summary>An operation as the directory recorded it: from a DID's audit log, or from the export.</summary>
public sealed class PlcAuditEntry
{
    /// <summary>The entry type: <c>sequenced_op</c> in the sequenced export, <see langword="null"/> in an audit log.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The DID the operation belongs to.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The signed operation the directory recorded.</summary>
    [JsonPropertyName("operation")]
    public required PlcOperation Operation { get; init; }

    /// <summary>The operation's CID.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>Whether a later operation nullified this one. <see langword="null"/> in the sequenced export, which does not report it.</summary>
    [JsonPropertyName("nullified")]
    public bool? Nullified { get; init; }

    /// <summary>When the directory accepted the operation.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The directory's sequence number for the operation, strictly increasing; the cursor for <see cref="PlcClient.ExportAsync"/> and <see cref="PlcClient.StreamExportAsync"/>. <see langword="null"/> in an audit log.</summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; init; }
}
