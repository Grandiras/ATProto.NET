namespace ATProtoNet.Identity;

// Verification against a key a DID document publishes.
internal static class DidResolverExtensions
{
    // Verifies against the key selectKey reads from did's document and, when there is none or the
    // verification fails, once more against the document fetched afresh.
    //
    // A cached document may predate a key rotation, or the key's publication, so a signature is not
    // declared bad until the document has been fetched once more: the one refetch the sync and label
    // specs ask for. A verification that passes never refetches, and the key is tried again only when
    // the refetch changed it.
    //
    // The refetch is IDidResolver.RefreshAsync, which a caching resolver rate-limits per DID: anyone can
    // send a bad signature naming any DID, and forged ones must not each cost a directory request.
    //
    // A resolution failure propagates, for each caller to report in its own terms.
    //
    // selectKey: Reads the caller's key from a document, never falling back to another entry: null when
    // there is none usable, which a refetch may cure, or an exception to refuse the document with no
    // refetch.
    //
    // verify: Whether the signature verifies against a key; an unusable key is a false.
    //
    // Returns: Whether it verified, and the key tried last: the one it verified against, or the
    // refetched one it failed against, null when the refetched document publishes none.
    public static Task<(bool Verified, string? Key)> VerifyWithRefreshAsync(
        this IDidResolver resolver,
        Did did,
        Func<DidDocument, string?> selectKey,
        Func<string, bool> verify,
        CancellationToken cancellationToken) =>
        VerifyWithRefreshAsync(
            refresh => refresh ? resolver.RefreshAsync(did, cancellationToken) : resolver.ResolveAsync(did, cancellationToken),
            selectKey,
            verify);

    // VerifyWithRefreshAsync over a caller's own resolution, which fetches afresh when passed true.
    public static async Task<(bool Verified, string? Key)> VerifyWithRefreshAsync(
        Func<bool, Task<DidDocument>> resolve, Func<DidDocument, string?> selectKey, Func<string, bool> verify)
    {
        var key = selectKey(await resolve(false).ConfigureAwait(false));
        if (key is not null && verify(key))
            return (true, key);

        var refreshed = selectKey(await resolve(true).ConfigureAwait(false));
        var changed = refreshed is not null && !string.Equals(refreshed, key, StringComparison.Ordinal);
        return (changed && verify(refreshed!), refreshed);
    }
}
