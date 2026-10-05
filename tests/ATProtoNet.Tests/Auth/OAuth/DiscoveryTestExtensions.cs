using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

// The discovery steps the tests drive on their own, composed from the pieces OAuthClient uses.
internal static class DiscoveryTestExtensions
{
    public static async Task ResolveFromIdentifierAsync(this AuthorizationServerDiscovery discovery, string identifier)
    {
        var identity = await discovery.ResolveIdentityAsync(AuthorizationServerDiscovery.ParseIdentifier(identifier), default);
        var pds = identity.PdsEndpoint
            ?? throw new OAuthException($"DID document for '{identity.Did}' does not contain an atproto PDS service.", "pds_not_found");
        await discovery.ResolveAuthorizationServerAsync(pds.OriginalString);
    }

    public static Task<ProtectedResourceMetadata> FetchProtectedResourceMetadataAsync(
        this AuthorizationServerDiscovery discovery, string pdsUrl) =>
        discovery.FetchMetadataAsync<ProtectedResourceMetadata>(pdsUrl, ".well-known/oauth-protected-resource", bypassCache: false, default);

    public static Task<AuthorizationServerMetadata> FetchAuthorizationServerMetadataAsync(
        this AuthorizationServerDiscovery discovery, string issuer) =>
        discovery.FetchMetadataAsync<AuthorizationServerMetadata>(issuer, ".well-known/oauth-authorization-server", bypassCache: false, default);
}
