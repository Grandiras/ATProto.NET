using System.Collections.Concurrent;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Resolves the DID documents a space server verifies signatures against.
/// </summary>
/// <remarks>
/// Every signature check in the space flow ends at a key published in a DID document: a
/// delegation token's at the user's <c>#atproto</c> entry, a space credential's at the
/// authority's <c>#atproto_space</c> entry (or its <c>#atproto</c> entry when it publishes
/// none), and a write notification's delivery endpoint at the subscriber's service entry. This
/// is the seam where that resolution is replaced, whether by a cache, a directory mirror, or a
/// fixture in a test.
/// </remarks>
public interface ISpaceDidDocumentResolver
{
    /// <summary>
    /// Resolves a DID to its document.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when the DID cannot be resolved.</exception>
    Task<DidDocument> ResolveAsync(string did, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISpaceDidDocumentResolver"/>: <c>did:plc</c> and <c>did:web</c>
/// resolution with a short-lived in-process cache.
/// </summary>
/// <remarks>
/// Resolution is on the critical path of every authenticated space request, and a DID document
/// changes rarely, so documents are held for
/// <see cref="SpaceServerOptions.DidDocumentCacheLifetime"/>. That is also the window in which a
/// rotated key keeps verifying: keep it short, and note that a rotation is announced on the
/// public firehose as an <c>#identity</c> event, which a service can use to evict eagerly.
/// </remarks>
public sealed class CachingSpaceDidDocumentResolver : ISpaceDidDocumentResolver, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly DidResolver _resolver;
    private readonly bool _ownsResolver;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a resolver.
    /// </summary>
    /// <param name="options">Server options; supplies the cache lifetime.</param>
    /// <param name="resolver">The underlying DID resolver. One is created and owned when omitted.</param>
    /// <param name="timeProvider">The clock used for cache expiry. Defaults to the system clock.</param>
    public CachingSpaceDidDocumentResolver(
        SpaceServerOptions? options = null,
        DidResolver? resolver = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? new SpaceServerOptions();
        _ownsResolver = resolver is null;
        _resolver = resolver ?? new DidResolver();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<DidDocument> ResolveAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var now = _timeProvider.GetUtcNow();
        if (_cache.TryGetValue(did, out var cached) && cached.ExpiresAt > now)
            return cached.Document;

        DidDocument document;
        try
        {
            document = await _resolver.ResolveDidAsync(did, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or DidWebException or ArgumentException or InvalidOperationException)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not resolve '{did}': {ex.Message}", ex);
        }

        _cache[did] = new CacheEntry(document, now.Add(_options.DidDocumentCacheLifetime));
        return document;
    }

    /// <summary>Drops any cached document for a DID, e.g. on an <c>#identity</c> firehose event.</summary>
    /// <param name="did">The DID whose document should be re-fetched next time.</param>
    public void Invalidate(string did) => _cache.TryRemove(did, out _);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsResolver)
            _resolver.Dispose();
    }

    private readonly record struct CacheEntry(DidDocument Document, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Extension helpers that pick the right key out of a DID document for each kind of space token.
/// </summary>
public static class SpaceDidDocumentResolverExtensions
{
    /// <summary>
    /// Resolves the <c>did:key</c> an account's own signatures verify against: the
    /// <c>#atproto</c> verification method, or another fragment when a token names one.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="did">The account DID.</param>
    /// <param name="keyId">
    /// The token's <c>kid</c>. Defaults to <c>#atproto</c>, which is what
    /// <see cref="SpaceTokens.Create"/> emits.
    /// </param>
    /// <param name="error">The XRPC error name to report a failure under.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the DID publishes no such key, or publishes one whose key material is
    /// malformed. The document is fetched from a party this service does not control, so
    /// unusable key material in it is a failed verification rather than a fault here.
    /// </exception>
    public static async Task<string> ResolveAccountKeyAsync(
        this ISpaceDidDocumentResolver resolver,
        string did,
        string? keyId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var document = await resolver.ResolveAsync(did, cancellationToken);
        var fragment = NormalizeFragment(keyId) ?? "#atproto";

        return FindKey(document, fragment, did, error)
            ?? throw new SpaceVerificationException(
                error, $"'{did}' publishes no '{fragment}' verification method to verify against.");
    }

    /// <summary>
    /// Resolves the <c>did:key</c> a space authority's credentials verify against: its
    /// <c>#atproto_space</c> verification method, falling back to <c>#atproto</c>.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="authorityDid">The space authority DID.</param>
    /// <param name="keyId">The credential's <c>kid</c>, when it names a specific key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The fallback is what makes an ordinary account a usable space authority for personal
    /// data with no DID-document change at all.
    /// </remarks>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the authority publishes no usable key, or publishes one whose key material is
    /// malformed.
    /// </exception>
    public static async Task<string> ResolveAuthorityKeyAsync(
        this ISpaceDidDocumentResolver resolver,
        string authorityDid,
        string? keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var document = await resolver.ResolveAsync(authorityDid, cancellationToken);
        var fragment = NormalizeFragment(keyId);

        var key = fragment is null
            ? FindAuthorityKey(document, authorityDid)
            : FindKey(document, fragment, authorityDid, SpaceErrors.NotAuthorized);

        return key ?? throw new SpaceVerificationException(
            SpaceErrors.NotAuthorized,
            $"Space authority '{authorityDid}' publishes no {fragment ?? SpaceAuthority.SigningKeyId} " +
            "verification method to verify its credentials against.");
    }

    private static string? NormalizeFragment(string? keyId)
    {
        if (string.IsNullOrEmpty(keyId))
            return null;

        var hash = keyId.IndexOf('#');
        return hash < 0 ? "#" + keyId : keyId[hash..];
    }

    private static string? FindKey(DidDocument document, string fragment, string did, string error)
    {
        try
        {
            return document.GetVerificationKey(fragment);
        }
        catch (FormatException ex)
        {
            throw new SpaceVerificationException(
                error,
                $"'{did}' publishes a '{fragment}' verification method whose key material is " +
                $"malformed: {ex.Message}",
                ex);
        }
    }

    private static string? FindAuthorityKey(DidDocument document, string authorityDid)
    {
        try
        {
            return SpaceAuthority.GetSigningKey(document);
        }
        catch (FormatException ex)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"Space authority '{authorityDid}' publishes a verification method whose key " +
                $"material is malformed: {ex.Message}",
                ex);
        }
    }
}
