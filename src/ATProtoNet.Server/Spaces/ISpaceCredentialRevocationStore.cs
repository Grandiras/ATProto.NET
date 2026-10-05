using System.Collections.Concurrent;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>Remembers the space credentials a space's authority has revoked, so a repo host refuses them before they expire.</summary>
/// <remarks>
/// <para>Credentials are short-lived and revocation is the early exit: an authority tells a repo host
/// <c>com.atproto.space.notifyCredentialRevoked</c>, the host records <c>(space, jti)</c> here, and
/// <see cref="SpaceCredentialVerifier"/> then answers <see cref="ATProtoNet.Lexicon.Com.AtProto.Space.SpaceErrors.CredentialRevoked"/>
/// for that credential. Entries are keyed on the space as well as the <c>jti</c>, since an authority
/// chooses its own identifiers and may revoke only what it issued for its own spaces.</para>
/// <para>An entry must be kept until the credential it names could no longer verify, which the endpoint
/// states in <c>retainUntil</c>: now plus the longest credential lifetime and the clock skew on both
/// ends of it. Dropping one earlier un-revokes the credential, so a sweep may remove only entries whose
/// <c>retainUntil</c> has passed. An implementation backing a multi-instance host must be shared across
/// instances; <see cref="InMemorySpaceCredentialRevocationStore"/> is per-process and is lost on restart.</para>
/// </remarks>
public interface ISpaceCredentialRevocationStore
{
    /// <summary>Records credentials as revoked. Idempotent, and never shortens an entry that is already kept longer.</summary>
    /// <param name="space">The space the credentials grant access to.</param>
    /// <param name="credentialIds">The <c>jti</c> of each revoked credential.</param>
    /// <param name="retainUntil">The first instant at which the entries need no longer be kept.</param>
    Task RevokeAsync(
        SpaceUri space, IReadOnlyCollection<string> credentialIds, DateTimeOffset retainUntil, CancellationToken cancellationToken = default);

    /// <summary>Reports whether a credential is revoked.</summary>
    /// <param name="space">The space the credential grants access to.</param>
    /// <param name="credentialId">The credential's <c>jti</c>.</param>
    /// <param name="now">The instant to judge by; an entry whose retention has passed no longer counts.</param>
    ValueTask<bool> IsRevokedAsync(
        SpaceUri space, string credentialId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>An in-process <see cref="ISpaceCredentialRevocationStore"/>, suitable for a single-instance host.</summary>
/// <remarks>Expired entries are swept at most once a minute, in the background. It is per-process and holds nothing across a restart, which forgets every revocation made since the last credential expired.</remarks>
public sealed class InMemorySpaceCredentialRevocationStore : ISpaceCredentialRevocationStore
{
    private readonly ConcurrentDictionary<(string Space, string Id), DateTimeOffset> _revoked = new();
    private readonly SweepSchedule _sweeps;

    /// <summary>Creates a store using the system clock.</summary>
    public InMemorySpaceCredentialRevocationStore() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a store whose sweep is scheduled against <paramref name="timeProvider"/>.</summary>
    /// <param name="timeProvider">The clock to use.</param>
    public InMemorySpaceCredentialRevocationStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _sweeps = new SweepSchedule(timeProvider);
    }

    /// <summary>The number of entries currently held, for diagnostics and tests.</summary>
    public int Count => _revoked.Count;

    // The most recent background sweep, for tests to wait on.
    internal Task LastSweep => _sweeps.Last;

    /// <inheritdoc/>
    public Task RevokeAsync(
        SpaceUri space, IReadOnlyCollection<string> credentialIds, DateTimeOffset retainUntil, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(credentialIds);

        foreach (var id in credentialIds)
            _revoked.AddOrUpdate((space.ToString(), id), retainUntil, (_, kept) => kept > retainUntil ? kept : retainUntil);

        _sweeps.RunIfDue(now =>
        {
            foreach (var (key, until) in _revoked)
                if (until <= now)
                    _revoked.TryRemove(new KeyValuePair<(string, string), DateTimeOffset>(key, until));

            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<bool> IsRevokedAsync(
        SpaceUri space, string credentialId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return ValueTask.FromResult(_revoked.TryGetValue((space.ToString(), credentialId), out var until) && until > now);
    }
}
