using System.Net.Http.Json;

namespace ATProtoNet.Pds;

/// <summary>
/// The outcome of a single <c>com.atproto.sync.requestCrawl</c> call.
/// </summary>
/// <param name="Relay">The relay that was contacted.</param>
/// <param name="Success">Whether the relay accepted the request.</param>
/// <param name="Error">The failure detail, when <paramref name="Success"/> is <c>false</c>.</param>
public sealed record PdsCrawlResult(string Relay, bool Success, string? Error);

/// <summary>
/// Asks relays to crawl this PDS by calling <c>com.atproto.sync.requestCrawl</c> on each host in
/// <see cref="PdsOptions.RelayHosts"/>.
/// <para>
/// A relay only discovers a new PDS when told about it, so this is what turns a correctly
/// implemented sync surface into an actually federated one. Call it after the host starts
/// listening, and again after creating an account on a PDS the network has not seen before.
/// </para>
/// </summary>
public sealed class PdsCrawlNotifier : IDisposable
{
    private readonly PdsOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>Creates a crawl notifier.</summary>
    /// <param name="options">PDS configuration, supplying the relay list and this PDS's hostname.</param>
    /// <param name="httpClient">
    /// An optional client to use. When <c>null</c>, one with a 30-second timeout is created and
    /// owned by this instance.
    /// </param>
    public PdsCrawlNotifier(PdsOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Requests a crawl from every configured relay.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One result per relay. Failures are reported rather than thrown: one unreachable relay
    /// must not stop the others from being notified, and a host that calls this at startup
    /// should not fail to start because a relay is down.
    /// </returns>
    public async Task<IReadOnlyList<PdsCrawlResult>> RequestCrawlAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<PdsCrawlResult>(_options.RelayHosts.Count);

        foreach (var relay in _options.RelayHosts)
        {
            results.Add(await RequestCrawlAsync(relay, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Requests a crawl from a single relay.
    /// </summary>
    /// <param name="relay">The relay hostname or base URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PdsCrawlResult> RequestCrawlAsync(
        string relay, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relay);

        var url = NormalizeRelayUrl(relay) + "/xrpc/com.atproto.sync.requestCrawl";

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                url, new { hostname = _options.Hostname }, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return new PdsCrawlResult(relay, true, null);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new PdsCrawlResult(relay, false, $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PdsCrawlResult(relay, false, ex.Message);
        }
    }

    /// <summary>
    /// Accepts either a bare hostname or a full URL, defaulting to HTTPS.
    /// </summary>
    internal static string NormalizeRelayUrl(string relay)
    {
        var trimmed = relay.Trim().TrimEnd('/');
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
