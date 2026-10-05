using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

// How the space server reads DID documents: resolution failures become the space error contract's
// SpaceVerificationException, and each kind of token has its key picked out of the document.
//
// Every signature check in the space flow ends at a key published in a DID document: a delegation
// token's at the user's #atproto entry, and a space credential's at the authority's #atproto_space or
// #atproto entry, whichever its kid names.
internal static class SpaceDidResolution
{
    // The kid values a delegation token may carry. Proposal 0016 requires #atproto: the user's PDS signs
    // it with the account's own key.
    public static readonly string[] DelegationKeyIds = [DidDocument.SigningKeyId];

    // The kid values a space credential may carry: the authority's dedicated #atproto_space key, or its
    // #atproto key.
    public static readonly string[] CredentialKeyIds = [SpaceAuthority.SigningKeyId, DidDocument.SigningKeyId];

    // Resolves a DID, or refreshes it past any cached copy, reporting failure as a refusal.
    //
    // Throws SpaceVerificationException: The DID cannot be resolved.
    public static async Task<DidDocument> ResolveOrRefuseAsync(
        this IDidResolver resolver, Did did, bool refresh, CancellationToken cancellationToken)
    {
        try
        {
            return refresh
                ? await resolver.RefreshAsync(did, cancellationToken).ConfigureAwait(false)
                : await resolver.ResolveAsync(did, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A DID the caller named that does not resolve, whatever the reason, is a refusal
            // rather than a fault of this service.
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not resolve '{did}': {ex.Message}", ex);
        }
    }

    // Checks a space token's kid against the fragments its kind may name, returning the fragment with
    // its leading #.
    //
    // A missing kid is refused rather than read as "try the usual keys": the reference implementation
    // refuses it too, every conforming issuer (this SDK's SpaceTokens.Create included) sends one, and a
    // verifier that guesses which key a token meant can be steered onto a key the issuer never used for
    // it.
    //
    // The fragment is accepted with or without its #, as the reference accepts it, but never
    // DID-qualified: the key always belongs to the token's own issuer.
    //
    // keyId: The token's kid.
    //
    // allowed: The fragments accepted, each with its leading #.
    //
    // error: The XRPC error name to report a refusal under.
    //
    // Throws SpaceVerificationException: Thrown when the token names no kid, or one outside allowed.
    public static string RequireKeyId(string? keyId, IReadOnlyList<string> allowed, string error)
    {
        if (string.IsNullOrEmpty(keyId))
            throw new SpaceVerificationException(error, "The token names no \"kid\"; a space token must.");

        var fragment = keyId.StartsWith('#') ? keyId : "#" + keyId;
        foreach (var candidate in allowed)
            if (string.Equals(fragment, candidate, StringComparison.Ordinal))
                return candidate;

        throw new SpaceVerificationException(
            error, $"The token's \"kid\" must be {string.Join(" or ", allowed)}; got '{keyId}'.");
    }

    // Verifies a space token against the key its issuer publishes under one verification-method
    // fragment, with no fallback to any other, through DidResolverExtensions.VerifyWithRefreshAsync:
    // only a failure on the signature refetches the document.
    //
    // The token named its key, so that key is the only one it verifies against. The #atproto_space to
    // #atproto fallback of SpaceAuthority.GetSigningKey decides which key an authority signs with; a
    // credential signed with the #atproto key says so in its kid.
    //
    // issuer: The token issuer's DID.
    //
    // fragment: The fragment the token's kid named, from RequireKeyId.
    //
    // error: The XRPC error name to report a failed verification, or a missing or malformed key, under.
    //
    // verify: Verifies against a key, throwing SpaceTokenException on failure.
    //
    // Returns: The verified token, and the key and document it verified against.
    //
    // Throws SpaceVerificationException: Thrown when the DID does not resolve, publishes no such key or
    // one that is not usable, or the token does not verify. The document is fetched from a party this
    // service does not control, so unusable key material in it is a failed verification rather than a
    // fault here.
    public static async Task<(SpaceToken Token, string Key, DidDocument Document)> VerifyTokenAsync(
        this IDidResolver resolver,
        Did issuer,
        string fragment,
        string error,
        Func<string, SpaceToken> verify,
        CancellationToken cancellationToken)
    {
        SpaceToken? verified = null;
        SpaceTokenException? failure = null;
        DidDocument? document = null;
        (bool Verified, string? Key) result;
        try
        {
            result = await DidResolverExtensions.VerifyWithRefreshAsync(
                refresh => resolver.ResolveOrRefuseAsync(issuer, refresh, cancellationToken),
                resolved =>
                {
                    document = resolved;
                    return resolved.TryGetVerificationKey(fragment, out var key) switch
                    {
                        DidDocumentEntryStatus.Found => key,
                        DidDocumentEntryStatus.Absent => throw new SpaceVerificationException(
                            error, $"'{issuer}' publishes no '{fragment}' verification method to verify against."),
                        _ => throw new SpaceVerificationException(
                            error, $"'{issuer}' publishes a '{fragment}' verification method that is malformed or of an unsupported type."),
                    };
                },
                key =>
                {
                    try
                    {
                        verified = verify(key);
                        return true;
                    }
                    catch (SpaceTokenException ex) when (ex.IsSignatureFailure)
                    {
                        failure = ex;
                        return false;
                    }
                }).ConfigureAwait(false);
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(error, ex.Message, ex);
        }

        return result.Verified
            ? (verified!, result.Key!, document!)
            : throw new SpaceVerificationException(error, failure!.Message, failure);
    }
}
