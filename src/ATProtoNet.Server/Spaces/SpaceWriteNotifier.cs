using System.Net;
using System.Net.Http.Json;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.Spaces;

/// <summary>Delivers a space's write and deletion notifications to the services registered for it.</summary>
/// <remarks>
/// <para>There is no relay for permissioned data, so an application keeps its copy current by
/// pulling from each repo host itself. Notifications are what stop that being a poll: they carry
/// no record data — only that a repo reached a new revision and hash — and say "this one
/// advanced, read it now".</para>
/// <para>They are deliberately <b>best-effort</b>. A dropped notification is not a lost write:
/// the repo is caught up by a later one, or by the syncer's catch-up over <c>listRepos</c> from its
/// <c>spaceRev</c> checkpoint, which is the actual correctness guarantee. A failed delivery is
/// therefore logged and dropped, and one unreachable subscriber never holds up the others. The one
/// leg that retries is a repo host's notification to its authority
/// (<see cref="NotifyWriteInBackground"/>): the authority is what sequences a write for everyone
/// else, so that leg is retried with backoff for <see cref="RetryWindow"/>, coalescing to the latest
/// revision per repo and space.</para>
/// <para>Each delivery is authenticated with service auth scoped to the method being called and
/// addressed to the service identifier the subscriber registered — fragment and all, since that
/// is the audience a subscriber such as
/// <c>did:web:syncer.example.com#atproto_space_syncer</c> verifies against. The one exception is
/// a space's own authority, registered by <see cref="EnsureAuthoritySubscribedAsync"/> as
/// <c>{authority}#atproto_space_host</c>: it is reached at its space host endpoint, falling back
/// to its <c>#atproto_pds</c>, and addressed by its bare DID, which is what the reference
/// authority checks.</para>
/// <para>The token is signed as the account the call speaks for — the writer on a repo host's
/// notification, the space's authority on a forwarded one or a deletion — when an
/// <see cref="ISpaceAccountSigner"/> holds that account's key, since the reference authority
/// accepts a write notification only from its writer. Otherwise it is signed as this service.</para>
/// </remarks>
public sealed class SpaceWriteNotifier
{
    private static readonly Nsid NotifyWrite = Nsid.Parse(SpaceNsids.NotifyWrite);
    private static readonly Nsid NotifySpaceDeleted = Nsid.Parse(SpaceNsids.NotifySpaceDeleted);

    private readonly ISpaceAuthorityStore _store;
    private readonly IDidResolver _resolver;
    private readonly ServiceAuthGenerator _serviceAuth;
    private readonly HttpClient _httpClient;
    private readonly ISpaceAccountSigner? _accountSigner;
    private readonly ILogger _logger;
    private readonly Dictionary<(string Space, string Repo), PendingWrite> _pending = [];

