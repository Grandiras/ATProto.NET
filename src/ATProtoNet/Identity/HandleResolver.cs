using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>
/// Resolves handles through their own authorities: the <c>_atproto.&lt;handle&gt;</c> DNS TXT
/// record, queried over DNS-over-HTTPS, and <c>https://&lt;handle&gt;/.well-known/atproto-did</c>.
/// </summary>
/// <remarks>
/// <para>Both lookups run concurrently under one
/// <see cref="IdentityResolverOptions.HandleResolutionTimeout"/> budget, so a handle domain that
/// never answers turns into "no answer" rather than holding the caller.</para>
/// <para>The two authorities are different trust roots — DNS and a TLS certificate — so when both
/// answer they must agree. A first-answer-wins policy would let an attacker who controls either
/// one (a hijacked web host for a domain whose DNS is intact) complete resolution alone, so a
/// disagreement fails closed with <see cref="DidResolutionErrorKind.HandleConflict"/>. When only
/// one answers, its answer is used.</para>
/// <para>Handles under TLDs that never resolve (<c>.local</c>, <c>.localhost</c>, <c>.internal</c>,
/// <c>.arpa</c>, <c>.onion</c>, <c>.alt</c>, <c>.example</c>, <c>.invalid</c>) are not looked up;
/// <c>.test</c> is looked up only under the development opt-out.</para>
/// </remarks>
public sealed class HandleResolver : IHandleResolver, IDisposable
{
    // A DID is at most a couple of kilobytes; the target is derived from untrusted input, so the
    // body is capped rather than buffered in full.
    private const int MaxWellKnownBytes = 2048;

    // Same-host redirects (http→https, a trailing slash) a well-known may take before answering.
    private const int MaxWellKnownRedirects = 3;

    private static readonly HashSet<string> ReservedTlds = new(StringComparer.Ordinal)
    {
        "alt", "arpa", "example", "internal", "invalid", "local", "localhost", "onion",
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IdentityResolverOptions _options;
    private readonly Uri? _dnsOverHttpsUrl;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a resolver with its own client under the SDK's identity fetch policy.
    /// </summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public HandleResolver(IdentityResolverOptions? options = null, ILogger? logger = null)
        : this(null, options, logger, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a resolver that sends its requests through <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to use, which the caller owns. Its handler is used as is: the connection-level
    /// address check applies only to the SDK's own handler.
    /// </param>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public HandleResolver(HttpClient httpClient, IdentityResolverOptions? options = null, ILogger? logger = null)
        : this(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), options, logger, ownsHttpClient: false)
    {
    }

    private HandleResolver(HttpClient? httpClient, IdentityResolverOptions? options, ILogger? logger, bool ownsHttpClient)
    {
        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        _dnsOverHttpsUrl = _options.DnsOverHttpsUrl is { } doh
            ? IdentityNetworkPolicy.ValidateServiceUrl(doh, _options.AllowPrivateNetworks, nameof(options))
            : null;
        _ownsHttpClient = ownsHttpClient;
        _httpClient = httpClient ?? IdentityNetworkPolicy.CreateClient(_options.AllowPrivateNetworks);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!IsResolvable(handle, _options.AllowPrivateNetworks))
        {
            _logger.LogDebug("Not resolving {Handle}: its TLD never resolves.", handle);
            return null;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.HandleResolutionTimeout);

        var httpsTask = ResolveViaHttpsAsync(handle, budget.Token, cancellationToken);
        var dnsTask = _dnsOverHttpsUrl is null
            ? Task.FromResult<Did?>(null)
            : ResolveViaDnsAsync(handle, _dnsOverHttpsUrl, budget.Token, cancellationToken);

        try
        {
            await Task.WhenAll(httpsTask, dnsTask).ConfigureAwait(false);
        }
        catch
        {
            // Observed below: caller cancellation and a DNS conflict propagate, and whichever of
            // the two failed first must not leave the other's outcome unobserved.
        }

        var httpsDid = await httpsTask.ConfigureAwait(false);
        var dnsDid = await dnsTask.ConfigureAwait(false);

        if (httpsDid is not null && dnsDid is not null && httpsDid != dnsDid)
        {
            _logger.LogWarning(
                "Handle {Handle} resolves to conflicting DIDs (HTTPS {HttpsDid}, DNS {DnsDid}); failing closed.",
                handle, httpsDid, dnsDid);
            throw new DidResolutionException(
                $"Handle '{handle}' resolution conflict: HTTPS reports '{httpsDid}', DNS reports '{dnsDid}'.",
                DidResolutionErrorKind.HandleConflict);
        }

        return httpsDid ?? dnsDid;
    }

