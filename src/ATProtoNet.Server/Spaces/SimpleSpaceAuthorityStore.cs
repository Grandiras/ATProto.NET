using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// An <see cref="ISpaceAuthorityStore"/> that takes a space's <em>existence</em> from an
/// <see cref="ISimpleSpaceStore"/> and keeps only the writer set and the notification
/// registrations of its own.
/// </summary>
/// <remarks>
/// <para>The two stores answer different questions. Which spaces this service hosts, and which
/// have been deleted, is space-management state — <c>com.atproto.simplespace</c> owns it, and
/// <c>createSpace</c> is what writes it. The writer set and the notification registrations are
/// protocol state the authority maintains from the <c>notifyWrite</c> calls it receives. Without
/// a bridge the second store never hears about a space the first one created, so
/// <c>listRepos</c>, <c>registerNotify</c>, and <c>notifyWrite</c> all refuse it with
/// <see cref="SpaceErrors.SpaceNotFound"/> — and a space whose writer set can never be populated
/// is a space no syncer can find anything in.</para>
/// <para>Reading existence rather than copying it is what keeps deletion working too: a space
/// flagged deleted through <c>deleteSpace</c> answers <see cref="SpaceErrors.SpaceDeleted"/>
/// here from the same moment, which is how a syncer that missed the notification learns to drop
/// its copy.</para>
/// <para>A space the <c>simplespace</c> store has never heard of falls through to the inner
/// store, so a service running a bespoke space type alongside the baseline keeps declaring those
/// spaces to the authority store directly.</para>
/// <para><see cref="SpaceServerExtensions.AddSpaceAuthority{TStore}"/> applies this
/// automatically when an <see cref="ISimpleSpaceStore"/> is registered, in either order. Wrap it
/// by hand only when registering an <see cref="ISpaceAuthorityStore"/> yourself.</para>
/// </remarks>
public sealed class SimpleSpaceAuthorityStore : ISpaceAuthorityStore
{
    private readonly ISpaceAuthorityStore _inner;
    private readonly ISimpleSpaceStore _spaces;

    /// <summary>
    /// Creates the bridge.
    /// </summary>
    /// <param name="inner">The store holding the writer set and the notification registrations.</param>
    /// <param name="spaces">The space-management store that knows which spaces exist.</param>
    public SimpleSpaceAuthorityStore(ISpaceAuthorityStore inner, ISimpleSpaceStore spaces)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(spaces);

        _inner = inner;
        _spaces = spaces;
    }

    /// <summary>The store holding the writer set and the notification registrations.</summary>
    public ISpaceAuthorityStore Inner => _inner;

    /// <inheritdoc/>
    public async Task<SpaceAccessOutcome> GetSpaceStateAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var record = await _spaces.GetSpaceAsync(space, cancellationToken);
        if (record is not null)
            return record.Deleted ? SpaceAccessOutcome.SpaceDeleted : SpaceAccessOutcome.Granted;

        // Not a simplespace space. It may still be one of a bespoke space type declared to the
        // authority store directly, so this is a fall-through rather than a refusal.
        return await _inner.GetSpaceStateAsync(space, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default) =>
        _inner.ListReposAsync(space, limit, cursor, cancellationToken);

    /// <inheritdoc/>
    public Task RecordWriteAsync(
        SpaceUri space, string repoDid, string rev, byte[] hash, CancellationToken cancellationToken = default) =>
        _inner.RecordWriteAsync(space, repoDid, rev, hash, cancellationToken);

    /// <inheritdoc/>
    public Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        _inner.RegisterNotifyAsync(space, service, expiresAt, cancellationToken);

    /// <inheritdoc/>
    public Task UnregisterNotifyAsync(SpaceUri space, string service, CancellationToken cancellationToken = default) =>
        _inner.UnregisterNotifyAsync(space, service, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SpaceNotifySubscriber>> ListSubscribersAsync(
        SpaceUri space, CancellationToken cancellationToken = default) =>
        _inner.ListSubscribersAsync(space, cancellationToken);
}
