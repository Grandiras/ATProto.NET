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
/// delegation token's at the user's <c>#atproto</c> entry, and a space credential's at the
/// authority's <c>#atproto_space</c> or <c>#atproto</c> entry, whichever its <c>kid</c> names.
/// </remarks>
internal static class SpaceDidResolution
{
    /// <summary>
    /// The <c>kid</c> values a delegation token may carry. Proposal 0016 requires
    /// <c>#atproto</c>: the user's PDS signs it with the account's own key.
    /// </summary>
    public static readonly string[] DelegationKeyIds = [DidDocument.SigningKeyId];

    /// <summary>
    /// The <c>kid</c> values a space credential may carry: the authority's dedicated
    /// <c>#atproto_space</c> key, or its <c>#atproto</c> key.
    /// </summary>
    public static readonly string[] CredentialKeyIds = [SpaceAuthority.SigningKeyId, DidDocument.SigningKeyId];

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
    /// Checks a space token's <c>kid</c> against the fragments its kind may name, returning the
    /// fragment with its leading <c>#</c>.
    /// </summary>
    /// <param name="keyId">The token's <c>kid</c>.</param>
    /// <param name="allowed">The fragments accepted, each with its leading <c>#</c>.</param>
    /// <param name="error">The XRPC error name to report a refusal under.</param>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the token names no <c>kid</c>, or one outside <paramref name="allowed"/>.
    /// </exception>
    /// <remarks>
    /// <para>A missing <c>kid</c> is refused rather than read as "try the usual keys": the
    /// reference implementation refuses it too, every conforming issuer (this SDK's
    /// <see cref="SpaceTokens.Create"/> included) sends one, and a verifier that guesses which key
    /// a token meant can be steered onto a key the issuer never used for it.</para>
    /// <para>The fragment is accepted with or without its <c>#</c>, as the reference accepts it,
    /// but never DID-qualified: the key always belongs to the token's own issuer.</para>
    /// </remarks>
    public static string RequireKeyId(string? keyId, IReadOnlyList<string> allowed, string error)
    {
        if (string.IsNullOrEmpty(keyId))
            throw new SpaceVerificationException(error, "The token names no \"kid\"; a space token must.");

        var fragment = keyId.StartsWith('#') ? keyId : "#" + keyId;
        foreach (var candidate in allowed)
        {
            if (string.Equals(fragment, candidate, StringComparison.Ordinal))
                return candidate;
        }

        throw new SpaceVerificationException(
            error, $"The token's \"kid\" must be {string.Join(" or ", allowed)}; got '{keyId}'.");
    }

    /// <summary>
    /// Resolves the <c>did:key</c> a DID publishes under one verification-method fragment, with
    /// no fallback to any other.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="did">The token issuer's DID.</param>
    /// <param name="fragment">The fragment the token's <c>kid</c> named, from <see cref="RequireKeyId"/>.</param>
    /// <param name="error">The XRPC error name to report a missing or malformed key under.</param>
    /// <param name="refresh">Whether to bypass a cached document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when the DID does not resolve, publishes no such key, or publishes one that is not
    /// usable. The document is fetched from a party this service does not control, so unusable
    /// key material in it is a failed verification rather than a fault here.
    /// </exception>
    /// <remarks>
    /// The token named its key, so that key is the only one it verifies against. The
    /// <c>#atproto_space</c> to <c>#atproto</c> fallback of <see cref="SpaceAuthority.GetSigningKey"/>
    /// decides which key an authority <em>signs</em> with; a credential signed with the
    /// <c>#atproto</c> key says so in its <c>kid</c>.
    /// </remarks>
    public static async Task<string> ResolveKeyAsync(
        this IDidResolver resolver, Did did, string fragment, string error, bool refresh, CancellationToken cancellationToken) =>
        (await resolver.ResolveKeyWithDocumentAsync(did, fragment, error, refresh, cancellationToken)).Key;

    /// <summary>
    /// <see cref="ResolveKeyAsync"/>, also returning the document the key was read from.
    /// </summary>
    /// <exception cref="SpaceVerificationException">As <see cref="ResolveKeyAsync"/>.</exception>
    public static async Task<(string Key, DidDocument Document)> ResolveKeyWithDocumentAsync(
        this IDidResolver resolver, Did did, string fragment, string error, bool refresh, CancellationToken cancellationToken)
    {
        var document = await resolver.ResolveOrRefuseAsync(did, refresh, cancellationToken);

        return document.TryGetVerificationKey(fragment, out var key) switch
        {
            DidDocumentEntryStatus.Found => (key!, document),
            DidDocumentEntryStatus.Absent => throw new SpaceVerificationException(
                error, $"'{did}' publishes no '{fragment}' verification method to verify against."),
            _ => throw new SpaceVerificationException(
                error, $"'{did}' publishes a '{fragment}' verification method that is malformed or of an unsupported type."),
        };
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
}
