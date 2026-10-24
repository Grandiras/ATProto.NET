using ATProtoNet.Crypto;

namespace ATProtoNet.Identity;

/// <summary>Verification against a key a DID document publishes.</summary>
public static class DidResolverExtensions
{
    /// <summary>Verifies against the key <paramref name="selectKey"/> reads from the DID's document and, when there is none or verification fails, once more after one refresh, if the refresh changed the key.</summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="did">The signer's DID.</param>
    /// <param name="selectKey">Reads the key from a document, never falling back to another entry: <see langword="null"/> when there is none, which a refresh may cure; an exception refuses the document without a refresh.</param>
    /// <param name="verify">Whether the signature verifies against a key.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>Whether it verified, and the key tried last.</returns>
    /// <exception cref="IdentityResolutionException">The DID did not resolve.</exception>
    /// <remarks>
    /// A cached document may predate a key rotation, so a signature is not declared bad before one refetch, as the sync and label
    /// specs ask. The refetch is <see cref="IDidResolver.RefreshAsync"/>, which a caching resolver rate-limits per DID: forged
    /// signatures naming any DID must not each cost a directory request.
    /// </remarks>
    public static async Task<KeyVerification> VerifyWithRefreshAsync(this IDidResolver resolver, Did did, Func<DidDocument, DidKey?> selectKey, Func<VerificationKey, bool> verify, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(selectKey);
        ArgumentNullException.ThrowIfNull(verify);
        var key = selectKey(await resolver.ResolveAsync(did, cancellationToken).ConfigureAwait(false));
        if (key is not null && verify(key.ToVerificationKey())) return new(true, key);
        var refreshed = selectKey(await resolver.RefreshAsync(did, cancellationToken).ConfigureAwait(false));
        return new(refreshed is not null && refreshed != key && verify(refreshed.ToVerificationKey()), refreshed);
    }
}

/// <summary>The outcome of <see cref="DidResolverExtensions.VerifyWithRefreshAsync"/>.</summary>
/// <param name="Verified">Whether the signature verified.</param>
/// <param name="KeyTried">The key tried last: the one it verified against, or the refreshed one; <see langword="null"/> when the refreshed document publishes none.</param>
public readonly record struct KeyVerification(bool Verified, DidKey? KeyTried);