    /// <summary>How long <see cref="NotifyWriteInBackground"/> keeps retrying a failed delivery. A newer revision of the same repo and space starts the window afresh. Defaults to one hour.</summary>
    public TimeSpan RetryWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The delay before the first retry, which doubles with each further one up to five minutes, with jitter. Defaults to five seconds.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Creates a notifier.</summary>
    /// <param name="store">The authority's state, which holds the subscriber list.</param>
    /// <param name="resolver">Resolves each subscriber's delivery endpoint.</param>
    /// <param name="serviceAuth">
    /// Signs the outbound service auth tokens as this service, when
    /// <paramref name="accountSigner"/> cannot sign as the account a call speaks for.
    /// </param>
    /// <param name="httpClient">The client used for delivery.</param>
    /// <param name="accountSigner">Signs as the hosted account a call speaks for. Optional.</param>
    /// <param name="logger">Optional logger.</param>
    public SpaceWriteNotifier(
        ISpaceAuthorityStore store,
        IDidResolver resolver,
        ServiceAuthGenerator serviceAuth,
        HttpClient httpClient,
        ISpaceAccountSigner? accountSigner = null,
        ILogger<SpaceWriteNotifier>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(serviceAuth);
        ArgumentNullException.ThrowIfNull(httpClient);

        _store = store;
        _resolver = resolver;
        _serviceAuth = serviceAuth;
        _httpClient = httpClient;
        _accountSigner = accountSigner;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>Fans a write notification out to every service registered for the space.</summary>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="repoRev">The repo's revision after the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <returns>The number of subscribers the notification reached.</returns>
    /// <remarks>
    /// One attempt, with no retry: call <see cref="NotifyWriteInBackground"/> instead to have a failed
    /// delivery retried. This is the repo host's half of the notification path. On a repo host the subscribers
    /// include the space's authority, registered by <see cref="EnsureAuthoritySubscribedAsync"/>,
    /// which applies its write policy and forwards the notification to its own subscribers
    /// (<see cref="ForwardWriteAsync"/>). Each delivery is signed as <paramref name="repoDid"/>
    /// when an <see cref="ISpaceAccountSigner"/> holds its key.
    /// </remarks>
    public async Task<int> NotifyWriteAsync(
        SpaceUri space, Did repoDid, Tid repoRev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(repoRev);
        ArgumentNullException.ThrowIfNull(hash);

        var body = new NotifyWriteRequest { Space = space, Repo = repoDid, RepoRev = repoRev, Hash = hash };
        var outcomes = await FanOutAsync(space, repoDid, NotifyWrite, body, includeAuthority: true, cancellationToken).ConfigureAwait(false);
        return outcomes.Count(o => o == Delivery.Delivered);
    }

    /// <summary>Notifies the space's subscribers (the authority first among them) of a write in the background, retrying what fails.</summary>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="repoRev">The repo's revision after the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <remarks>
    /// <para>This is what a repo host calls after a write. A delivery that fails in a way that may pass
    /// (a network error, a timeout, a 408, 429 or 5xx answer, an endpoint that does not resolve) is
    /// retried with exponential backoff and jitter until <see cref="RetryWindow"/> runs out; one the
    /// receiver refused (any other answer, such as <c>FutureRev</c> or a refused writer) is dropped.</para>
    /// <para>Calls coalesce per repo and space: a newer revision replaces a pending older one and
    /// restarts the window, and an older or equal one is ignored. The authority applies each
    /// idempotently, so a repeated delivery is harmless.</para>
    /// <para>The queue lives in this process. The reference PDS persists it and retries for 24 hours,
    /// so that a restart loses nothing; here a restart drops what is pending, and the repo is caught
    /// up by its next write. Call it again from your own durable record of writes to close that gap.</para>
    /// </remarks>
    public void NotifyWriteInBackground(SpaceUri space, Did repoDid, Tid repoRev, byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(repoRev);
        ArgumentNullException.ThrowIfNull(hash);

        var key = (space.Value, repoDid.Value);
        var write = new PendingWrite(repoRev, hash, DateTimeOffset.UtcNow + RetryWindow);

        lock (_pending)
        {
            // A worker is already delivering this repo: hand it the newer state.
            if (_pending.TryGetValue(key, out var current))
            {
                if (repoRev.CompareTo(current.RepoRev) > 0)
                    _pending[key] = write;
                return;
            }

            _pending[key] = write;
        }

        _ = Task.Run(() => DeliverWithRetryAsync(space, repoDid, key));
    }

    // Delivers the latest pending write of one repo and space until it succeeds, is refused, or the
    // window closes, picking up a newer revision whenever one arrives meanwhile.
    private async Task DeliverWithRetryAsync(SpaceUri space, Did repoDid, (string Space, string Repo) key)
    {
        var attempt = 0;
        while (true)
        {
            PendingWrite write;
            lock (_pending)
                write = _pending[key];

            var retry = false;
            try
            {
                var body = new NotifyWriteRequest { Space = space, Repo = repoDid, RepoRev = write.RepoRev, Hash = write.Hash };
                var outcomes = await FanOutAsync(space, repoDid, NotifyWrite, body, includeAuthority: true, CancellationToken.None).ConfigureAwait(false);
                retry = outcomes.Contains(Delivery.Retry);
            }
            catch (Exception ex)
            {
                // Reading the subscriber list failed, which may pass.
                _logger.LogWarning(ex, "Notifying {Space} of a write by {Repo} failed.", space, repoDid);
                retry = true;
            }

            TimeSpan delay;
            lock (_pending)
            {
                if (!ReferenceEquals(_pending[key], write))
                {
                    attempt = 0;
                    continue;
                }

                var remaining = write.Deadline - DateTimeOffset.UtcNow;
                if (!retry || remaining <= TimeSpan.Zero)
                {
                    _pending.Remove(key);
                    if (retry)
                        _logger.LogWarning("Gave up notifying {Space} of {Repo} at {RepoRev}.", space, repoDid, write.RepoRev);
                    return;
                }

                var backoff = Math.Min(RetryBaseDelay.TotalSeconds * Math.Pow(2, attempt++), 300);
                delay = TimeSpan.FromSeconds(backoff * (0.5 + Random.Shared.NextDouble() / 2));
                if (delay > remaining)
                    delay = remaining;
            }

            await Task.Delay(delay).ConfigureAwait(false);
        }
    }

    private sealed record PendingWrite(Tid RepoRev, byte[] Hash, DateTimeOffset Deadline);

    /// <summary>Forwards a write notification this authority accepted to the services registered for the space, in the background.</summary>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="repoRev">The repo's revision after the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <param name="sequence">The space revision <see cref="ISpaceAuthorityStore.RecordWriteAsync"/> assigned, which the syncers use to detect a gap.</param>
    /// <returns>
    /// The fan-out, which never faults: it resolves to the number of subscribers reached. The
    /// <c>notifyWrite</c> endpoint does not await it, so neither the writer's repo host nor the
    /// request waits on downstream syncers.
    /// </returns>
    /// <remarks>
    /// <para>This is the authority's half of the notification path: a repo host tells the
    /// authority, and the authority tells everyone who registered with it. Call it only for a
    /// write the space's write policy admitted.</para>
    /// <para>The space's own authority subscription is skipped. On a service that is both repo
    /// host and authority it sits in the same store, and forwarding to it would only deliver the
    /// notification back to this endpoint.</para>
    /// </remarks>
    public Task<int> ForwardWriteAsync(SpaceUri space, Did repoDid, Tid repoRev, byte[] hash, SpaceWriteSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(repoRev);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(sequence);

        var body = new NotifyWriteRequest
        {
            Space = space, Repo = repoDid, RepoRev = repoRev, Hash = hash,
            SpaceRev = sequence.SpaceRev, PrevSpaceRev = sequence.PrevSpaceRev,
        };

        // Off the caller's path, and with no cancellation token: the request that triggered this
        // completes before the deliveries do, and cancelling them with it would drop them all.
        return Task.Run(async () =>
        {
            try
            {
                var outcomes = await FanOutAsync(
                    space, space.Authority, NotifyWrite, body, includeAuthority: false, CancellationToken.None).ConfigureAwait(false);
                return outcomes.Count(o => o == Delivery.Delivered);
            }
            catch (Exception ex)
            {
                // Best-effort: a failure to even read the subscriber list is a latency cost, since
                // every syncer's catch-up over listRepos still finds the write.
                _logger.LogWarning(ex, "Forwarding {Nsid} for {Space} failed.", SpaceNsids.NotifyWrite, space);
                return 0;
            }
        });
    }

    /// <summary>Tells every registered service that a space was deleted and its data should be dropped.</summary>
    /// <param name="space">The deleted space.</param>
    /// <returns>The number of subscribers the notification reached.</returns>
    /// <remarks>
    /// A syncer that misses this learns on its next credential renewal, which answers
    /// <see cref="SpaceErrors.SpaceDeleted"/> — so this is a latency optimization here too, not
    /// the mechanism a deletion depends on.
    /// </remarks>
    public async Task<int> NotifySpaceDeletedAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        // Only the authority deletes a space, so its own registration has nothing to learn.
        var body = new NotifySpaceDeletedRequest(space);
        var outcomes = await FanOutAsync(space, space.Authority, NotifySpaceDeleted, body, includeAuthority: false, cancellationToken).ConfigureAwait(false);
        return outcomes.Count(o => o == Delivery.Delivered);
    }

