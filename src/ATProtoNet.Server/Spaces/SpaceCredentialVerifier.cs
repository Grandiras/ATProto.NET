using ATProtoNet.Auth.OAuth;
using ATProtoNet.Caching;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.Spaces;

/// <summary>A space credential that verified, together with the proof that accompanied it.</summary>
/// <param name="Space">The space it grants read access to, from its <c>sub</c>.</param>
/// <param name="Proof">The DPoP proof presented with it, already verified against the request.</param>
public sealed record VerifiedSpaceCredential(SpaceUri Space, DPoPProof Proof);

/// <summary>Verifies the space credentials presented to a repo host.</summary>
/// <remarks>
/// <para>This is the repo host's side of the read path. A credential says the space authority
/// admitted this reader to this space; the repo host does not re-evaluate that decision, and has
/// no way to — it holds no member list and the protocol enumerates no readers. What it does
/// check is that the credential is genuine, current, addressed to this space, and presented by
/// the party it was issued to.</para>
/// <para>That last part is the DPoP binding, and it is what makes a credential safe to hand to
/// a host at all. A credential reads a whole space and is presented to every host in it, so
/// without the binding any one of those hosts could replay it against the rest.</para>
/// <para>A syncer presents the same credential on every request for two hours, so a credential
/// whose signature verified is remembered, keyed by its SHA-256 hash, and a later presentation
/// skips parsing and the signature check. Everything else is checked every time: expiry, the
/// space the request names, the DPoP proof with its single-use <c>jti</c>, and the authority's
/// key — re-read from the (cached) DID document and compared with the key the signature was
/// checked against. So a rotated authority key stops verifying a cached credential at exactly the
/// moment it stops verifying a new one; the cache never extends the DID cache's own window.</para>
/// <para>The cache holds 10,000 credentials (about 30 MB) and evicts the least recently used
/// entry when full. Any DID can be the authority of its own
/// spaces and mint credentials for them, so one authority may hold at most a quarter of the
/// entries: an authority presenting more displaces its own least recently used ones, and
/// credentials in steady use by other authorities stay cached.</para>
/// </remarks>
public sealed class SpaceCredentialVerifier
{
    private readonly IDidResolver _resolver;
    private readonly DPoPProofValidator _proofValidator;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    // ath (base64url SHA-256 of the credential) → entry; the lock keeps the per-authority counts
    // in step with it. Null when caching is off.
    private readonly object _lock = new();
    private readonly LruCache<string, CachedCredential>? _entries;
    private readonly Dictionary<Did, int> _perAuthority = new();
    private int _signatureChecks;

    /// <summary>Creates a verifier.</summary>
    /// <param name="resolver">
    /// Resolves the issuing authority's DID document; a <see cref="CachingDidResolver"/>, since
    /// every request resolves one. Resolved from the container under
    /// <see cref="SpaceServerExtensions.DidResolverKey"/>.
    /// </param>
    /// <param name="proofValidator">Verifies the accompanying DPoP proof.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceCredentialVerifier(
        [FromKeyedServices(SpaceServerExtensions.DidResolverKey)] IDidResolver resolver,
        DPoPProofValidator proofValidator,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(proofValidator);

