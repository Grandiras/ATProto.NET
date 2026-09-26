using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Streaming;

/// <summary>
/// Downloads a repository's full export (<c>com.atproto.sync.getRepo</c>) from somewhere: the
/// source a resynchronization fetches from.
/// </summary>
/// <remarks>
/// The CAR is verified after it is downloaded, so a fetcher needs no trust in the host it asks.
/// <see cref="HostRepoFetcher"/> asks one host, such as the relay; <see cref="PdsRepoFetcher"/>
/// asks the account's own PDS. Implement this to fetch from a cache or mirror of your own.
/// </remarks>
public interface IRepoFetcher
{
    /// <summary>
    /// Downloads the repository's CAR export, or returns null when this source does not have it.
    /// </summary>
    /// <param name="did">The repository.</param>
    /// <param name="maxBytes">The largest export to accept.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="RepoFetchException">The source failed, or the export exceeds <paramref name="maxBytes"/>.</exception>
    Task<byte[]?> FetchAsync(Did did, long maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches repositories from one host: the relay a firehose comes from, or a mirror.
/// </summary>
/// <remarks>
/// Asking the relay first is what the sync spec recommends, so many consumers resynchronizing
/// one repository do not descend on its PDS at once: the relay can coalesce and cache the export,
/// or redirect to the PDS, and a redirect is followed (up to three times).
/// </remarks>
public sealed class HostRepoFetcher : IRepoFetcher
{
    private readonly Uri _host;
    private readonly HttpClient _httpClient;
    private readonly bool _allowHttp;

    /// <summary>
    /// Creates a fetcher for one host.
    /// </summary>
    /// <param name="host">The host's base URL, e.g. <c>https://bsky.network</c>.</param>
    /// <param name="httpClient">
    /// The client to send requests with; its <see cref="HttpClient.Timeout"/> bounds each whole
    /// download, body included. Default: one that refuses private and loopback addresses, as the
    /// SDK's identity fetches do, with a 5-minute timeout.
    /// </param>
    public HostRepoFetcher(Uri host, HttpClient? httpClient = null)
        : this(host, httpClient ?? RepoDownload.CreateClient(allowPrivateNetworks: false, RepoDownload.DefaultTimeout), allowHttp: httpClient is not null)
    {
    }

    internal HostRepoFetcher(Uri host, HttpClient httpClient, bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.IsAbsoluteUri || host.Scheme is not ("https" or "http"))
            throw new ArgumentException($"'{host}' is not an http(s) URL.", nameof(host));

        _host = host;
        _httpClient = httpClient;
        _allowHttp = allowHttp;
    }

    /// <inheritdoc/>
    public Task<byte[]?> FetchAsync(Did did, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return RepoDownload.GetRepoAsync(_httpClient, _host, did, maxBytes, _allowHttp, cancellationToken);
    }

    /// <inheritdoc/>
    public override string ToString() => _host.GetLeftPart(UriPartial.Authority);
}

/// <summary>
/// Fetches a repository from the PDS its account's DID document names.
/// </summary>
public sealed class PdsRepoFetcher : IRepoFetcher
{
    private readonly IDidResolver _didResolver;
    private readonly HttpClient _httpClient;
    private readonly bool _allowHttp;

    /// <summary>
    /// Creates a fetcher.
    /// </summary>
    /// <param name="didResolver">Resolves each account's PDS. A caching resolver avoids a lookup per fetch.</param>
    /// <param name="httpClient">
    /// The client to send requests with; its <see cref="HttpClient.Timeout"/> bounds each whole
    /// download, body included. Default: one that refuses private and loopback addresses (the PDS
    /// URL comes from a document anyone can publish), with a 5-minute timeout.
    /// </param>
    public PdsRepoFetcher(IDidResolver didResolver, HttpClient? httpClient = null)
        : this(didResolver, httpClient ?? RepoDownload.CreateClient(allowPrivateNetworks: false, RepoDownload.DefaultTimeout), allowHttp: httpClient is not null)
    {
    }