    /// <summary>Registers a space's own authority as a subscriber for a repo's writes, if it is not the repo's owner.</summary>
    /// <param name="space">The space being written into.</param>
    /// <param name="repoDid">The account doing the writing.</param>
    /// <param name="lifetime">How long the registration lasts. Defaults to 30 days.</param>
    /// <returns><see langword="true"/> when a registration was added.</returns>
    /// <remarks>
    /// <para>Call this from a repo host on the first write into a shared space. Without it a
    /// space's writer set would never mention the account: the authority learns who holds data in
    /// its spaces only from the write notifications it receives, and it receives them only
    /// because it is registered to.</para>
    /// <para>A personal-data space needs none of this — the authority and the repo host are the
    /// same service, so it is skipped when the space is anchored on the writing account's own
    /// DID.</para>
    /// </remarks>
    public async Task<bool> EnsureAuthoritySubscribedAsync(
        SpaceUri space,
        Did repoDid,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);

        if (space.Authority == repoDid)
            return false;

        var service = SpaceAuthority.HostAudience(space.Authority);
        var subscribers = await _store.ListSubscribersAsync(space, cancellationToken).ConfigureAwait(false);
        if (subscribers.Any(s => string.Equals(s.Service, service, StringComparison.Ordinal)))
            return false;

