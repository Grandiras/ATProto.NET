namespace ATProtoNet.Identity;

/// <summary>
/// Configuration for the SDK's DID and handle resolvers.
/// </summary>
public sealed class IdentityResolverOptions
{
    /// <summary>The PLC directory the SDK resolves <c>did:plc</c> against by default.</summary>
    public static readonly Uri DefaultPlcDirectoryUrl = new("https://plc.directory/");

    /// <summary>The DNS-over-HTTPS endpoint the SDK queries for handle TXT records by default.</summary>
    public static readonly Uri DefaultDnsOverHttpsUrl = new("https://dns.google/resolve");

    /// <summary>
    /// The PLC directory <c>did:plc</c> resolves against. Defaults to
    /// <see cref="DefaultPlcDirectoryUrl"/>.
    /// </summary>
    public Uri PlcDirectoryUrl { get; set; } = DefaultPlcDirectoryUrl;

    /// <summary>
    /// The DNS-over-HTTPS endpoint queried for a handle's <c>_atproto</c> TXT record, speaking the
    /// JSON API (<c>?name=…&amp;type=TXT</c>) that Google and Cloudflare serve. Defaults to
    /// <see cref="DefaultDnsOverHttpsUrl"/>.
    /// </summary>
    /// <remarks>
    /// .NET has no TXT lookup of its own, so DNS resolution of a handle goes through this
    /// endpoint, which therefore learns every handle resolved. Point it at a resolver you run or
    /// trust (<c>https://cloudflare-dns.com/dns-query</c> also works), or set it to
    /// <see langword="null"/> to resolve handles over HTTPS only.
    /// </remarks>
    public Uri? DnsOverHttpsUrl { get; set; } = DefaultDnsOverHttpsUrl;

    /// <summary>
    /// How long one DID document fetch may take, including reading the body. Defaults to five
    /// seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The budget shared by the concurrent DNS and HTTPS lookups of one handle resolution.
    /// Defaults to five seconds.
    /// </summary>
    /// <remarks>
    /// A handle's domain may be parked or firewalled and drop packets on port 443; the budget
    /// turns that into "no answer" after a few seconds instead of holding the caller.
    /// </remarks>
    public TimeSpan HandleResolutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The largest DID document accepted, in bytes. Defaults to 64 KiB.</summary>
    public int MaxDidDocumentBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// The development opt-out from the identity fetch policy. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>By default every identity fetch is HTTPS-only, a <c>did:web</c> carries no port, and
    /// no connection reaches a loopback, private, link-local or CGNAT address, whatever name led
    /// there. Setting this lifts all three for a local PDS, a private PLC mirror or a test
    /// network: plain HTTP is accepted, <c>did:web:localhost%3A2583</c> resolves over
    /// <c>http://</c>, <c>.test</c> handles resolve, and any address is reachable.</para>
    /// <para>Never set it on a service that resolves identifiers it receives from other parties:
    /// that is exactly the request forgery the policy exists to stop. To reach one trusted
    /// private PLC mirror while keeping the policy for everything else, give a
    /// <see cref="PlcClient"/> your own <see cref="HttpClient"/> instead.</para>
    /// </remarks>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>The DID document cache used by <see cref="IdentityResolver.CreateDefault"/> and DI registration.</summary>
    public DidCacheOptions Cache { get; set; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(PlcDirectoryUrl, nameof(PlcDirectoryUrl));
        RequirePositive(RequestTimeout, nameof(RequestTimeout));
        RequirePositive(HandleResolutionTimeout, nameof(HandleResolutionTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDidDocumentBytes, 1024, nameof(MaxDidDocumentBytes));
        ArgumentNullException.ThrowIfNull(Cache, nameof(Cache));
        Cache.Validate();
    }

    private static void RequirePositive(TimeSpan value, string name)
    {
        if (value != Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Must be positive or Timeout.InfiniteTimeSpan.");
    }
}

/// <summary>
/// Configuration for a <see cref="CachingDidResolver"/>.
/// </summary>
/// <remarks>
/// The defaults match the reference implementation's (<c>@atproto/identity</c>): a document is
/// served as is for an hour, served while being refreshed in the background for up to a day,
/// and refetched before use after that. A consumer of the firehose should also invalidate on
/// <c>#identity</c> events (<see cref="Streaming.TypedFirehoseConsumer"/> does), which is what
/// makes a key rotation take effect at once rather than within the hour.
/// </remarks>
public sealed class DidCacheOptions
{
    /// <summary>The most documents held in memory. The least recently used goes first. Defaults to 10,000.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    /// How long a document is served without being refreshed. After that it is still served, and
    /// refreshed in the background. Defaults to one hour.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a document may be served at all. After that it is refetched before use. Defaults
    /// to one day.
    /// </summary>
    public TimeSpan ExpireAfter { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long a failed resolution is remembered, so a DID that does not resolve (or a caller
    /// naming bogus DIDs) does not cost a fetch per request. Defaults to one minute.
    /// </summary>
    public TimeSpan FailureTtl { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The most failed resolutions remembered, the least recently used going first. Defaults to
    /// 1,000.
    /// </summary>
    /// <remarks>
    /// Failures are held apart from documents, so a stream of DIDs that do not resolve — which
    /// anyone can send — evicts other failures, never the documents of DIDs that do.
    /// </remarks>
    public int FailureCapacity { get; set; } = 1_000;

    /// <summary>
    /// The least time between two refetches forced by <see cref="IDidResolver.RefreshAsync"/> for
    /// the same DID, counted from the last fetch attempt, failed or not. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// A consumer refreshes when a signature fails against a cached key, and anyone can send a
    /// bad signature naming any DID; without a floor each one would cost a directory request.
    /// <see cref="IDidResolver.InvalidateAsync"/> is not limited.
    /// </remarks>
    public TimeSpan MinRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether dependency-injection registration backs the cache with the registered
    /// <c>IDistributedCache</c>, so instances share resolved documents. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool UseDistributedCache { get; set; }

    /// <summary>The key prefix for documents in a distributed cache. Defaults to <c>atproto:did:</c>.</summary>
    public string DistributedCacheKeyPrefix { get; set; } = "atproto:did:";

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1, nameof(Capacity));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero, nameof(StaleAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(ExpireAfter, StaleAfter, nameof(ExpireAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureTtl, TimeSpan.Zero, nameof(FailureTtl));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureCapacity, 1, nameof(FailureCapacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(MinRefreshInterval, TimeSpan.Zero, nameof(MinRefreshInterval));
        ArgumentNullException.ThrowIfNull(DistributedCacheKeyPrefix, nameof(DistributedCacheKeyPrefix));
    }
}
