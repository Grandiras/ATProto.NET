using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>Configuration for the space server: who this service is, and how strictly it evaluates the tokens presented to it.</summary>
/// <remarks>
/// A space authority needs <see cref="ServiceDid"/> (checked when the host starts) and a
/// credential signing key, which is supplied to <see cref="SpaceCredentialIssuer"/> rather than
/// held here. A service that only verifies, or only hosts repos, can leave it unset.
/// </remarks>
public sealed class SpaceServerOptions
{
    /// <summary>This service's DID: the space authority's DID when acting as one, and the issuer of its outbound service auth.</summary>
    public Did? ServiceDid { get; set; }

    /// <summary>The externally reachable base URL of this service, e.g. <c>https://pds.example.com</c>.</summary>
    /// <remarks>
    /// A DPoP proof's <c>htu</c> is compared with the request as received, which behind a reverse
    /// proxy names an internal host. Set this to the URL clients address (only its scheme, host and
    /// port are used), or apply <c>UseForwardedHeaders</c>; this is the more reliable of the two,
    /// since it trusts no header.
    /// </remarks>
    public string? PublicBaseUrl { get; set; }

    /// <summary>How far a DPoP proof's <c>iat</c> may sit from this service's clock, and so how long its <c>jti</c> is remembered. Default: 5 minutes.</summary>
    public TimeSpan ProofLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Tolerance of the token expiry checks. Default: <see cref="SpaceTokens.DefaultClockSkew"/>.</summary>
    public TimeSpan ClockSkew { get; set; } = SpaceTokens.DefaultClockSkew;

    /// <summary>The furthest ahead of now a single-use token's <c>exp</c> may sit: a delegation token, a client attestation or a service auth token. Default: 5 minutes.</summary>
    /// <remarks>
    /// The <c>exp</c> is the signer's choice. Bounding it bounds how long a captured token stays
    /// replayable, and how long its <c>jti</c> occupies the <see cref="IJtiReplayStore"/>.
    /// </remarks>
    public TimeSpan MaxSingleUseTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The cache in front of the DID documents this service verifies tokens against. Default: a hard 5-minute lifetime.</summary>
    /// <remarks>
    /// <para>A cached document is how long a key its owner has rotated away keeps verifying. A
    /// space server rarely follows the firehose's <c>#identity</c> events, so this lifetime bounds
    /// that window; the SDK's general defaults (an hour, then a day) suit a consumer that does.
    /// Setting <see cref="DidCacheOptions.ExpireAfter"/> above <see cref="DidCacheOptions.StaleAfter"/>
    /// serves a stale document while a background fetch replaces it, accepting the old key until
    /// then. A token that fails against a cached key is retried once against a refreshed
    /// document either way.</para>
    /// <para>Fetching follows the registered <see cref="IdentityResolverOptions"/>. The resolver is
    /// registered under <see cref="SpaceServerExtensions.DidResolverKey"/>; register your own under
    /// that key to replace it.</para>
    /// </remarks>
    public DidCacheOptions DidCache { get; set; } = new()
    {
        StaleAfter = TimeSpan.FromMinutes(5),
        ExpireAfter = TimeSpan.FromMinutes(5),
    };

    /// <summary>The lifetime of the credentials this authority issues. Default: <see cref="SpaceTokens.DefaultCredentialLifetime"/> (two hours).</summary>
    public TimeSpan CredentialLifetime { get; set; } = SpaceTokens.DefaultCredentialLifetime;

    /// <summary>How long a <c>registerNotify</c> registration lasts before it must be renewed. Default: 7 days.</summary>
    public TimeSpan NotifyRegistrationLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The verification method this authority's credentials are signed with, sent as their <c>kid</c>: <see cref="SpaceAuthority.SigningKeyId"/> (<c>#atproto_space</c>) or, when unset, <c>#atproto</c>, which lets an ordinary account be an authority.</summary>
    /// <remarks>
    /// A reader verifies a credential against exactly the entry its <c>kid</c> names, so an
    /// authority signing with a dedicated <c>#atproto_space</c> key must say so here.
    /// </remarks>
    public string? CredentialKeyId { get; set; }

    // Cache bounds and fetch limits (see SpaceCredentialVerifier and SpaceClientAttestationVerifier),
    // settable for tests.
    internal int VerifiedCredentialCacheCapacity { get; set; } = 10_000;

    internal int MaxClientMetadataBytes { get; set; } = 256 * 1024;

    internal TimeSpan ClientMetadataCacheLifetime { get; set; } = TimeSpan.FromMinutes(5);

    // Whether a single-use token's exp sits inside MaxSingleUseTokenLifetime, allowing for ClockSkew.
    internal bool IsWithinSingleUseWindow(DateTimeOffset expiresAt, DateTimeOffset now) =>
        expiresAt <= now + MaxSingleUseTokenLifetime + ClockSkew;

    // How long a single-use token's jti must stay in the IJtiReplayStore: until the token stops being
    // accepted. Delegation tokens and client attestations are checked with SpaceTokens.DefaultClockSkew
    // and service auth with ClockSkew; the larger covers either.
    internal DateTimeOffset ReplayRetention(DateTimeOffset expiresAt) =>
        expiresAt + (ClockSkew > SpaceTokens.DefaultClockSkew ? ClockSkew : SpaceTokens.DefaultClockSkew);

    // The absolute URL a DPoP proof's htu is compared against, honouring PublicBaseUrl.
    internal string BuildRequestUri(string requestScheme, string requestHost, string path) =>
        string.IsNullOrEmpty(PublicBaseUrl)
            ? $"{requestScheme}://{requestHost}{path}"
            : $"{PublicBaseUrl.TrimEnd('/')}{path}";
}