        await _store.RegisterNotifyAsync(
            space, service, DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)), cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Registered {Service} for {Space} on the first write by {Repo}.", service, space, repoDid);
        return true;
    }

    // Delivers one notification to every subscriber of a space, reporting how each ended.
    //
    // issuer: The account the notification speaks for, which it is signed as when possible.
    //
    // nsid: The method being called.
    //
    // body: The request body.
    //
    // includeAuthority: Whether the space's own authority subscription is delivered to.
    private async Task<Delivery[]> FanOutAsync<TBody>(
        SpaceUri space, Did issuer, Nsid nsid, TBody body, bool includeAuthority, CancellationToken cancellationToken)
    {
        var subscribers = await _store.ListSubscribersAsync(space, cancellationToken).ConfigureAwait(false);

        if (!includeAuthority)
            subscribers = subscribers.Where(s => !IsAuthority(space, s.Service)).ToList();

        if (subscribers.Count == 0)
            return [];

        var signer = await SpaceAccountSigning.ChooseAsync(_accountSigner, _serviceAuth, issuer, _logger, cancellationToken).ConfigureAwait(false);
        var deliveries = subscribers.Select(s => DeliverAsync(space, s, signer, nsid, body, cancellationToken));
        return await Task.WhenAll(deliveries).ConfigureAwait(false);
    }

    // How one delivery ended: Retry for a failure that may pass, Failed for one that will not.
    private enum Delivery { Delivered, Retry, Failed }

    // Whether an answer says "try again later" rather than "no".
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    // The aud a delivery is addressed to: the identifier the subscriber registered, except for the
    // space's own authority host, which the reference authority expects to be addressed by its bare DID.
    private static string Audience(SpaceUri space, string service) =>
        string.Equals(service, SpaceAuthority.HostAudience(space.Authority), StringComparison.Ordinal)
            ? space.Authority.Value
            : service;

    // Whether a service identifier names the space's own authority as its space host — bare, or as
    // #atproto_space_host, both of which resolve to the same endpoint.
    private static bool IsAuthority(SpaceUri space, string service) =>
        string.Equals(service, space.Authority.Value, StringComparison.Ordinal) ||
        string.Equals(service, SpaceAuthority.HostAudience(space.Authority), StringComparison.Ordinal);

    // Whether an exception is a delivery failure to be logged and dropped, rather than a cancellation
    // the caller asked for and must see.
    //
    // Anything a subscriber's DID document or endpoint can make go wrong is a failed delivery to that
    // subscriber: the document is written by whoever registered it, so letting one of its failures
    // escape would let a single subscriber stop deleteSpace for everyone. An HttpClient timeout surfaces
    // as a cancellation nobody requested, and counts too.
    private static bool IsDeliveryFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private async Task<Delivery> DeliverAsync<TBody>(
        SpaceUri space,
        SpaceNotifySubscriber subscriber,
        ServiceAuthGenerator signer,
        Nsid nsid,
        TBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = await SpaceServiceCall.ResolveAsync(_resolver, subscriber.Service, nsid, cancellationToken).ConfigureAwait(false);
            if (url is null)
            {
                _logger.LogWarning(
                    "Subscriber {Service} for {Space} resolves to no delivery endpoint; skipping.",
                    subscriber.Service, space);
                return Delivery.Retry;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: AtProtoJsonDefaults.Options),
            };

            using var response = await SpaceServiceCall.SendAsync(
                _httpClient, request, signer, Audience(space, subscriber.Service), nsid, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return Delivery.Delivered;

            _logger.LogWarning(
                "Delivering {Nsid} for {Space} to {Service} answered {Status}.",
                nsid, space, subscriber.Service, (int)response.StatusCode);
            return IsTransient(response.StatusCode) ? Delivery.Retry : Delivery.Failed;
        }
        catch (Exception ex) when (IsDeliveryFailure(ex, cancellationToken))
        {
            // Best-effort by design: the syncer's catch-up over listRepos is what makes a dropped
            // notification a latency cost rather than a lost write.
            _logger.LogWarning(
                ex, "Delivering {Nsid} for {Space} to {Service} failed.", nsid, space, subscriber.Service);
            return Delivery.Retry;
        }
    }
}
