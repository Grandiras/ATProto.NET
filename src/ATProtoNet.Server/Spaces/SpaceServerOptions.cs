using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Configuration for the space server: who this service is, and how strictly it evaluates the
/// tokens presented to it.
/// </summary>
/// <remarks>
/// A single service can act as a space authority, a repo host, or both. Only
/// <see cref="ServiceDid"/> is always required; the authority half additionally needs a
/// credential signing key, which is supplied to
/// <see cref="SpaceCredentialIssuer"/> rather than held here.
/// </remarks>
public sealed class SpaceServerOptions
{
    /// <summary>
    /// The DID of this service — the space authority DID when acting as one, and the issuer of
    /// the service auth on outbound write notifications.
    /// </summary>
    public string? ServiceDid { get; set; }

    /// <summary>
    /// The externally reachable base URL of this service, e.g. <c>https://pds.example.com</c>.
    /// </summary>
    /// <remarks>
    /// <para>A DPoP proof names the URL it was minted for in its <c>htu</c>, and the verifier
    /// compares that against the request <em>as received</em>. Behind a reverse proxy that is
    /// not what the request line says — the scheme is <c>http</c> and the host is an internal
    /// name — so either the forwarded headers must be applied before the endpoint runs
    /// (<c>UseForwardedHeaders</c>), or this must be set to the URL clients actually address.
    /// Setting it is the more reliable of the two, because it does not depend on trusting a
    /// header.</para>
    /// <para>Only the scheme, host, and port are taken from it; the path comes from the
    /// request.</para>
    /// </remarks>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// How far a DPoP proof's <c>iat</c> may sit from this service's clock. Defaults to five
    /// minutes, the usual allowance in RFC 9449 deployments.
    /// </summary>
    /// <remarks>
    /// This doubles as the window a consumed proof <c>jti</c> is remembered for: outside it a
    /// replayed proof is rejected on its <c>iat</c> anyway, so the replay store need not hold
    /// the identifier any longer.
    /// </remarks>
    public TimeSpan ProofLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Tolerance applied to token expiry checks. Defaults to
    /// <see cref="SpaceTokens.DefaultClockSkew"/>.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = SpaceTokens.DefaultClockSkew;

    /// <summary>
    /// The furthest ahead of now the <c>exp</c> of a single-use token may sit — a delegation
    /// token, a client attestation, or a service auth token. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// All three are minted short-lived (60 seconds by
    /// <see cref="SpaceTokens.DefaultShortLifetime"/> and by
    /// <see cref="ATProtoNet.Auth.ServiceAuthGenerator"/>, which itself refuses to exceed five
    /// minutes), but the <c>exp</c> on an inbound one is whatever its signer chose. Bounding it
    /// bounds two things: how long a captured token stays replayable at all, and how long its
    /// <c>jti</c> occupies <see cref="ISpaceReplayStore"/>, which evicts an entry only once the
    /// token it guards has expired anyway.
    /// </remarks>
    public TimeSpan MaxSingleUseTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a resolved DID document is reused before being re-fetched. Defaults to five minutes.</summary>
    public TimeSpan DidDocumentCacheLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The lifetime of the credentials this authority issues. Defaults to
    /// <see cref="SpaceTokens.DefaultCredentialLifetime"/> (two hours).
    /// </summary>
    public TimeSpan CredentialLifetime { get; set; } = SpaceTokens.DefaultCredentialLifetime;

    /// <summary>
    /// How long a <c>registerNotify</c> registration lasts before a syncer must renew it.
    /// Defaults to seven days.
    /// </summary>
    public TimeSpan NotifyRegistrationLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The verification-method fragment a space authority's credentials are signed with. When
    /// omitted, <see cref="SpaceAuthority.SigningKeyId"/> is used if the authority publishes it
    /// and <c>#atproto</c> otherwise, which is what lets an ordinary account be an authority
    /// with no DID-document change.
    /// </summary>
    public string? CredentialKeyId { get; set; }

    /// <summary>
    /// Maximum size of a fetched <c>client-metadata.json</c> or JWKS document, in bytes.
    /// Defaults to 256 KiB.
    /// </summary>
    /// <remarks>
    /// Client attestation verification fetches a document from a URL the <em>attestation</em>
    /// chose, so the fetch is attacker-directed and needs a ceiling.
    /// </remarks>
    public int MaxClientMetadataBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Whether to log at startup that the space server is still using the in-process default
    /// stores. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>The defaults are per-process, which is a correctness gap rather than a performance
    /// one once a second instance exists: <see cref="InMemorySpaceReplayStore"/> catches a
    /// replayed single-use token only on the instance that saw the original, and
    /// <see cref="InMemorySimpleSpaceStore"/> loses a member list — which nothing on the network
    /// republishes — on every restart.</para>
    /// <para>Set this to <see langword="false"/> where the defaults are the intended choice, as
    /// in a test host or a development server.</para>
    /// </remarks>
    public bool WarnOnInMemoryStores { get; set; } = true;

    /// <summary>
    /// Reports whether a single-use token expiring at <paramref name="expiresAt"/> sits inside
    /// <see cref="MaxSingleUseTokenLifetime"/>, allowing for <see cref="ClockSkew"/>.
    /// </summary>
    /// <param name="expiresAt">The token's <c>exp</c>.</param>
    /// <param name="now">The current time.</param>
    internal bool IsWithinSingleUseWindow(DateTimeOffset expiresAt, DateTimeOffset now) =>
        expiresAt <= now + MaxSingleUseTokenLifetime + ClockSkew;

    /// <summary>
    /// Resolves this service's public request URI, honouring <see cref="PublicBaseUrl"/>.
    /// </summary>
    /// <param name="requestScheme">The scheme the request arrived on.</param>
    /// <param name="requestHost">The host the request named.</param>
    /// <param name="path">The request path, including any path base.</param>
    /// <returns>The absolute URL a DPoP proof's <c>htu</c> is compared against.</returns>
    public string BuildRequestUri(string requestScheme, string requestHost, string path)
    {
        if (string.IsNullOrEmpty(PublicBaseUrl))
            return $"{requestScheme}://{requestHost}{path}";

        var root = PublicBaseUrl.TrimEnd('/');
        return $"{root}{path}";
    }
}