    /// <summary>
    /// Whether a handle's TLD is one that resolves: the reserved ones never do, and <c>.test</c>
    /// only under the development opt-out.
    /// </summary>
    internal static bool IsResolvable(Handle handle, bool allowPrivateNetworks)
    {
        var tld = handle.Value[(handle.Value.LastIndexOf('.') + 1)..];
        return !ReservedTlds.Contains(tld) && (allowPrivateNetworks || tld != "test");
    }

    /// <summary>
    /// Resolves via <c>/.well-known/atproto-did</c>. Every failure, including the budget running
    /// out and a host that misbehaves in any way, is "no answer"; only the caller cancelling
    /// propagates.
    /// </summary>
    private async Task<Did?> ResolveViaHttpsAsync(Handle handle, CancellationToken attemptToken, CancellationToken callerToken)
    {
        // Built up front so the host is compared in the same (punycode) form the response reports.
        var origin = new Uri($"https://{handle.Value}/.well-known/atproto-did");

        try
        {
            var url = origin;
            for (var hop = 0; ; hop++)
            {
                var result = await IdentityFetch.GetAsync(
                    _httpClient, url, "text/plain", MaxWellKnownBytes, Timeout.InfiniteTimeSpan, did: null, attemptToken)
                    .ConfigureAwait(false);

                // A DID handed back by some other host is not the handle domain's answer, so only
                // same-host HTTPS redirects are followed. The SDK's handler follows none itself; a
                // caller's client that did is checked by the host it ended up at.
                if (result.IsRedirect && result.Location is { } next && hop < MaxWellKnownRedirects)
                {
                    if (next.Scheme != Uri.UriSchemeHttps || !next.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Ignoring atproto-did for {Handle}: redirected to {Location}.", handle, next);
                        return null;
                    }

                    url = next;
                    continue;
                }

                if (!result.IsSuccess)
                    return null;

                var finalHost = result.FinalUri?.Host;
                if (finalHost is not null && !finalHost.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Ignoring atproto-did for {Handle}: redirected to {Host}.", handle, finalHost);
                    return null;
                }

                var text = Encoding.UTF8.GetString(result.Body.Span).Trim();
                return Did.TryParse(text, out var did) ? did : null;
            }
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTTPS resolution of {Handle} produced no answer.", handle);
            return null;
        }
    }

    /// <summary>
    /// Resolves via the <c>_atproto</c> TXT record. Every failure is "no answer", except several
    /// distinct <c>did=</c> values, which the spec treats as a failed resolution rather than a
    /// choice to make.
    /// </summary>
    private async Task<Did?> ResolveViaDnsAsync(
        Handle handle, Uri endpoint, CancellationToken attemptToken, CancellationToken callerToken)
    {
        IReadOnlyList<string>? records;
        try
        {
            records = await DnsTxtLookup.QueryAsync(
                _httpClient, endpoint, $"_atproto.{handle.Value}", Timeout.InfiniteTimeSpan, attemptToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS resolution of {Handle} produced no answer.", handle);
            return null;
        }

        Did? found = null;
        foreach (var text in records ?? [])
        {
            if (!text.StartsWith("did=", StringComparison.Ordinal))
                continue;

            if (!Did.TryParse(text["did=".Length..], out var did))
                continue;

            if (found is not null && found != did)
            {
                throw new DidResolutionException(
                    $"Handle '{handle}' publishes more than one DID in DNS ('{found}' and '{did}').",
                    DidResolutionErrorKind.HandleConflict);
            }

            found = did;
        }

        return found;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