    internal PdsRepoFetcher(IDidResolver didResolver, HttpClient httpClient, bool allowHttp)
    {
        _didResolver = didResolver ?? throw new ArgumentNullException(nameof(didResolver));
        _httpClient = httpClient;
        _allowHttp = allowHttp;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> FetchAsync(Did did, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        DidDocument document;
        try
        {
            document = await _didResolver.ResolveAsync(did, cancellationToken).ConfigureAwait(false);
        }
        catch (DidResolutionException ex)
        {
            throw new RepoFetchException($"Could not resolve {did} to find its PDS: {ex.Message}", ex);
        }

        var pds = document.GetPdsEndpoint()
            ?? throw new RepoFetchException($"{did} names no PDS.");

        return await RepoDownload.GetRepoAsync(_httpClient, pds, did, maxBytes, _allowHttp, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override string ToString() => "the account's PDS";
}

/// <summary>A repository could not be fetched from a source.</summary>
public sealed class RepoFetchException : AtProtoException
{
    /// <summary>Creates an exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause, if any.</param>
    public RepoFetchException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The download behind the repository fetchers.</summary>
internal static class RepoDownload
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private const int MaxRedirects = 3;

    /// <summary>The most a download's buffer starts at, whatever length the host declares.</summary>
    private const int InitialBufferBytes = 1024 * 1024;

    /// <summary>A client over the SDK's SSRF-hardened handler, or one allowing private networks for development.</summary>
    internal static HttpClient CreateClient(bool allowPrivateNetworks, TimeSpan timeout)
    {
        var client = new HttpClient(IdentityNetworkPolicy.SharedHandler(allowPrivateNetworks), disposeHandler: false)
        {
            Timeout = timeout,
        };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd(AtProtoHttp.DefaultUserAgent);
        return client;
    }

    /// <summary>
    /// Downloads <paramref name="did"/>'s export from <paramref name="host"/>, following redirects,
    /// within the client's <see cref="HttpClient.Timeout"/> for the whole download: the response
    /// is read as it streams in, which <see cref="HttpClient.Timeout"/> alone does not cover.
    /// </summary>
    internal static async Task<byte[]?> GetRepoAsync(
        HttpClient client, Uri host, Did did, long maxBytes, bool allowHttp, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
            budget.CancelAfter(client.Timeout);

        try
        {
            return await GetRepoCoreAsync(client, host, did, maxBytes, allowHttp, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget ran out, not the caller's patience: a host that trickles the body is a
            // failed source, and the next one is tried.
            throw new RepoFetchException($"Fetching {did} from {host.Host} took longer than {client.Timeout}.", ex);
        }
    }

    private static async Task<byte[]?> GetRepoCoreAsync(
        HttpClient client, Uri host, Did did, long maxBytes, bool allowHttp, CancellationToken cancellationToken)
    {
        var uri = new Uri(host, $"/xrpc/com.atproto.sync.getRepo?did={Uri.EscapeDataString(did.Value)}");
        for (var redirects = 0; ; redirects++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
                throw new RepoFetchException($"Refusing to fetch {did} over {uri.Scheme}.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/vnd.ipld.car");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new RepoFetchException($"Could not reach {uri.Host} to fetch {did}: {ex.Message}", ex);
            }

            using (response)
            {
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    if (redirects >= MaxRedirects)
                        throw new RepoFetchException($"Fetching {did} redirected more than {MaxRedirects} times.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }

                // A host that does not hold the repository says so with a 4xx: try the next one.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                    return null;

                if (!response.IsSuccessStatusCode)
                    throw new RepoFetchException($"{uri.Host} answered HTTP {(int)response.StatusCode} for {did}.");

                if (response.Content.Headers.ContentLength > maxBytes)
                    throw new RepoFetchException($"{did}'s repository is larger than {maxBytes} bytes.");

                return await ReadBoundedAsync(response.Content, response.Content.Headers.ContentLength, maxBytes, did, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content, long? declared, long maxBytes, Did did, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // A declared length sizes the buffer up to 1 MiB and no further: the header is the host's
        // word, so a larger claim must not reserve memory before the bytes arrive.
        using var buffer = new MemoryStream((int)Math.Min(declared ?? 0, InitialBufferBytes));
        var chunk = new byte[81920];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new RepoFetchException($"The download of {did}'s repository broke off: {ex.Message}", ex);
            }

            if (read == 0)
            {
                return buffer.Length == buffer.Capacity && buffer.TryGetBuffer(out var whole) && whole.Offset == 0
                    ? whole.Array!
                    : buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
                throw new RepoFetchException($"{did}'s repository is larger than {maxBytes} bytes.");

            buffer.Write(chunk, 0, read);
        }
    }
}
