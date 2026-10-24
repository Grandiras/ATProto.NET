using ATProtoNet.Crypto;

namespace ATProtoNet.Identity;

/// <summary>Resolves DIDs to their documents.</summary>
public interface IDidResolver
{
    /// <summary>The DID's document, possibly cached.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The document, whose id is exactly <paramref name="did"/>.</returns>
    /// <exception cref="IdentityResolutionException">Resolution failed.</exception>
    Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>The DID's document fetched past any cache, rate-limited per DID: for a signature that failed against a cached key.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The document.</returns>
    /// <exception cref="IdentityResolutionException">Resolution failed.</exception>
    Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default) => ResolveAsync(did, cancellationToken);

    /// <summary>The DID's document fetched past any cache, never rate-limited: for security decisions the local user initiates.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The document.</returns>
    /// <exception cref="IdentityResolutionException">Resolution failed.</exception>
    Task<DidDocument> FetchFreshAsync(Did did, CancellationToken cancellationToken = default) => ResolveAsync(did, cancellationToken);

    /// <summary>Forgets anything cached for the DID.</summary>
    /// <param name="did">The DID.</param>
    /// <param name="cancellationToken">Cancels a distributed-cache removal.</param>
    /// <returns>When the entry is gone.</returns>
    ValueTask InvalidateAsync(Did did, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Resolves handles to DIDs, one direction only.</summary>
public interface IHandleResolver
{
    /// <summary>The DID the handle's authorities name.</summary>
    /// <param name="handle">The handle.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The DID, or <see langword="null"/> when no authority answers.</returns>
    /// <exception cref="IdentityResolutionException">The authorities conflict (<see cref="IdentityResolutionError.HandleConflict"/>).</exception>
    Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default);

    /// <summary>Forgets a cached answer for the handle.</summary>
    /// <param name="handle">The handle.</param>
    /// <param name="cancellationToken">Unused by in-memory caches.</param>
    /// <returns>When the answer is gone.</returns>
    ValueTask InvalidateAsync(Handle handle, CancellationToken cancellationToken = default);
}

/// <summary>Resolves a DID or handle to an identity whose handle is checked in both directions.</summary>
public interface IIdentityResolver
{
    /// <summary>Resolves an identifier.</summary>
    /// <param name="identifier">A DID or a handle.</param>
    /// <param name="options">How to resolve. Defaults apply when omitted.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="IdentityResolutionException">The DID does not resolve, or a handle input resolves to no DID.</exception>
    Task<ResolvedIdentity> ResolveAsync(AtIdentifier identifier, IdentityResolveOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets what is cached for the identifier: a DID's document, a handle's answer.</summary>
    /// <param name="identifier">A DID or a handle.</param>
    /// <param name="cancellationToken">Cancels a distributed-cache removal.</param>
    /// <returns>When the entries are gone.</returns>
    ValueTask InvalidateAsync(AtIdentifier identifier, CancellationToken cancellationToken = default);
}

/// <summary>How one identity resolution runs.</summary>
public sealed record IdentityResolveOptions
{
    /// <summary>Resolve the handle and the document past every cache, updating the caches with the results: for decisions the local user initiates, such as an OAuth callback's issuer check.</summary>
    public bool BypassCache { get; init; }
}

/// <summary>A resolved identity.</summary>
/// <param name="Did">The DID.</param>
/// <param name="Handle">The handle: the verified one, <see cref="Handle.Invalid"/> when the claim failed verification, <see langword="null"/> when the document claims none.</param>
/// <param name="HandleVerified">Whether <paramref name="Handle"/> resolves to <paramref name="Did"/> and the document claims it.</param>
/// <param name="Document">The DID document.</param>
public sealed record ResolvedIdentity(Did Did, Handle? Handle, bool HandleVerified, DidDocument Document)
{
    /// <summary>The PDS (also a space authority's host), if published.</summary>
    public Uri? Pds => Document.GetPdsEndpoint();

    /// <summary>The signing key (also a space authority's credential key), if published.</summary>
    public DidKey? SigningKey => Document.GetSigningKey();
}
