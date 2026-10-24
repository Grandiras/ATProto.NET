using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Net;

/// <summary>The one path to hosts that untrusted input names: DID documents, handle well-knowns, the PLC directory, DNS-over-HTTPS, OAuth metadata.</summary>
/// <remarks>
/// <para>Owns the hardened handler and the concurrency gate; create one per application. The handler uses no proxy, follows no
/// redirects, decompresses nothing and checks, after DNS, <em>every</em> address a host resolves to, connecting only to those
/// addresses, so a name resolving to one public and one private address is refused.</para>
/// <para>Every fetch has a total budget (<see cref="UntrustedFetchPolicy.RequestTimeout"/>), a capped body, and refuses a response
/// whose final URL differs from the one requested. Failures are <see cref="UntrustedFetchException"/>; caller cancellation is
/// <see cref="OperationCanceledException"/>.</para>
/// </remarks>
public sealed partial class UntrustedHttp : IDisposable
{
    /// <summary>The most bytes kept of a body that is not a success.</summary>
    public const int MaxErrorBodyBytes = 64 * 1024;

    private readonly HttpMessageHandler _handler;
    private readonly bool _ownsHandler;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _time;

    /// <summary>Creates the layer.</summary>
    /// <param name="policy">The policy. Defaults apply when omitted.</param>
    /// <param name="time">The clock budgets run on. Defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="handler">
    /// A handler to send through instead of the hardened one, which the caller owns. The address check is then its job
    /// (<see cref="IsEnforced"/> is false); the URL, budget, body and redirect rules still apply.
    /// </param>
    public UntrustedHttp(UntrustedFetchPolicy? policy = null, TimeProvider? time = null, ILogger? logger = null, HttpMessageHandler? handler = null)
    {
        Policy = policy ?? new();
        Policy.Validate();
        _time = time ?? TimeProvider.System;
        _slots = new(Policy.MaxConcurrentFetches, Policy.MaxConcurrentFetches);
        _ownsHandler = handler is null;
        _handler = handler ?? CreateHandler(Policy);
        _client = CreateClient();
        IsEnforced = _ownsHandler && SocketsHttpHandler.IsSupported && !Policy.AllowPrivateNetworks;
        var log = logger ?? NullLogger.Instance;
        if (Policy.AllowPrivateNetworks) LogPrivateNetworksAllowed(log);
        else if (_ownsHandler && !IsEnforced) LogNotEnforced(log);
    }

    /// <summary>The policy fetches run under.</summary>
    public UntrustedFetchPolicy Policy { get; }

    /// <summary>Whether the address check runs: false over a caller's handler, under <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/>, and where connections cannot be intercepted (browser WebAssembly).</summary>
    public bool IsEnforced { get; }

