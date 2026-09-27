using System.Net.Http.Headers;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>An outbound XRPC call from the space server to another service, signed with service auth.</summary>
internal static class SpaceServiceCall
{
    /// <summary>
    /// The URL of <paramref name="nsid"/> at the service <paramref name="service"/> identifies
    /// (<c>did</c> or <c>did#fragment</c>), or <see langword="null"/> when its DID document
    /// publishes no usable endpoint for it.
    /// </summary>
    /// <remarks>
    /// A <c>#atproto_space_host</c> fragment falls back to <c>#atproto_pds</c>, so an authority on
    /// an ordinary PDS, which publishes no such entry, is still reached.
    /// </remarks>
    /// <exception cref="FormatException">The service publishes a malformed <c>#atproto_space_host</c> entry.</exception>
    public static async Task<Uri?> ResolveAsync(
        IDidResolver resolver, string service, Nsid nsid, CancellationToken cancellationToken)
    {
        var (did, fragment) = SpaceAuthority.ParseServiceIdentifier(service);
        var document = await resolver.ResolveOrRefuseAsync(did, refresh: false, cancellationToken).ConfigureAwait(false);
        var endpoint = SpaceAuthority.GetServiceEndpoint(document, fragment);

        return AtProtoHttp.TryNormalizeBaseUrl(endpoint?.OriginalString, out var baseUrl)
            ? new Uri(baseUrl, $"xrpc/{nsid}")
            : null;
    }

    /// <summary>Sends <paramref name="request"/> with a service auth token for <paramref name="audience"/> and <paramref name="nsid"/>.</summary>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        ServiceAuthGenerator signer,
        string audience,
        Nsid nsid,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signer.CreateToken(audience, nsid));
        return client.SendAsync(request, cancellationToken);
    }
}
