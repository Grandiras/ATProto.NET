using System.Buffers.Binary;
using System.Globalization;
using ATProtoNet.Http;

namespace ATProtoNet.Streaming;

/// <summary>
/// A zstd dictionary served by <c>network.bsky.jetstream.getZstdDictionary</c>, together with
/// the ID that identifies it on the wire.
/// </summary>
/// <param name="Id">The dictionary ID to pass as <see cref="JetstreamConsumerOptions.ZstdDictionaryId"/>.</param>
/// <param name="Data">The raw zstd structured dictionary (RFC 8878 §5) to build a decompressor with.</param>
public sealed record JetstreamZstdDictionary(int Id, byte[] Data);

/// <summary>
/// Fetches the zstd dictionary that <see cref="JetstreamProtocol.V2"/> frame compression uses.
/// </summary>
/// <remarks>
/// <para>Compressed v2 frames are dictionary-versioned: the client fetches the server's current
/// dictionary, builds an <see cref="IJetstreamDecompressor"/> with it, and opts in by setting
/// both <see cref="JetstreamConsumerOptions.Decompressor"/> and
/// <see cref="JetstreamConsumerOptions.ZstdDictionaryId"/>. A dictionary is immutable for a given
/// ID and may be retired after the server retrains, at which point connecting is rejected with an
/// HTTP 400 and the current ID — fetch again with no ID to get it.</para>
/// <para>The SDK ships no zstd implementation, so this only retrieves the dictionary bytes;
/// decompression stays the caller's (see the Jetstream documentation page for a
/// <c>ZstdSharp.Port</c> implementation).</para>
/// </remarks>
/// <example>
/// <code>
/// using var dictionaries = new JetstreamDictionaryClient(JetstreamEndpoints.UsEast);
/// var dictionary = await dictionaries.GetDictionaryAsync();
///
/// var options = new JetstreamConsumerOptions
/// {
///     ServiceUrl = JetstreamEndpoints.UsEast,
///     Protocol = JetstreamProtocol.V2,
///     ZstdDictionaryId = dictionary.Id,
///     Decompressor = new ZstdDictionaryDecompressor(dictionary.Data),
/// };
/// </code>
/// </example>
public sealed class JetstreamDictionaryClient : IDisposable
{
    /// <summary>The four-byte little-endian magic number a zstd structured dictionary starts with.</summary>
    private const uint DictionaryMagic = 0xEC30A437;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;
    private bool _disposed;

    /// <summary>
    /// Create a dictionary client for a Jetstream host.
    /// </summary>
    /// <param name="serviceUrl">The Jetstream host URL. <c>ws(s)</c> schemes are converted to
    /// <c>http(s)</c> automatically, so the same value can configure both this and
    /// <see cref="JetstreamConsumerOptions.ServiceUrl"/>.</param>
    /// <param name="httpClient">An <see cref="HttpClient"/> to send with. When null, one is
    /// created and disposed with this instance.</param>
    public JetstreamDictionaryClient(string serviceUrl, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _baseUri = new Uri(ToHttpUrl(serviceUrl), UriKind.Absolute);
        _http = httpClient ?? AtProtoHttp.CreateClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Fetch a zstd dictionary from the server.
    /// </summary>
    /// <param name="id">The dictionary ID to fetch. When null (the default), the server returns
    /// its current dictionary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dictionary bytes and the ID read out of them.</returns>
    /// <exception cref="JetstreamConnectException">The server refused the request (e.g.
    /// <c>DictionaryNotFound</c>) or returned something that is not a zstd dictionary.</exception>
    public async Task<JetstreamZstdDictionary> GetDictionaryAsync(
        int? id = null,
        CancellationToken cancellationToken = default)
    {
        var path = "xrpc/network.bsky.jetstream.getZstdDictionary"
            + (id.HasValue ? $"?id={id.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty);

        using var response = await _http.GetAsync(new Uri(_baseUri, path), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A proxy error page rather than an XRPC envelope leaves the status as the whole story.
            var error = (await XrpcResponseReader.ReadErrorAsync(response, cancellationToken)).Error;
            throw new JetstreamConnectException(
                $"Jetstream refused the zstd dictionary request with HTTP {(int)response.StatusCode}" +
                (error is null ? "." : $" ({error})."),
                (int)response.StatusCode)
            {
                Error = error,
            };
        }

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // A structured dictionary carries its own ID in its header, which is the same ID the
        // subscription's zstdDictionary parameter takes — so a bare "current dictionary" fetch
        // is enough to configure a subscription, with no second call to learn the ID.
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != DictionaryMagic)
            throw new JetstreamConnectException(
                $"Jetstream returned {data.Length} bytes that are not a zstd dictionary.",
                (int)response.StatusCode);

        return new JetstreamZstdDictionary(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)),
            data);
    }

    private static string ToHttpUrl(string serviceUrl)
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
}