    /// <summary>A client over the policy's handler: no proxy, no redirects, no timeout of its own. The caller disposes it; the handler stays.</summary>
    /// <returns>The client.</returns>
    public HttpClient CreateClient() => new(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>GETs <paramref name="url"/>.</summary>
    /// <param name="url">The URL: <c>https</c> on the default port, unless <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/> is set.</param>
    /// <param name="request">What the fetch accepts.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The response, whatever its status; redirects are followed only as <paramref name="request"/> allows.</returns>
    /// <exception cref="UntrustedFetchException">The fetch failed.</exception>
    public Task<UntrustedResponse> GetAsync(Uri url, UntrustedRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, url, null, request, cancellationToken);

    /// <summary>POSTs <paramref name="content"/> to <paramref name="url"/>, disposing it once sent. Redirects are refused.</summary>
    /// <param name="url">The URL, under the same rules as <see cref="GetAsync"/>.</param>
    /// <param name="content">The body.</param>
    /// <param name="request">What the fetch accepts.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The response, whatever its status.</returns>
    /// <exception cref="UntrustedFetchException">The fetch failed.</exception>
    public Task<UntrustedResponse> PostAsync(Uri url, HttpContent content, UntrustedRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, url, content ?? throw new ArgumentNullException(nameof(content)), (request ?? throw new ArgumentNullException(nameof(request))) with { MaxSameHostRedirects = 0 }, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        if (_ownsHandler) _handler.Dispose();
        _slots.Dispose();
    }

    private async Task<UntrustedResponse> SendAsync(HttpMethod method, Uri url, HttpContent? content, UntrustedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaxBodyBytes);
        CheckUrl(url);
        using var timeout = new CancellationTokenSource(Policy.RequestTimeout, _time);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var slot = false;
        try
        {
            await _slots.WaitAsync(budget.Token).ConfigureAwait(false);
            slot = true;
            for (var hops = 0; ; hops++)
            {
                using var message = new HttpRequestMessage(method, url) { Content = content, Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
                if (request.Accept is { } accept) message.Headers.TryAddWithoutValidation("Accept", accept);
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
                // A caller's handler may have followed a redirect itself.
                if (response.RequestMessage?.RequestUri is { } final && final != url)
                    throw new UntrustedFetchException(UntrustedFetchError.RedirectRefused, url, $"{url.Host} was redirected to {final.Host}.", response.StatusCode);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    url = NextHop(url, response, hops < request.MaxSameHostRedirects);
                    continue;
                }
                var body = await ReadAsync(url, response, request.MaxBodyBytes, budget.Token).ConfigureAwait(false);
                return new UntrustedResponse(response.StatusCode, body, url);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UntrustedFetchException(UntrustedFetchError.Timeout, url, $"{url.Host} did not answer within {Policy.RequestTimeout.TotalSeconds:0.#} s.", innerException: ex);
        }
        catch (HttpRequestException ex) when (IsBlocked(ex))
        {
            throw new UntrustedFetchException(UntrustedFetchError.Blocked, url, $"The fetch policy refused {url.Host}: {ex.InnerException?.Message ?? ex.Message}", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new UntrustedFetchException(UntrustedFetchError.Network, url, $"Could not read from {url.Host}: {ex.Message}", innerException: ex);
        }
        finally
        {
            if (slot) _slots.Release();
        }
    }

    private void CheckUrl(Uri url)
    {
        if (!url.IsAbsoluteUri) throw new ArgumentException("The URL must be absolute.", nameof(url));
        var allowed = url.Scheme == Uri.UriSchemeHttps ? Policy.AllowPrivateNetworks || url.IsDefaultPort
            : url.Scheme == Uri.UriSchemeHttp && Policy.AllowPrivateNetworks;
        if (!allowed) throw new UntrustedFetchException(UntrustedFetchError.Blocked, url, $"Only https URLs on the default port are fetched without {nameof(UntrustedFetchPolicy.AllowPrivateNetworks)}: '{url}'.");
    }

    private Uri NextHop(Uri url, HttpResponseMessage response, bool mayFollow)
    {
        Uri? next = null;
        try
        {
            if (response.Headers.Location is { } location) next = new Uri(url, location);
        }
        catch (UriFormatException ex)
        {
            throw new UntrustedFetchException(UntrustedFetchError.Malformed, url, $"{url.Host} redirected to an unusable location.", response.StatusCode, ex);
        }
        if (!mayFollow || next is null || next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, url.Host, StringComparison.OrdinalIgnoreCase))
            throw new UntrustedFetchException(UntrustedFetchError.RedirectRefused, url, $"{url.Host} redirected to {next?.ToString() ?? "nowhere"}.", response.StatusCode);
        CheckUrl(next);
        return next;
    }

    // A success over the cap is refused, declared or actual; other bodies keep their first MaxErrorBodyBytes.
    private static async Task<ReadOnlyMemory<byte>> ReadAsync(Uri url, HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var truncate = !response.IsSuccessStatusCode;
        if (truncate) maxBytes = Math.Min(maxBytes, MaxErrorBodyBytes);
        var declared = response.Content.Headers.ContentLength;
        if (declared > maxBytes && !truncate) throw TooLarge(url, maxBytes);
        // A declared length is the sender's word, so it sizes the first buffer only up to a bound.
        var buffer = new byte[(int)Math.Min(declared ?? 16 * 1024, Math.Min(maxBytes, 1024 * 1024)) + 1];
        var read = 0;
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            if (read == buffer.Length)
            {
                if (read > maxBytes) return truncate ? buffer.AsMemory(0, maxBytes) : throw TooLarge(url, maxBytes);
                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, maxBytes + 1L));
            }
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0) return buffer.AsMemory(0, read);
            read += n;
        }
    }

    private static UntrustedFetchException TooLarge(Uri url, int maxBytes) =>
        new(UntrustedFetchError.ResponseTooLarge, url, $"The response from {url.Host} is larger than the {maxBytes} bytes accepted.");

    private static HttpMessageHandler CreateHandler(UntrustedFetchPolicy policy)
    {
        // The browser makes the connection itself; the final-URL check still refuses redirects it followed.
        if (!SocketsHttpHandler.IsSupported) return new HttpClientHandler();
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = policy.ConnectTimeout,
            MaxResponseHeadersLength = (policy.MaxResponseHeadersBytes + 1023) / 1024,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        if (!policy.AllowPrivateNetworks) handler.ConnectCallback = ConnectToPublicAddressAsync;
        return handler;
    }

    // Checked after DNS against every address, and the connection made to those same addresses: checking the name, or
    // resolving twice, would let a rebinding DNS server answer differently the second time.
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
        if (Array.Find(addresses, a => !UntrustedFetchPolicy.IsPublicAddress(a)) is { } refused)
            throw new AddressRefusedException($"'{host}' resolves to {refused}, which is not a public address.");
        // A fresh socket per address: some stacks cannot reuse one after a failed connect.
        for (var i = 0; ; i++)
        {
            var socket = new Socket(addresses[i].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses[i], context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (i < addresses.Length - 1)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static bool IsBlocked(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is AddressRefusedException) return true;
        return false;
    }

    [LoggerMessage(1000, LogLevel.Warning, "Untrusted fetches may reach private networks, loopback and plain http (AllowPrivateNetworks); use this for development only.")]
    private static partial void LogPrivateNetworksAllowed(ILogger logger);

    [LoggerMessage(1001, LogLevel.Warning, "The untrusted-fetch address policy cannot be enforced on this platform; body, time and redirect limits still apply.")]
    private static partial void LogNotEnforced(ILogger logger);

    private sealed class AddressRefusedException(string message) : IOException(message);
}
