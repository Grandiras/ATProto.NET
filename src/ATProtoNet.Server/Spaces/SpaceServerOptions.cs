using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
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
    public Did? ServiceDid { get; set; }

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
    /// <c>jti</c> occupies <see cref="IJtiReplayStore"/>, which evicts an entry only once the
    /// token it guards has expired anyway.
    /// </remarks>
    public TimeSpan MaxSingleUseTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The cache in front of the DID documents this service verifies tokens against. Defaults to
    /// a hard 5-minute lifetime: <see cref="DidCacheOptions.StaleAfter"/> and
    /// <see cref="DidCacheOptions.ExpireAfter"/> are both 5 minutes.
    /// </summary>
    /// <remarks>
    /// <para>Every document here backs a signature check on a credential, a delegation token or a
    /// service auth token, and a cached document is also how long a key its owner has rotated
    /// away — perhaps because it leaked — keeps verifying. A space server rarely follows the
    /// firehose's <c>#identity</c> events, so its cache lifetime is what bounds that window: with
    /// the default, a rotated-out key is never accepted more than 5 minutes after it was
    /// fetched. The SDK's general defaults (an hour, then a day) suit a firehose consumer that
    /// does follow those events.</para>
    /// <para>Setting <see cref="DidCacheOptions.ExpireAfter"/> above
    /// <see cref="DidCacheOptions.StaleAfter"/> opts into stale-while-revalidate: a document past
    /// <see cref="DidCacheOptions.StaleAfter"/> is still served while a background fetch replaces
    /// it. That takes the fetch off the request path once a document goes stale, at the cost of
    /// accepting the old key for requests that arrive before the fetch completes — on an idle
    /// server, for as long as <see cref="DidCacheOptions.ExpireAfter"/>. A token that fails
    /// against a cached key is retried once against a refreshed document either way.</para>
    /// <para>Fetching follows the registered <see cref="IdentityResolverOptions"/> (see
    /// <c>AddAtProtoIdentity</c>): the PLC directory, the fetch policy and its development
    /// opt-out. The resolver is registered under <see cref="SpaceServerExtensions.DidResolverKey"/>;
    /// register your own <see cref="IDidResolver"/> under that key to replace it.</para>
    /// </remarks>
    public DidCacheOptions DidCache { get; set; } = new()
    {
        StaleAfter = TimeSpan.FromMinutes(5),
        ExpireAfter = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// How many verified space credentials a repo host remembers, so that a credential presented
    /// again skips its signature check. Defaults to 10,000; zero turns the cache off.
    /// </summary>
    /// <remarks>
    /// <para>A syncer presents the same credential on every read for its two-hour life, and
    /// verifying its signature is most of what a read costs before any data is touched. An entry
    /// lasts until the credential expires. Expiry, the requested space, the DPoP proof and the
    /// authority's key are still checked on every request — the key against the DID document
    /// <see cref="DidCache"/> currently serves, so a rotated key stops verifying cached credentials
    /// exactly when it stops verifying new ones.</para>
    /// <para>An entry holds the parsed credential, about 3 KB, so the default bounds the cache
    /// near 30 MB. When full, the least recently used entry is evicted; one authority may hold at
    /// most a quarter of the entries, since any DID can mint credentials for its own spaces.</para>
    /// </remarks>
    public int VerifiedCredentialCacheCapacity { get; set; } = 10_000;

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
    /// The verification-method fragment a space authority's credentials are signed with, sent as
    /// their <c>kid</c>: <see cref="SpaceAuthority.SigningKeyId"/> (<c>#atproto_space</c>) or
    /// <c>#atproto</c>. When omitted, <c>#atproto</c>, which is what lets an ordinary account be
    /// an authority with no DID-document change.
    /// </summary>
    /// <remarks>
    /// A reader verifies a credential against exactly the entry its <c>kid</c> names, with no
    /// fallback, so an authority that signs with a dedicated <c>#atproto_space</c> key must say
    /// so here.
    /// </remarks>
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
    /// How long the keys a client publishes — its <c>client-metadata.json</c> and JWKS — are
    /// remembered for verifying its attestations. Defaults to five minutes;
    /// <see cref="TimeSpan.Zero"/> fetches them for every attestation.
    /// </summary>
    /// <remarks>
    /// An attestation that names a key the remembered set lacks, or does not verify against it,
    /// is checked once more against a fresh fetch, so a client's key rotation takes effect
    /// without waiting for the entry to expire. A key the client <em>removes</em> from its JWKS,
    /// though, keeps verifying its attestations here for up to this long. A client's metadata is
    /// fetched at most once every 30 seconds, whether the fetch succeeds or fails.
    /// </remarks>
    public TimeSpan ClientMetadataCacheLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to log at startup that the space server is still tracking single-use tokens in
    /// the in-process default <see cref="InMemoryJtiReplayStore"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>The default is per-process, which is a correctness gap rather than a performance one
    /// once a second instance exists: it catches a replayed single-use token only on the instance
    /// that saw the original.</para>
    /// <para>Set this to <see langword="false"/> where the default is the intended choice, as in
    /// a test host or a development server.</para>
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
    /// How long a single-use token's <c>jti</c> must stay in the <see cref="IJtiReplayStore"/>:
    /// until the token stops being accepted, which the clock skew of its expiry check puts past
    /// its <c>exp</c>.
    /// </summary>
    /// <param name="expiresAt">The token's <c>exp</c>.</param>
    /// <remarks>
    /// Delegation tokens and client attestations are checked for expiry by
    /// <see cref="SpaceToken.IsExpired"/> with <see cref="SpaceTokens.DefaultClockSkew"/>, and
    /// service auth with <see cref="ClockSkew"/>; the larger of the two covers either.
    /// </remarks>
    internal DateTimeOffset ReplayRetention(DateTimeOffset expiresAt) =>
        expiresAt + (ClockSkew > SpaceTokens.DefaultClockSkew ? ClockSkew : SpaceTokens.DefaultClockSkew);

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
