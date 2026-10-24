using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ATProtoNet.Net;

namespace ATProtoNet.Identity.Plc;

/// <summary>A PLC directory (<c>https://plc.directory</c>), the registry behind <c>did:plc</c>: documents, operation logs, state, submission and export.</summary>
/// <remarks>Requests go through <see cref="UntrustedHttp"/> to absolute URLs under <see cref="Directory"/>. See <see href="https://web.plc.directory/spec/v0.1/did-plc"/>.</remarks>
public sealed class PlcClient : IDidResolver
{
    /// <summary>The largest document or single operation accepted, in bytes.</summary>
    public const int MaxDocumentBytes = 64 * 1024;

    /// <summary>The largest operation or audit log accepted, in bytes.</summary>
    public const int MaxLogBytes = 8 * 1024 * 1024;

    /// <summary>The largest export page accepted, in bytes.</summary>
    public const int MaxExportBytes = 32 * 1024 * 1024;

    /// <summary>The most entries one export page may ask for.</summary>
    public const int MaxExportCount = 1000;

    private readonly UntrustedHttp _http;

    /// <summary>Creates a client.</summary>
    /// <param name="http">The fetch layer, which the caller owns.</param>
    /// <param name="directory">The directory, <c>https</c> unless <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/> is set. Defaults to <see cref="DefaultDirectory"/>; a path prefix is kept.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is not an absolute URL of an allowed scheme, or has a query or fragment.</exception>
    public PlcClient(UntrustedHttp http, Uri? directory = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        directory ??= DefaultDirectory;
        var scheme = directory.IsAbsoluteUri && (directory.Scheme == Uri.UriSchemeHttps || (directory.Scheme == Uri.UriSchemeHttp && http.Policy.AllowPrivateNetworks));
        if (!scheme || (!directory.IsDefaultPort && !http.Policy.AllowPrivateNetworks) || directory.Query.Length > 0 || directory.Fragment.Length > 0)
            throw new ArgumentException($"The PLC directory must be an absolute https URL on the default port without query or fragment (http and ports only under {nameof(UntrustedFetchPolicy.AllowPrivateNetworks)}).", nameof(directory));
        _http = http;
        Directory = directory.AbsoluteUri.EndsWith('/') ? directory : new Uri(directory.AbsoluteUri + "/");
    }

    /// <summary>The public directory.</summary>
    public static Uri DefaultDirectory { get; } = new("https://plc.directory/");

    /// <summary>The directory's base URL.</summary>
    public Uri Directory { get; }

    /// <inheritdoc />
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
        DidDocumentFetch.GetAsync(_http, Url(did, null), did, MaxDocumentBytes, cancellationToken);

    /// <summary>The DID's operations in order, nullified ones excluded.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The operations.</returns>
    /// <exception cref="IdentityResolutionException">The request failed or the answer is malformed.</exception>
    public async Task<IReadOnlyList<SignedPlcOperation>> GetOperationLogAsync(Did did, CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(Url(did, "log"), did, MaxLogBytes, cancellationToken).ConfigureAwait(false);
        return Read(did, () => Array(body).Select(e => SignedPlcOperation.Parse(JsonMarshal.GetRawUtf8Value(e))).ToArray());
    }

    /// <summary>Every operation of the DID with its CID, time and whether it was nullified.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The audit log.</returns>
    /// <exception cref="IdentityResolutionException">The request failed or the answer is malformed.</exception>
    public async Task<IReadOnlyList<PlcAuditEntry>> GetAuditLogAsync(Did did, CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(Url(did, "log/audit"), did, MaxLogBytes, cancellationToken).ConfigureAwait(false);
        return Read(did, () => Array(body).Select(PlcAuditEntry.Read).ToArray());
    }

    /// <summary>The DID's latest operation.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The operation.</returns>
    /// <exception cref="IdentityResolutionException">The request failed or the answer is malformed.</exception>
    public async Task<SignedPlcOperation> GetLastOperationAsync(Did did, CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(Url(did, "log/last"), did, MaxDocumentBytes, cancellationToken).ConfigureAwait(false);
        return Read(did, () => SignedPlcOperation.Parse(body.Span));
    }

    /// <summary>The DID's current state (<c>/data</c>), as the update that would recreate it.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The state; its <see cref="PlcOperation.Prev"/> is null.</returns>
    /// <exception cref="IdentityResolutionException">The request failed (a tombstoned DID is <see cref="IdentityResolutionError.Deactivated"/>) or the answer is malformed.</exception>
    public async Task<PlcUpdate> GetStateAsync(Did did, CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(Url(did, "data"), did, MaxDocumentBytes, cancellationToken).ConfigureAwait(false);
        return Read(did, () => Data.DataModelValue.FromJson(body.Span) is Data.DataModelMap map ? PlcOperation.Update(map, null) : throw new FormatException("The state is not an object."));
    }

