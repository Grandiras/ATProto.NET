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
/// <para>A space carries three policies. For a <b>read</b> — a credential request — two
/// perimeters are evaluated, and a credential is minted only when both pass. The <b>read
/// policy</b> asks whether this account may read the space — <see cref="PublicPolicy"/> admits
/// anyone, <see cref="MemberListPolicy"/> admits members whose read flag is set, and
/// <see cref="ManagingAppPolicy"/> asks the managing app per request, which is what enables
/// follower-gating, paid subscriptions, and join approvals without an app maintaining an
/// explicit list. The <b>app access policy</b> asks whether this application may act —
/// <see cref="OpenAppAccess"/> admits any, and <see cref="AllowListAppAccess"/> names the client
/// IDs it accepts.</para>
/// <para>For a <b>write</b> — a write notification from the writer's repo host — only the
/// <b>write policy</b> is evaluated, the same three ways, with the member's write flag in place
/// of its read flag. There is no app perimeter: the notification comes from a repo host, not an
/// application, and carries no attestation to judge. The space's owner is always admitted, to
/// read and to write, whatever the policies say.</para>
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

        switch (request.Access)
        {
            case SpaceAccessKind.Read:
                var app = EvaluateApp(space, request);
                if (!app.IsGranted)
                    return app;

                return await EvaluateUserAsync(space, space.ReadPolicy, request, cancellationToken);

            case SpaceAccessKind.Write:
                return await EvaluateUserAsync(space, space.WritePolicy, request, cancellationToken);

            default:
                return SpaceAccessDecision.Refuse(
                    SpaceAccessOutcome.NotAuthorized, $"Unknown access kind '{request.Access}'.");
        }
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
        SimpleSpaceRecord space,
        SimpleSpaceUserPolicy policy,
        SpaceAccessRequest request,
        CancellationToken cancellationToken)
    {
        // The owner is the only party who can reconfigure the space, so no policy may lock it out.
        if (string.Equals(space.Owner, request.UserDid, StringComparison.Ordinal))
            return SpaceAccessDecision.Granted;

        var write = request.Access == SpaceAccessKind.Write;

        switch (policy)
        {
            case PublicPolicy:
                return SpaceAccessDecision.Granted;

            case MemberListPolicy:
                var member = await _store.GetMemberAsync(space.Uri, request.UserDid, cancellationToken);
                if (member is null)
                {
                    return SpaceAccessDecision.Refuse(
                        SpaceAccessOutcome.UserNotAuthorized, "Not on the space's member list.");
                }

                return (write ? member.Write : member.Read)
                    ? SpaceAccessDecision.Granted
                    : SpaceAccessDecision.Refuse(
                        SpaceAccessOutcome.UserNotAuthorized,
                        write ? "A member without write access." : "A member without read access.");

            case ManagingAppPolicy managing:
                try
                {
                    // A write check has no app behind it, so there is no client ID to pass on.
                    var authorized = await _managingApp.CheckUserAccessAsync(
                        managing.ManagingApp,
                        space.Uri,
                        request.UserDid,
                        request.Access,
                        write ? null : request.AttestedClientId,
                        cancellationToken);

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
                    $"Unsupported user policy variant '{policy.GetType().Name}'.");
        }
    }
}

/// <summary>
/// Calls <c>com.atproto.simplespace.checkUserAccess</c> on a space's managing app.
/// </summary>
public interface ISimpleSpaceManagingAppClient
{
    /// <summary>
    /// Asks a managing app whether to authorize one user for one kind of access.
    /// </summary>
    /// <param name="managingApp">
    /// The managing app's service identifier: a DID with an optional service fragment.
    /// </param>
    /// <param name="space">The space being asked about.</param>
    /// <param name="userDid">The user asking to read, or the writer.</param>
    /// <param name="access">Whether this is a read (credential) check or a write check.</param>
    /// <param name="clientId">
    /// The attested client ID, when a read request attested. Always <see langword="null"/> for a
    /// write check.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> CheckUserAccessAsync(
        string managingApp, SpaceUri space, string userDid, SpaceAccessKind access, string? clientId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISimpleSpaceManagingAppClient"/>: resolves the managing app's endpoint
/// from its DID document and calls it with service auth.
/// </summary>
/// <remarks>
/// <para>The service auth token is addressed to the managing app's service identifier exactly as
/// the policy names it — <c>did:web:app.example.com#forum</c>, fragment and all — because that
/// is the audience a managing app verifies against.</para>
/// <para>A read check sits on the critical path of a credential request, so this wants a tight
/// timeout on the <see cref="HttpClient"/> handed to it — a slow managing app should refuse
/// quickly rather than hold the exchange open.</para>
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
        SpaceAccessKind access,
        string? clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managingApp);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDid);

        var accessValue = access switch
        {
            SpaceAccessKind.Read => SimpleSpaceAccess.Read,
            SpaceAccessKind.Write => SimpleSpaceAccess.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(access), access, "Unknown access kind."),
        };

        var (did, fragment) = SpaceAuthority.ParseServiceIdentifier(managingApp);
        var document = await _resolver.ResolveAsync(did, cancellationToken);
        var endpoint = SpaceAuthority.GetServiceEndpoint(document, fragment)
            ?? throw new InvalidOperationException($"Managing app '{managingApp}' resolves to no endpoint.");

        // The same failure as a missing endpoint, which the policy treats as a refusal.
        if (!Http.AtProtoHttp.TryNormalizeBaseUrl(endpoint, out var baseUrl))
            throw new InvalidOperationException($"Managing app '{managingApp}' resolves to an unusable endpoint '{endpoint}'.");

        // The Lexicon omits clientId for write checks, which have no app behind them.
        var query = $"?space={Uri.EscapeDataString(space.Value)}" +
                    $"&user={Uri.EscapeDataString(userDid)}" +
                    $"&access={accessValue}" +
                    (clientId is null || access == SpaceAccessKind.Write
                        ? string.Empty
                        : $"&clientId={Uri.EscapeDataString(clientId)}");
        var url = new Uri(baseUrl, $"xrpc/{SpaceNsids.CheckUserAccess}{query}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _serviceAuth.CreateToken(managingApp, SpaceNsids.CheckUserAccess));

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
