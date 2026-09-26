namespace ATProtoNet.Identity;

/// <summary>
/// Resolves a DID to its DID document.
/// </summary>
/// <remarks>
/// <para><see cref="DidResolver"/> fetches <c>did:plc</c> and <c>did:web</c> documents under the
/// SDK's SSRF policy; <see cref="CachingDidResolver"/> caches any resolver. Implement this
/// interface to resolve from somewhere else, such as a directory mirror or a fixture.</para>
/// <para>A consumer that verifies signatures against a resolved key calls
/// <see cref="RefreshAsync"/> once when a signature fails, since a cached document may predate
/// a key rotation, and <see cref="InvalidateAsync"/> when it learns of an identity change, such
/// as an <c>#identity</c> firehose event.</para>
/// </remarks>
public interface IDidResolver
{
    /// <summary>
    /// Resolves a DID to its document, from a cache where the resolver keeps one.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, whose <see cref="DidDocument.Id"/> is <paramref name="did"/>.</returns>
    /// <exception cref="DidResolutionException">Thrown when the DID cannot be resolved.</exception>
    Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a DID's document afresh, bypassing and then replacing any cached copy.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document.</returns>
    /// <exception cref="DidResolutionException">Thrown when the DID cannot be resolved.</exception>
    /// <remarks>
    /// A resolver that caches nothing resolves again. A caching one may rate-limit refreshes per
    /// DID and return the copy it just fetched, since anyone can trigger a refresh by sending a
    /// bad signature.
    /// </remarks>
    Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default) =>
        ResolveAsync(did, cancellationToken);

    /// <summary>
    /// Drops anything cached for a DID, so its next resolution fetches the document afresh.
    /// </summary>
    /// <param name="did">The DID whose document changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>A resolver that caches nothing has nothing to drop.</remarks>
    Task InvalidateAsync(Did did, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Resolves a handle to the DID it names, through the handle's own authorities: the
/// <c>_atproto</c> DNS TXT record and <c>https://&lt;handle&gt;/.well-known/atproto-did</c>.
/// </summary>
/// <remarks>
/// A handle resolving to a DID is only half of a verified handle; the DID document must also
/// claim the handle. <see cref="IIdentityResolver"/> checks both directions.
/// </remarks>
public interface IHandleResolver
{
    /// <summary>
    /// Resolves a handle to a DID.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID, or <see langword="null"/> when no authority answered with one.</returns>
    /// <exception cref="DidResolutionException">
    /// Thrown with <see cref="DidResolutionErrorKind.HandleConflict"/> when the authorities
    /// disagree.
    /// </exception>
    Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves an account identifier, a DID or a handle, to a verified identity.
/// </summary>
public interface IIdentityResolver
{
    /// <summary>
    /// Resolves a DID or a handle to the account's DID document, its bidirectionally verified
    /// handle and its PDS.
    /// </summary>
    /// <param name="identifier">A DID or a handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="DidResolutionException">
    /// Thrown when the DID cannot be resolved, or a handle identifier resolves to no DID
    /// (<see cref="DidResolutionErrorKind.HandleNotFound"/>) or to conflicting ones.
    /// </exception>
    Task<ResolvedIdentity> ResolveAsync(AtIdentifier identifier, CancellationToken cancellationToken = default);
}

/// <summary>
/// An account identity, resolved and checked in both directions.
/// </summary>
/// <param name="Did">The account's DID.</param>
/// <param name="Handle">
/// The handle: the verified one when <paramref name="HandleVerified"/>, otherwise
/// <see cref="Identity.Handle.Invalid"/> (<c>handle.invalid</c>) when a handle was claimed or
/// asked for but did not verify, or <see langword="null"/> when the document claims none.
/// </param>
/// <param name="HandleVerified">
/// Whether the DID document claims the handle and the handle resolves back to the DID. Render an
/// unverified account by its DID or as <c>handle.invalid</c>, never by the handle it claims.
/// </param>
/// <param name="PdsEndpoint">The account's PDS, or <see langword="null"/> when the document publishes none.</param>
/// <param name="Document">The DID document.</param>
public sealed record ResolvedIdentity(
    Did Did, Handle? Handle, bool HandleVerified, Uri? PdsEndpoint, DidDocument Document);
