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
/// <para>Each delivery is authenticated with service auth issued by this service, addressed to
/// the subscriber's DID and scoped to the method being called.</para>
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
    public Task<int> NotifyWriteAsync(
        SpaceUri space, string repoDid, string rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rev);
        ArgumentNullException.ThrowIfNull(hash);

        var body = new NotifyWriteRequest { Space = space.Value, Repo = repoDid, Rev = rev, Hash = hash };
        return FanOutAsync(space, SpaceNsids.NotifyWrite, body, cancellationToken);
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

        var body = new NotifySpaceDeletedRequest { Space = space.Value };
        return FanOutAsync(space, SpaceNsids.NotifySpaceDeleted, body, cancellationToken);
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
        SpaceUri space, string nsid, TBody body, CancellationToken cancellationToken)
    {
        var subscribers = await _store.ListSubscribersAsync(space, cancellationToken);
        if (subscribers.Count == 0)
            return 0;

        var deliveries = subscribers.Select(s => DeliverAsync(space, s, nsid, body, cancellationToken));
        var results = await Task.WhenAll(deliveries);

        return results.Count(delivered => delivered);
    }

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
                "Bearer", _serviceAuth.CreateToken(did, nsid));

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
