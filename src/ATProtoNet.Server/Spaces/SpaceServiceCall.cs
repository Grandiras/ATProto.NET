using System.Net.Http.Headers;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

// An outbound XRPC call from the space server to another service, signed with service auth.
internal static class SpaceServiceCall
{
    // The URL of nsid at the service service identifies (did or did#fragment), or null when its DID
    // document publishes no usable endpoint for it.
    //
    // A #atproto_space_host fragment falls back to #atproto_pds, so an authority on an ordinary PDS,
    // which publishes no such entry, is still reached.
    //
    // Throws FormatException: The service publishes a malformed #atproto_space_host entry.
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

    // Sends request with a service auth token for audience and nsid.
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