        _resolver = resolver;
        _proofValidator = proofValidator;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (_options.VerifiedCredentialCacheCapacity > 0)
            _entries = new LruCache<string, CachedCredential>(_options.VerifiedCredentialCacheCapacity, StringComparer.Ordinal);
    }

    // The number of verified credentials currently remembered, for diagnostics and tests.
    internal int CachedCount => _entries?.Count ?? 0;

    // How many credential signatures have been checked, for tests.
    internal int SignatureChecks => Volatile.Read(ref _signatureChecks);

    /// <summary>Verifies a credential and the proof presented with it.</summary>
    /// <param name="credentialJwt">The credential, from the <c>Authorization: DPoP</c> header.</param>
    /// <param name="proofJwt">The proof, from the <c>DPoP</c> header.</param>
    /// <param name="httpMethod">The HTTP method as received.</param>
    /// <param name="requestUri">The request URL as received.</param>
    /// <param name="expectedSpace">
    /// The space the request names, which the credential must grant. Pass <see langword="null"/>
    /// to take the space from the credential instead.
    /// </param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedSpaceCredential> VerifyAsync(
        string credentialJwt,
        string proofJwt,
        string httpMethod,
        string requestUri,
        SpaceUri? expectedSpace = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialJwt))
            throw Invalid("The request carries no space credential.");

        // One hash serves as the cache key and as the `ath` the proof must carry.
        var accessTokenHash = DPoP.AccessTokenHash(credentialJwt);
        var now = _timeProvider.GetUtcNow();

        var credential = Lookup(accessTokenHash, now);
        if (credential is not null)
        {
            if (expectedSpace is not null && credential.Space != expectedSpace)
                throw WrongSpace(credential.Space, expectedSpace);

            if (!await IsKeyCurrentAsync(credential, cancellationToken).ConfigureAwait(false))
            {
                Forget(credential);
                credential = null;
            }
        }

        credential ??= await VerifyCredentialAsync(credentialJwt, accessTokenHash, expectedSpace, now, cancellationToken).ConfigureAwait(false);

        // Checked on every presentation, cached or not: the skew allowance is evaluated against
        // the request's own instant.
        if (credential.Token.IsExpired(now))
            throw Invalid("The space credential is expired.");

        var proof = await _proofValidator.ValidateWithHashAsync(
            proofJwt,
            httpMethod,
            requestUri,
            boundThumbprint: credential.Token.ConfirmationThumbprint,
            expectedAccessTokenHash: accessTokenHash,
            cancellationToken).ConfigureAwait(false);

        return new VerifiedSpaceCredential(credential.Space, proof);
    }

    private async Task<CachedCredential> VerifyCredentialAsync(
        string credentialJwt,
        string accessTokenHash,
        SpaceUri? expectedSpace,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        SpaceToken parsed;
        try
        {
            parsed = SpaceTokens.Parse(SpaceTokenType.Credential, credentialJwt);
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.NotAuthorized, ex.Message, ex);
        }

        if (!SpaceUri.TryParse(parsed.Subject, out var space))
            throw Invalid($"A space credential's subject must be a space URI; got '{parsed.Subject}'.");

        // Before any key is resolved, so a credential presented for the wrong space costs nothing.
        if (expectedSpace is not null && space != expectedSpace)
            throw WrongSpace(space, expectedSpace);

        // A space's credentials are minted by its own authority and by nobody else. Taking the
        // signer from the space URI rather than from the credential's iss is what stops an
        // authority minting credentials for a space it does not gate.
        if (!string.Equals(parsed.Issuer, space.Authority.Value, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The credential for {space} was issued by '{parsed.Issuer}', not by the space's authority.");
        }

        var keyId = SpaceDidResolution.RequireKeyId(
            parsed.KeyId, SpaceDidResolution.CredentialKeyIds, SpaceErrors.NotAuthorized);

        var (verified, key, document) = await _resolver.VerifyTokenAsync(
            space.Authority,
            keyId,
            SpaceErrors.NotAuthorized,
            authorityKey =>
            {
                Interlocked.Increment(ref _signatureChecks);
                return SpaceTokens.Verify(parsed, authorityKey, expectedAudience: null, expectedSubject: space, now);
            },
            cancellationToken).ConfigureAwait(false);

        var entry = new CachedCredential(accessTokenHash, verified, space, keyId, key, document);
        Remember(entry);
        return entry;
    }

    // Whether the authority still publishes the key a cached credential was verified against, as the DID
    // document a fresh verification would use right now says.
    private async Task<bool> IsKeyCurrentAsync(CachedCredential entry, CancellationToken cancellationToken)
    {
        var document = await _resolver.ResolveOrRefuseAsync(entry.Space.Authority, refresh: false, cancellationToken).ConfigureAwait(false);

        // A caching resolver hands back the same document until it refetches, so the common case
        // is a reference comparison.
        if (ReferenceEquals(document, entry.Document))
            return true;

        if (!string.Equals(document.GetVerificationKey(entry.KeyId), entry.DidKey, StringComparison.Ordinal))
            return false;

        entry.Document = document;
        return true;
    }

    private CachedCredential? Lookup(string accessTokenHash, DateTimeOffset now)
    {
        if (_entries is null)
            return null;

        lock (_lock)
        {
            if (!_entries.TryGetValue(accessTokenHash, out var entry))
                return null;

            if (entry.Token.IsExpired(now))
            {
                _entries.Remove(accessTokenHash);
                Removed(entry);
                return null;
            }

            return entry;
        }
    }

    private void Remember(CachedCredential entry)
    {
        if (_entries is null)
            return;

        var quota = Math.Max(1, _entries.Capacity / 4);
        var authority = entry.Space.Authority;

        lock (_lock)
        {
            if (_entries.Remove(entry.Key, out var existing))
                Removed(existing);

            // One authority displaces only its own entries past its share; everyone else's
            // steadily used credentials stay put.
            if (_perAuthority.GetValueOrDefault(authority) >= quota &&
                _entries.RemoveLeastRecent(cached => cached.Space.Authority == authority) is { } own)
            {
                Removed(own.Value);
            }

            if (_entries.Set(entry.Key, entry) is { } evicted)
                Removed(evicted.Value);
            _perAuthority[authority] = _perAuthority.GetValueOrDefault(authority) + 1;
        }
    }

    private void Forget(CachedCredential entry)
    {
        lock (_lock)
        {
            if (_entries!.Remove(entry.Key, entry))
                Removed(entry);
        }
    }

    // Counts a credential out of its authority's share. Call under the lock.
    private void Removed(CachedCredential entry)
    {
        var authority = entry.Space.Authority;
        var count = _perAuthority.GetValueOrDefault(authority) - 1;
        if (count > 0)
            _perAuthority[authority] = count;
        else
            _perAuthority.Remove(authority);
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);

    private static SpaceVerificationException WrongSpace(SpaceUri granted, SpaceUri requested) =>
        Invalid($"The credential grants {granted}, not the requested {requested}.");

    // A verified credential, and the key and document it verified against.
    private sealed class CachedCredential(
        string key, SpaceToken token, SpaceUri space, string keyId, string didKey, DidDocument document)
    {
        public string Key { get; } = key;

        public SpaceToken Token { get; } = token;

        public SpaceUri Space { get; } = space;

        // The verification-method fragment the credential's kid named.
        public string KeyId { get; } = keyId;

        public string DidKey { get; } = didKey;

        // The newest document seen to still publish DidKey.
        public DidDocument Document
        {
            get => Volatile.Read(ref _document);
            set => Volatile.Write(ref _document, value);
        }

        private DidDocument _document = document;
    }
}
