namespace ATProtoNet.Http;

/// <summary>
/// Builds <c>atproto-proxy</c> header values for service proxying through a PDS.
/// <para>
/// When making XRPC requests through a PDS, the <c>atproto-proxy</c> header tells the PDS
/// which backend service to forward the request to. The header value is a DID with a
/// service endpoint fragment identifier (e.g., <c>did:web:api.bsky.app#bsky_appview</c>).
/// </para>
/// </summary>
/// <remarks>
/// See: <see href="https://atproto.com/specs/xrpc#service-proxying">AT Protocol Service Proxying</see>
/// </remarks>
public static class ServiceProxy
{
    // ─── Well-known service endpoint identifiers ────────────────────────

    /// <summary>Bluesky App View service identifier.</summary>
    public const string BskyAppView = "#bsky_appview";

    /// <summary>Bluesky Chat service identifier.</summary>
    public const string BskyChat = "#bsky_chat";

    /// <summary>AT Protocol Labeler service identifier.</summary>
    public const string AtProtoLabeler = "#atproto_labeler";

    /// <summary>AT Protocol PDS service identifier.</summary>
    public const string AtProtoPds = "#atproto_pds";

    // ─── Well-known service DIDs ────────────────────────────────────────

    /// <summary>Bluesky App View DID (<c>did:web:api.bsky.app</c>).</summary>
    public const string BskyAppViewDid = "did:web:api.bsky.app";

    /// <summary>Bluesky Chat service DID (<c>did:web:api.bsky.chat</c>).</summary>
    public const string BskyChatDid = "did:web:api.bsky.chat";

    /// <summary>
    /// Pre-built proxy header value for the Bluesky App View:
    /// <c>did:web:api.bsky.app#bsky_appview</c>.
    /// </summary>
    public const string BskyAppViewHeader = $"{BskyAppViewDid}{BskyAppView}";

    /// <summary>
    /// Pre-built proxy header value for the Bluesky Chat service:
    /// <c>did:web:api.bsky.chat#bsky_chat</c>.
    /// </summary>
    public const string BskyChatHeader = $"{BskyChatDid}{BskyChat}";

    /// <summary>
    /// Constructs an <c>atproto-proxy</c> header value from a service DID and service endpoint identifier.
    /// </summary>
    /// <param name="did">The DID of the target service (e.g., <c>did:web:api.bsky.app</c>).</param>
    /// <param name="serviceId">The service endpoint fragment identifier (e.g., <c>#bsky_appview</c>).
    /// The leading <c>#</c> is added automatically if not present.</param>
    /// <returns>The proxy header value (e.g., <c>did:web:api.bsky.app#bsky_appview</c>).</returns>
    public static string Build(string did, string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        if (!serviceId.StartsWith('#'))
            serviceId = $"#{serviceId}";

        return $"{did}{serviceId}";
    }
}
