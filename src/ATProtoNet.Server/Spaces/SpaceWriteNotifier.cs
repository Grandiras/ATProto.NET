using System.Net.Http.Headers;
using System.Net.Http.Json;
using ATProtoNet.Auth;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Delivers a space's write and deletion notifications to the services registered for it.
/// </summary>
/// <remarks>
/// <para>There is no relay for permissioned data, so an application keeps its copy current by
/// pulling from each repo host itself. Notifications are what stop that being a poll: they carry
/// no record data — only that a repo reached a new revision and hash — and say "this one
/// advanced, read it now".</para>
/// <para>They are deliberately <b>best-effort</b>. A dropped notification is not a lost write:
/// the repo is caught up by a later one, or by the syncer's periodic sweep over
/// <c>listRepos</c>, which is the actual correctness guarantee. Delivery failures are therefore
/// logged and dropped rather than retried into a queue, and one unreachable subscriber never
/// holds up the others.</para>
/// <para>Each delivery is authenticated with service auth issued by this service, scoped to the
/// method being called and addressed to the service identifier the subscriber registered —
/// fragment and all, since that is the audience a subscriber such as
/// <c>did:web:syncer.example.com#atproto_space_syncer</c> verifies against. The one exception is
/// a space's own authority, registered by <see cref="EnsureAuthoritySubscribedAsync"/> as
/// <c>{authority}#atproto_space_host</c>: it is reached at its space host endpoint, falling back
/// to its <c>#atproto_pds</c>, and addressed by its bare DID, which is what the reference
/// authority checks.</para>
/// </remarks>
public sealed class SpaceWriteNotifier
{
    private readonly ISpaceAuthorityStore _store;
    private readonly ISpaceDidDocumentResolver _resolver;
    private readonly ServiceAuthGenerator _serviceAuth;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a notifier.
    /// </summary>
    /// <param name="store">The authority's state, which holds the subscriber list.</param>
    /// <param name="resolver">Resolves each subscriber's delivery endpoint.</param>
    /// <param name="serviceAuth">Signs the outbound service auth tokens as this service.</param>
    /// <param name="httpClient">The client used for delivery.</param>
    /// <param name="logger">Optional logger.</param>
    public SpaceWriteNotifier(
        ISpaceAuthorityStore store,
        ISpaceDidDocumentResolver resolver,
        ServiceAuthGenerator serviceAuth,
        HttpClient httpClient,
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
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Fans a write notification out to every service registered for the space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="rev">The revision of the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of subscribers the notification reached.</returns>
    /// <remarks>
    /// This is the repo host's half of the notification path. On a repo host the subscribers
    /// include the space's authority, registered by <see cref="EnsureAuthoritySubscribedAsync"/>,
    /// which applies its write policy and forwards the notification to its own subscribers
    /// (<see cref="ForwardWriteAsync"/>).
    /// </remarks>
    public Task<int> NotifyWriteAsync(
        SpaceUri space, string repoDid, string rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rev);
        ArgumentNullException.ThrowIfNull(hash);

        var body = new NotifyWriteRequest { Space = space.Value, Repo = repoDid, Rev = rev, Hash = hash };
        return FanOutAsync(space, SpaceNsids.NotifyWrite, body, includeAuthority: true, cancellationToken);
    }

    /// <summary>
    /// Forwards a write notification this authority accepted to the services registered for the
    /// space, in the background.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="rev">The revision of the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
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
    public Task<int> ForwardWriteAsync(SpaceUri space, string repoDid, string rev, byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rev);
        ArgumentNullException.ThrowIfNull(hash);

        var body = new NotifyWriteRequest { Space = space.Value, Repo = repoDid, Rev = rev, Hash = hash };

        // Off the caller's path, and with no cancellation token: the request that triggered this
        // completes before the deliveries do, and cancelling them with it would drop them all.
        return Task.Run(async () =>
        {
            try
            {
                return await FanOutAsync(
                    space, SpaceNsids.NotifyWrite, body, includeAuthority: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Best-effort: a failure to even read the subscriber list is a latency cost, since
                // every syncer's sweep over listRepos still finds the write.
                _logger.LogWarning(ex, "Forwarding {Nsid} for {Space} failed.", SpaceNsids.NotifyWrite, space);
                return 0;
            }
        });
    }

