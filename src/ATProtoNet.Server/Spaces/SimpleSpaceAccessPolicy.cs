using System.Net.Http.Json;
using ATProtoNet.Auth;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The <c>com.atproto.simplespace</c> access policy: the baseline every PDS must implement.
/// </summary>
/// <remarks>
/// <para>Two perimeters are evaluated, and a credential is minted only when both pass. The
/// <b>user policy</b> asks whether this account may read the space —
/// <see cref="PublicPolicy"/> admits anyone, <see cref="MemberListPolicy"/> consults the space's
/// member list, and <see cref="ManagingAppPolicy"/> asks the managing app per request, which is
/// what enables follower-gating, paid subscriptions, and join approvals without an app
/// maintaining an explicit list. The <b>app access policy</b> asks whether this application may
/// act — <see cref="OpenAppAccess"/> admits any, and <see cref="AllowListAppAccess"/> names the
/// client IDs it accepts.</para>
/// <para>The allow list is evaluated against the <em>attested</em> client ID and nothing else,
/// which is what makes it enforceable rather than advisory: only the holder of a key published
/// at that client ID can produce an attestation for it. A request that carries no attestation is
/// refused with <see cref="SpaceAccessOutcome.AppNotAuthorized"/>, which is also the signal that
/// tells a client holding one to retry with it — whether a space gates on app identity is not
/// advertised anywhere else.</para>
/// <para>The order matters. The app perimeter is evaluated first so that an unattested client
/// is told to attest before any user-level decision is made, and so that the managing-app call —
/// the one expensive step — is never made for a request that was going to be refused on app
/// grounds anyway.</para>
/// </remarks>
public sealed class SimpleSpaceAccessPolicy : ISpaceAccessPolicy
{
    private readonly ISimpleSpaceStore _store;
    private readonly ISimpleSpaceManagingAppClient _managingApp;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a policy.
    /// </summary>
    /// <param name="store">The spaces and member lists this authority holds.</param>
    /// <param name="managingApp">Calls <c>checkUserAccess</c> for a managing-app policy.</param>
    /// <param name="logger">Optional logger.</param>
    public SimpleSpaceAccessPolicy(
        ISimpleSpaceStore store,
        ISimpleSpaceManagingAppClient managingApp,
        ILogger<SimpleSpaceAccessPolicy>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(managingApp);

        _store = store;
        _managingApp = managingApp;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<SpaceAccessDecision> EvaluateAsync(
        SpaceAccessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var space = await _store.GetSpaceAsync(request.Space, cancellationToken);
        if (space is null)
            return SpaceAccessDecision.Refuse(SpaceAccessOutcome.SpaceNotFound, "No such space.");

        if (space.Deleted)
            return SpaceAccessDecision.Refuse(SpaceAccessOutcome.SpaceDeleted, "The space was deleted.");

        var app = EvaluateApp(space, request);
        if (!app.IsGranted)
            return app;

        return await EvaluateUserAsync(space, request, cancellationToken);
    }

    private static SpaceAccessDecision EvaluateApp(SimpleSpaceRecord space, SpaceAccessRequest request) =>
        space.AppAccess switch
        {
            OpenAppAccess => SpaceAccessDecision.Granted,

            AllowListAppAccess allowList when request.AttestedClientId is null =>
                SpaceAccessDecision.Refuse(
                    SpaceAccessOutcome.AppNotAuthorized,
                    $"The space gates on app identity ({allowList.Allowed.Count} allowed) and the request did not attest."),

            AllowListAppAccess allowList =>
                allowList.Allowed.Contains(request.AttestedClientId, StringComparer.Ordinal)
                    ? SpaceAccessDecision.Granted
                    : SpaceAccessDecision.Refuse(
                        SpaceAccessOutcome.AppNotAuthorized,
                        $"Client '{request.AttestedClientId}' is not on the space's allow list."),

            // A host rejects an app access variant it does not implement at create time rather
            // than storing one it could not enforce, so reaching this means the store handed back
            // something this policy was never asked about. Refusing is the only safe answer.
            _ => SpaceAccessDecision.Refuse(
                SpaceAccessOutcome.NotAuthorized,
                $"Unsupported app access variant '{space.AppAccess.GetType().Name}'."),
        };

    private async Task<SpaceAccessDecision> EvaluateUserAsync(
        SimpleSpaceRecord space, SpaceAccessRequest request, CancellationToken cancellationToken)
    {
        // The owner administers the space and always reaches it, whatever the policy says.
        if (string.Equals(space.Owner, request.UserDid, StringComparison.Ordinal))
            return SpaceAccessDecision.Granted;

        switch (space.Policy)
        {
            case PublicPolicy:
                return SpaceAccessDecision.Granted;

            case MemberListPolicy:
                return await _store.IsMemberAsync(space.Uri, request.UserDid, cancellationToken)
                    ? SpaceAccessDecision.Granted
                    : SpaceAccessDecision.Refuse(
                        SpaceAccessOutcome.UserNotAuthorized, "Not on the space's member list.");

            case ManagingAppPolicy managing:
                try
                {
                    var authorized = await _managingApp.CheckUserAccessAsync(
                        managing.ManagingApp, space.Uri, request.UserDid, request.AttestedClientId, cancellationToken);

                    return authorized
                        ? SpaceAccessDecision.Granted
                        : SpaceAccessDecision.Refuse(
                            SpaceAccessOutcome.UserNotAuthorized, "The managing app declined.");
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    // An unreachable managing app is a refusal, not a grant. Failing open here
                    // would turn every outage of the app into an open space.
                    _logger.LogWarning(
                        ex, "The managing app {App} for {Space} could not be reached.", managing.ManagingApp, space.Uri);

                    return SpaceAccessDecision.Refuse(
                        SpaceAccessOutcome.NotAuthorized, "The managing app could not be reached.");
                }

            default:
                return SpaceAccessDecision.Refuse(
                    SpaceAccessOutcome.NotAuthorized,
                    $"Unsupported user policy variant '{space.Policy.GetType().Name}'.");
        }
    }
}

/// <summary>
/// Calls <c>com.atproto.simplespace.checkUserAccess</c> on a space's managing app.
/// </summary>
public interface ISimpleSpaceManagingAppClient
{
    /// <summary>
    /// Asks a managing app whether to authorize one credential request.
    /// </summary>
    /// <param name="managingApp">
    /// The managing app's service identifier: a DID with an optional service fragment.
    /// </param>
    /// <param name="space">The space being asked for.</param>
    /// <param name="userDid">The user the requesting application is acting for.</param>
    /// <param name="clientId">The attested client ID, when the request attested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> CheckUserAccessAsync(
        string managingApp, SpaceUri space, string userDid, string? clientId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISimpleSpaceManagingAppClient"/>: resolves the managing app's endpoint
/// from its DID document and calls it with service auth.
/// </summary>
/// <remarks>
/// This call sits on the critical path of a credential request, so it wants a tight timeout on
/// the <see cref="HttpClient"/> handed to it — a slow managing app should refuse quickly rather
/// than hold the exchange open.
/// </remarks>
public sealed class SimpleSpaceManagingAppClient : ISimpleSpaceManagingAppClient
{
    private readonly ISpaceDidDocumentResolver _resolver;
    private readonly ServiceAuthGenerator _serviceAuth;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="resolver">Resolves the managing app's endpoint.</param>
    /// <param name="serviceAuth">Signs the outbound service auth token as this authority.</param>
    /// <param name="httpClient">The client used for the call.</param>
    public SimpleSpaceManagingAppClient(
        ISpaceDidDocumentResolver resolver, ServiceAuthGenerator serviceAuth, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(serviceAuth);
        ArgumentNullException.ThrowIfNull(httpClient);

        _resolver = resolver;
        _serviceAuth = serviceAuth;
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<bool> CheckUserAccessAsync(
        string managingApp,
        SpaceUri space,
        string userDid,
        string? clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managingApp);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDid);

        var (did, fragment) = SpaceAuthority.ParseServiceIdentifier(managingApp);
        var document = await _resolver.ResolveAsync(did, cancellationToken);
        var endpoint = SpaceAuthority.GetServiceEndpoint(document, fragment)
            ?? throw new InvalidOperationException($"Managing app '{managingApp}' resolves to no endpoint.");

        var query = $"?space={Uri.EscapeDataString(space.Value)}&did={Uri.EscapeDataString(userDid)}" +
                    (clientId is null ? string.Empty : $"&clientId={Uri.EscapeDataString(clientId)}");
        var url = new Uri(new Uri(endpoint.TrimEnd('/') + "/"), $"xrpc/{SpaceNsids.CheckUserAccess}{query}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _serviceAuth.CreateToken(did, SpaceNsids.CheckUserAccess));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Managing app '{managingApp}' answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var body = await response.Content.ReadFromJsonAsync<CheckUserAccessResponse>(cancellationToken);
        return body?.Authorized ?? false;
    }
}
