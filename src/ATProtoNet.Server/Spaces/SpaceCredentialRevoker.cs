using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>A space authority's way of revoking credentials it issued before they expire.</summary>
/// <remarks>
/// <para>Credentials last ten minutes by default, and that is the primary limit on a reader whose access
/// ended; revoking is the early exit for the cases where waiting is too long, and it is optional. Removing
/// a member does not revoke anything by itself: call <see cref="RevokeAsync"/> with the <c>jti</c> of the
/// credentials to cut off (<see cref="SpaceToken.TokenId"/>; the authority keeps no record of what it issued).</para>
/// <para>It records the revocation on this service, which then refuses the credentials on its own authority
/// endpoints, and tells the repo host of every writer in the space with
/// <see cref="SpaceWriteNotifier.NotifyCredentialRevokedAsync"/>. A host that cannot be reached keeps
/// accepting the credential until it expires.</para>
/// </remarks>
public sealed class SpaceCredentialRevoker
{
    private readonly SpaceWriteNotifier _notifier;
    private readonly ISpaceCredentialRevocationStore _store;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a revoker.</summary>
    /// <param name="notifier">Delivers the revocation to the writers' repo hosts.</param>
    /// <param name="store">Records the revocation for this service's own endpoints.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceCredentialRevoker(
        SpaceWriteNotifier notifier,
        ISpaceCredentialRevocationStore store,
        SpaceServerOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        _notifier = notifier;
        _store = store;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Revokes credentials issued for a space.</summary>
    /// <param name="space">The space the credentials grant access to; this service must be its authority.</param>
    /// <param name="credentialIds">The <c>jti</c> of each credential to revoke. Any number; sent in batches of 100.</param>
    /// <returns>The number of deliveries to repo hosts that succeeded (one per writer and batch).</returns>
    /// <exception cref="ArgumentException">An identifier is blank, or <paramref name="space"/> is another authority's.</exception>
    public async Task<int> RevokeAsync(
        SpaceUri space, IReadOnlyCollection<string> credentialIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(credentialIds);

        if (credentialIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A credential is revoked by its non-blank jti.", nameof(credentialIds));
        if (_options.ServiceDid is { } serviceDid && space.Authority != serviceDid)
            throw new ArgumentException($"This service is not the authority for {space}.", nameof(space));
        if (credentialIds.Count == 0)
            return 0;

        await _store.RevokeAsync(
            space, credentialIds, SpaceCredentialRevocation.RetainUntil(_timeProvider.GetUtcNow(), _options), cancellationToken).ConfigureAwait(false);

        return await _notifier.NotifyCredentialRevokedAsync(space, credentialIds, cancellationToken).ConfigureAwait(false);
    }
}
