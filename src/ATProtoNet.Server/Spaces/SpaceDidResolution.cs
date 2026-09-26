using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// How the space server reads DID documents: resolution failures become the space error
/// contract's <see cref="SpaceVerificationException"/>, and each kind of token has its key picked
/// out of the document.
/// </summary>
/// <remarks>
/// Every signature check in the space flow ends at a key published in a DID document: a
/// delegation token's at the user's <c>#atproto</c> entry, a space credential's at the
/// authority's <c>#atproto_space</c> entry (or its <c>#atproto</c> entry when it publishes none),
/// and a write notification's delivery endpoint at the subscriber's service entry.
/// </remarks>
internal static class SpaceDidResolution
{
    /// <summary>
    /// Resolves a DID, or refreshes it past any cached copy, reporting failure as a refusal.
    /// </summary>
    /// <exception cref="SpaceVerificationException">The DID cannot be resolved.</exception>
    public static async Task<DidDocument> ResolveOrRefuseAsync(
        this IDidResolver resolver, Did did, bool refresh, CancellationToken cancellationToken)
    {
        try
        {
            return refresh
                ? await resolver.RefreshAsync(did, cancellationToken)
                : await resolver.ResolveAsync(did, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A DID the caller named that does not resolve, whatever the reason, is a refusal
            // rather than a fault of this service.
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not resolve '{did}': {ex.Message}", ex);
        }
    }

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
    /// <param name="error">The XRPC error name to report a missing or malformed key under.</param>
    /// <param name="refresh">Whether to bypass a cached document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the DID does not resolve, publishes no such key, or publishes one whose key
    /// material is malformed. The document is fetched from a party this service does not control,
    /// so unusable key material in it is a failed verification rather than a fault here.
    /// </exception>
    public static async Task<string> ResolveAccountKeyAsync(
        this IDidResolver resolver, Did did, string? keyId, string error, bool refresh, CancellationToken cancellationToken)
    {
        var document = await resolver.ResolveOrRefuseAsync(did, refresh, cancellationToken);
        var fragment = NormalizeFragment(keyId) ?? DidDocument.SigningKeyId;

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
    /// <param name="refresh">Whether to bypass a cached document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The fallback is what makes an ordinary account a usable space authority for personal
    /// data with no DID-document change at all.
    /// </remarks>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the authority does not resolve, publishes no usable key, or publishes one whose
    /// key material is malformed.
    /// </exception>
    public static async Task<string> ResolveAuthorityKeyAsync(
        this IDidResolver resolver, Did authorityDid, string? keyId, bool refresh, CancellationToken cancellationToken)
    {
        var document = await resolver.ResolveOrRefuseAsync(authorityDid, refresh, cancellationToken);
        var fragment = NormalizeFragment(keyId);

        var key = fragment is null
            ? FindAuthorityKey(document, authorityDid)
            : FindKey(document, fragment, authorityDid, SpaceErrors.NotAuthorized);

        return key ?? throw new SpaceVerificationException(
            SpaceErrors.NotAuthorized,
            $"Space authority '{authorityDid}' publishes no {fragment ?? SpaceAuthority.SigningKeyId} " +
            "verification method to verify its credentials against.");
    }

    /// <summary>
    /// Verifies a token against a key resolved from a DID document, and once more against a
    /// refreshed document when it fails on its signature: a cached document may predate a key
    /// rotation. The resolver rate-limits refreshes, so forged tokens cannot turn into a
    /// directory request each.
    /// </summary>
    /// <param name="resolveKey">Resolves the key, bypassing the cache when passed <see langword="true"/>.</param>
    /// <param name="verify">Verifies against a key, throwing <see cref="SpaceTokenException"/> on failure.</param>
    /// <param name="error">The XRPC error name to report a failed verification under.</param>
    public static async Task<SpaceToken> VerifyWithKeyRefreshAsync(
        Func<bool, Task<string>> resolveKey, Func<string, SpaceToken> verify, string error)
    {
        var key = await resolveKey(false);
        try
        {
            return verify(key);
        }
        catch (SpaceTokenException ex) when (ex.IsSignatureFailure)
        {
            var refreshed = await resolveKey(true);
            if (string.Equals(refreshed, key, StringComparison.Ordinal))
                throw new SpaceVerificationException(error, ex.Message, ex);

            try
            {
                return verify(refreshed);
            }
            catch (SpaceTokenException retry)
            {
                throw new SpaceVerificationException(error, retry.Message, retry);
            }
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(error, ex.Message, ex);
        }
    }

    private static string? NormalizeFragment(string? keyId)
    {
        if (string.IsNullOrEmpty(keyId))
            return null;

        var hash = keyId.IndexOf('#');
        return hash < 0 ? "#" + keyId : keyId[hash..];
    }

    private static string? FindKey(DidDocument document, string fragment, Did did, string error)
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

    private static string? FindAuthorityKey(DidDocument document, Did authorityDid)
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