    /// <summary>Submits a signed operation for <paramref name="did"/>.</summary>
    /// <param name="did">The DID; for a genesis operation, <see cref="SignedPlcOperation.DeriveDid"/>.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>When the directory accepted it.</returns>
    /// <exception cref="PlcOperationRejectedException">The directory refused the operation.</exception>
    /// <exception cref="UntrustedFetchException">The directory could not be reached.</exception>
    public async Task SubmitAsync(Did did, SignedPlcOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var content = new ByteArrayContent(operation.ToJson().ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        var response = await _http.PostAsync(Url(did, null), content, new() { MaxBodyBytes = UntrustedHttp.MaxErrorBodyBytes }, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess) return;
        var message = Encoding.UTF8.GetString(response.Body.Span);
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
        }
        catch (JsonException) { }
        throw new PlcOperationRejectedException($"The PLC directory refused the operation for {did} (HTTP {(int)response.Status}): {message}", message);
    }

    /// <summary>A page of the directory's export: every DID's operations, in the directory's order.</summary>
    /// <param name="after">The sequence number to continue after (the last entry's <see cref="PlcAuditEntry.Seq"/>), 0 from the start.</param>
    /// <param name="count">The most entries, 1 to <see cref="MaxExportCount"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The entries.</returns>
    /// <exception cref="IdentityResolutionException">The request failed or the answer is malformed.</exception>
    public async Task<IReadOnlyList<PlcAuditEntry>> ExportAsync(long after = 0, int count = MaxExportCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxExportCount);
        var body = await GetAsync(new Uri(Directory, $"export?after={after}&count={count}"), null, MaxExportBytes, cancellationToken).ConfigureAwait(false);
        var entries = new List<PlcAuditEntry>();
        for (var rest = body; !rest.IsEmpty;)
        {
            var end = rest.Span.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? default : rest[(end + 1)..];
            if (!line.Span.Trim(" \r\t"u8).IsEmpty) entries.Add(Read(null, () => { using var json = JsonDocument.Parse(line); return PlcAuditEntry.Read(json.RootElement); }));
        }
        return entries;
    }

    /// <summary>Whether the directory reports itself healthy (<c>/_health</c>).</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether it answered 2xx; an unreachable directory is unhealthy.</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _http.GetAsync(new Uri(Directory, "_health"), new() { MaxBodyBytes = MaxDocumentBytes }, cancellationToken).ConfigureAwait(false)).IsSuccess;
        }
        catch (UntrustedFetchException)
        {
            return false;
        }
    }

    private Task<ReadOnlyMemory<byte>> GetAsync(Uri url, Did? did, int maxBytes, CancellationToken cancellationToken) =>
        DidDocumentFetch.FetchAsync(_http, url, did, maxBytes, "application/json", cancellationToken);

    // The elements of a JSON array body, read within the caller's enumeration while the document is alive.
    private static IEnumerable<JsonElement> Array(ReadOnlyMemory<byte> body)
    {
        using var json = JsonDocument.Parse(body);
        foreach (var e in json.RootElement.EnumerateArray()) yield return e;
    }

    private static T Read<T>(Did? did, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw new IdentityResolutionException(IdentityResolutionError.InvalidDocument, $"The PLC directory's answer is malformed: {ex.Message}", did, ex);
        }
    }

    // The DID is a path segment under the directory: a bare "did:plc:…" would parse as an absolute URI and replace it.
    private Uri Url(Did did, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(did);
        if (did.Method != "plc") throw new IdentityResolutionException(IdentityResolutionError.UnsupportedMethod, $"'{did}' is not a did:plc.", did);
        if (did.MethodSpecificId is not { Length: 24 } id || id.ContainsAnyExcept("abcdefghijklmnopqrstuvwxyz234567"))
            throw new IdentityResolutionException(IdentityResolutionError.InvalidDid, $"'{did}' is not a valid did:plc.", did);
        return new(Directory, "./" + did.Value + (suffix is null ? "" : "/" + suffix));
    }
}

/// <summary>An entry of a DID's audit log or of the directory's export.</summary>
/// <param name="Did">The DID.</param>
/// <param name="Operation">The signed operation.</param>
/// <param name="Cid">The operation's CID, as the directory reports it.</param>
/// <param name="Nullified">Whether a recovery nullified the operation.</param>
/// <param name="CreatedAt">When the directory accepted it.</param>
/// <param name="Seq">The export sequence number, when the directory gives one.</param>
public sealed record PlcAuditEntry(Did Did, SignedPlcOperation Operation, Cid Cid, bool Nullified, DateTimeOffset CreatedAt, long? Seq)
{
    internal static PlcAuditEntry Read(JsonElement e) => new(
        Did.Parse(e.GetProperty("did").GetString()!),
        SignedPlcOperation.Parse(JsonMarshal.GetRawUtf8Value(e.GetProperty("operation"))),
        Cid.Parse(e.GetProperty("cid").GetString()!),
        e.TryGetProperty("nullified", out var n) && n.GetBoolean(),
        e.GetProperty("createdAt").GetDateTimeOffset(),
        e.TryGetProperty("seq", out var s) ? s.GetInt64() : null);
}

/// <summary>The PLC directory refused an operation; not an identity-resolution failure.</summary>
public sealed class PlcOperationRejectedException : AtProtoException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="directoryMessage">The directory's own message, if it gave one.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public PlcOperationRejectedException(string message, string? directoryMessage = null, Exception? innerException = null)
        : base(message, innerException) => DirectoryMessage = directoryMessage;

    /// <summary>The directory's own message, if it gave one.</summary>
    public string? DirectoryMessage { get; }
}