    /// <summary>
    /// Tells every registered service that a space was deleted and its data should be dropped.
    /// </summary>
    /// <param name="space">The deleted space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of subscribers the notification reached.</returns>
    /// <remarks>
    /// A syncer that misses this learns on its next credential renewal, which answers
    /// <see cref="SpaceErrors.SpaceDeleted"/> — so this is a latency optimization here too, not
    /// the mechanism a deletion depends on.
    /// </remarks>
    public Task<int> NotifySpaceDeletedAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        // Only the authority deletes a space, so its own registration has nothing to learn.
        var body = new NotifySpaceDeletedRequest { Space = space.Value };
        return FanOutAsync(space, SpaceNsids.NotifySpaceDeleted, body, includeAuthority: false, cancellationToken);
    }

    /// <summary>
    /// Registers a space's own authority as a subscriber for a repo's writes, if it is not the
    /// repo's owner.
    /// </summary>
    /// <param name="space">The space being written into.</param>
    /// <param name="repoDid">The account doing the writing.</param>
    /// <param name="lifetime">How long the registration lasts. Defaults to 30 days.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
        string repoDid,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);

        if (string.Equals(space.Authority, repoDid, StringComparison.Ordinal))
            return false;

        var service = SpaceAuthority.HostAudience(space.Authority);
        var subscribers = await _store.ListSubscribersAsync(space, cancellationToken);
        if (subscribers.Any(s => string.Equals(s.Service, service, StringComparison.Ordinal)))
            return false;

        await _store.RegisterNotifyAsync(
            space, service, DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)), cancellationToken);

        _logger.LogDebug("Registered {Service} for {Space} on the first write by {Repo}.", service, space, repoDid);
        return true;
    }

    private async Task<int> FanOutAsync<TBody>(
        SpaceUri space, string nsid, TBody body, bool includeAuthority, CancellationToken cancellationToken)
    {
        var subscribers = await _store.ListSubscribersAsync(space, cancellationToken);

        if (!includeAuthority)
            subscribers = subscribers.Where(s => !IsAuthority(space, s.Service)).ToList();

        if (subscribers.Count == 0)
            return 0;

        var deliveries = subscribers.Select(s => DeliverAsync(space, s, nsid, body, cancellationToken));
        var results = await Task.WhenAll(deliveries);

        return results.Count(delivered => delivered);
    }

    /// <summary>
    /// The <c>aud</c> a delivery is addressed to: the identifier the subscriber registered,
    /// except for the space's own authority host, which the reference authority expects to be
    /// addressed by its bare DID.
    /// </summary>
    private static string Audience(SpaceUri space, string service, string did) =>
        string.Equals(service, SpaceAuthority.HostAudience(space.Authority), StringComparison.Ordinal)
            ? did
            : service;

    /// <summary>
    /// Whether a service identifier names the space's own authority as its space host — bare, or
    /// as <c>#atproto_space_host</c>, both of which resolve to the same endpoint.
    /// </summary>
    private static bool IsAuthority(SpaceUri space, string service) =>
        string.Equals(service, space.Authority, StringComparison.Ordinal) ||
        string.Equals(service, SpaceAuthority.HostAudience(space.Authority), StringComparison.Ordinal);

    /// <summary>
    /// Whether an exception is a delivery failure to be logged and dropped, rather than a
    /// cancellation the caller asked for and must see.
    /// </summary>
    private static bool IsDeliveryFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            // An HttpClient timeout surfaces as a cancellation nobody requested.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException or ArgumentException or SpaceVerificationException
                or InvalidOperationException or UriFormatException => true,
            _ => false,
        };

    private async Task<bool> DeliverAsync<TBody>(
        SpaceUri space,
        SpaceNotifySubscriber subscriber,
        string nsid,
        TBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var (did, fragment) = SpaceAuthority.ParseServiceIdentifier(subscriber.Service);
            var document = await _resolver.ResolveAsync(did, cancellationToken);

            // A #atproto_space_host fragment resolves with its #atproto_pds fallback, so an
            // authority on an ordinary PDS, which publishes no such entry, is still reached.
            var endpoint = SpaceAuthority.GetServiceEndpoint(document, fragment);

            if (string.IsNullOrEmpty(endpoint))
            {
                _logger.LogWarning(
                    "Subscriber {Service} for {Space} resolves to no delivery endpoint; skipping.",
                    subscriber.Service, space);
                return false;
            }

            var url = new Uri(new Uri(endpoint.TrimEnd('/') + "/"), $"xrpc/{nsid}");

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: AtProtoJsonDefaults.Options),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", _serviceAuth.CreateToken(Audience(space, subscriber.Service, did), nsid));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            _logger.LogWarning(
                "Delivering {Nsid} for {Space} to {Service} answered {Status}.",
                nsid, space, subscriber.Service, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (IsDeliveryFailure(ex, cancellationToken))
        {
            // Best-effort by design: the syncer's periodic sweep is what makes a dropped
            // notification a latency cost rather than a lost write.
            _logger.LogWarning(
                ex, "Delivering {Nsid} for {Space} to {Service} failed.", nsid, space, subscriber.Service);
            return false;
        }
    }
}
