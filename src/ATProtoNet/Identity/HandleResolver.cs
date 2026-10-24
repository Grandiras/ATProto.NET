using System.Text;
using ATProtoNet.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>Options of <see cref="HandleResolver"/>.</summary>
public sealed record HandleResolverOptions
{
    /// <summary>The budget both lookups share.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long an answer is remembered; <see cref="TimeSpan.Zero"/> remembers none. "No answer" is never remembered.</summary>
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The most answers remembered.</summary>
    public int CacheCapacity { get; init; } = 10_000;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Budget, TimeSpan.Zero, nameof(Budget));
        ArgumentOutOfRangeException.ThrowIfLessThan(CacheTtl, TimeSpan.Zero, nameof(CacheTtl));
        ArgumentOutOfRangeException.ThrowIfLessThan(CacheCapacity, 1, nameof(CacheCapacity));
    }
}

/// <summary>Resolves handles through their own authorities: the <c>_atproto.{handle}</c> TXT record and <c>https://{handle}/.well-known/atproto-did</c>.</summary>
/// <remarks>
/// <para>Both lookups start at once under one budget. DNS wins, as the handle spec prefers: its answer is returned as soon as it
/// arrives, and the HTTPS answer is used only when DNS has none. Two distinct DIDs in DNS fail closed. Every other failure of a
/// lookup is "no answer".</para>
/// <para>Handles in TLDs that never resolve (<c>alt arpa example internal invalid local localhost onion</c>) are not looked up;
/// <c>.test</c> only under <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/>. See <see href="https://atproto.com/specs/handle"/>.</para>
/// </remarks>
public sealed partial class HandleResolver : IHandleResolver
{
    /// <summary>The longest well-known body read, in bytes.</summary>
    public const int MaxWellKnownBytes = 2048;

    /// <summary>The most same-host <c>https</c> redirects a well-known may take.</summary>
    public const int MaxWellKnownRedirects = 3;

    private static readonly string[] ReservedTlds = ["alt", "arpa", "example", "internal", "invalid", "local", "localhost", "onion"];

    private static readonly UntrustedRequest WellKnown = new() { MaxBodyBytes = MaxWellKnownBytes, Accept = "text/plain", MaxSameHostRedirects = MaxWellKnownRedirects };

    private readonly UntrustedHttp _http;
    private readonly IDnsTxtResolver? _dns;
    private readonly HandleResolverOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly LruCache<Handle, (Task<Did?> Did, DateTimeOffset Expires)>? _answers;

    /// <summary>Creates a resolver.</summary>
    /// <param name="http">The fetch layer, which the caller owns.</param>
    /// <param name="dns">The TXT resolver, or <see langword="null"/> to resolve over HTTPS only.</param>
    /// <param name="options">Options. Defaults apply when omitted.</param>
    /// <param name="time">The clock of the budget and the cache. Defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    public HandleResolver(UntrustedHttp http, IDnsTxtResolver? dns, HandleResolverOptions? options = null, TimeProvider? time = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _options = options ?? new();
        _options.Validate();
        (_http, _dns, _time, _logger) = (http, dns, time ?? TimeProvider.System, logger ?? NullLogger.Instance);
        if (_options.CacheTtl > TimeSpan.Zero) _answers = new(_options.CacheCapacity);
    }

    /// <inheritdoc />
    public Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!IsResolvable(handle, _http.Policy.AllowPrivateNetworks))
        {
            LogNotResolvable(_logger, handle);
            return Task.FromResult<Did?>(null);
        }
        return _answers is not null && _answers.TryGetValue(handle, out var cached) && _time.GetUtcNow() < cached.Expires ? cached.Did : LookUpAsync(handle, cancellationToken);
    }

    private async Task<Did?> LookUpAsync(Handle handle, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.Budget, _time);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var https = ViaHttpsAsync(handle, budget.Token, cancellationToken);
        Did? answer;
        try
        {
            answer = await ViaDnsAsync(handle, budget.Token, cancellationToken).ConfigureAwait(false) ?? await https.ConfigureAwait(false);
        }
        finally
        {
            // Stops an HTTPS lookup the DNS answer made moot.
            await budget.CancelAsync().ConfigureAwait(false);
            await ((Task)https).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        if (answer is not null) _answers?.Set(handle, (Task.FromResult<Did?>(answer), _time.GetUtcNow() + _options.CacheTtl));
        return answer;
    }

    /// <inheritdoc />
    public ValueTask InvalidateAsync(Handle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _answers?.Remove(handle);
        return ValueTask.CompletedTask;
    }

    // The reserved TLDs never resolve, .test only under the development opt-out; did:web hosts follow the same list.
    internal static bool IsResolvable(Handle handle, bool allowPrivateNetworks)
    {
        var tld = handle.Value[(handle.Value.LastIndexOf('.') + 1)..];
        return !handle.IsInvalid && Array.IndexOf(ReservedTlds, tld) < 0 && (allowPrivateNetworks || tld != "test");
    }

    private async Task<Did?> ViaDnsAsync(Handle handle, CancellationToken attempt, CancellationToken caller)
    {
        var name = "_atproto." + handle.Value;
        if (_dns is null || name.Length > Handle.MaxLength) return null;
        IReadOnlyList<string>? records;
        try
        {
            records = await _dns.QueryAsync(name, attempt).ConfigureAwait(false);
        }
        catch (Exception ex) when (!caller.IsCancellationRequested)
        {
            LogNoAnswer(_logger, ex, "DNS", handle);
            return null;
        }
        Did? found = null;
        foreach (var record in records ?? [])
        {
            if (!record.StartsWith("did=", StringComparison.Ordinal) || !Did.TryParse(record.AsSpan(4), out var did)) continue;
            if (found is not null && found != did)
                throw new IdentityResolutionException(IdentityResolutionError.HandleConflict, $"'{handle}' publishes more than one DID in DNS ('{found}' and '{did}').", handle);
            found = did;
        }
        return found;
    }

    private async Task<Did?> ViaHttpsAsync(Handle handle, CancellationToken attempt, CancellationToken caller)
    {
        try
        {
            var response = await _http.GetAsync(new Uri($"https://{handle.Value}/.well-known/atproto-did"), WellKnown, attempt).ConfigureAwait(false);
            return response.IsSuccess && Did.TryParse(Encoding.UTF8.GetString(response.Body.Span).Trim(), out var did) ? did : null;
        }
        catch (Exception ex) when (!caller.IsCancellationRequested)
        {
            LogNoAnswer(_logger, ex, "HTTPS", handle);
            return null;
        }
    }

    [LoggerMessage(1010, LogLevel.Debug, "Not resolving {Handle}: its TLD never resolves.")]
    private static partial void LogNotResolvable(ILogger logger, Handle handle);

    [LoggerMessage(1011, LogLevel.Debug, "{Method} resolution of {Handle} gave no answer.")]
    private static partial void LogNoAnswer(ILogger logger, Exception exception, string method, Handle handle);
}
