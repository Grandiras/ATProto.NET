using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Lexicon.Com.AtProto.Lexicon;

/// <summary>
/// Resolves an NSID to the Lexicon schema its authority publishes.
/// </summary>
/// <remarks>
/// <para><see cref="LexiconResolver"/> resolves over the network and verifies what it fetches;
/// <see cref="LexiconClient"/> asks a service (<c>com.atproto.lexicon.resolveLexicon</c>) and
/// trusts its answer; <see cref="CachingLexiconResolver"/> caches either.</para>
/// <para>Every implementation reports failure as <see cref="LexiconResolutionException"/>.</para>
/// </remarks>
public interface ILexiconResolver
{
    /// <summary>
    /// Resolves an NSID to its published schema.
    /// </summary>
    /// <param name="nsid">The NSID of the schema.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema, whose <see cref="LexiconSchemaRecord.Id"/> is <paramref name="nsid"/>.</returns>
    /// <exception cref="LexiconResolutionException">The schema could not be resolved.</exception>
    Task<ResolvedLexicon> ResolveAsync(Nsid nsid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops anything cached for an NSID, so its next resolution fetches it afresh.
    /// </summary>
    /// <param name="nsid">The NSID whose schema changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// A consumer of the firehose can call it when it sees a <c>com.atproto.lexicon.schema</c>
    /// record change. A resolver that caches nothing has nothing to drop.
    /// </remarks>
    Task InvalidateAsync(Nsid nsid, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Resolves Lexicon schemas as the Lexicon specification describes: the authority's
/// <c>_lexicon</c> DNS TXT record names a DID, the DID document names a PDS and a signing key, and
/// the schema is the <c>com.atproto.lexicon.schema</c> record keyed by the NSID in that repository,
/// fetched with its proof and verified against the signing key.
/// </summary>
/// <remarks>
/// <para>Resolution is not hierarchical: <c>app.example.feed.post</c> is looked up at
/// <c>_lexicon.feed.example.app</c> and nowhere else, and the record must name exactly one DID.
/// The record is fetched with <c>com.atproto.sync.getRecord</c> and checked with
/// <see cref="RecordProof"/>, as the reference implementation (<c>@atproto/lex-resolver</c>) does,
/// so a PDS cannot hand out a schema its account never committed.</para>
/// <para>The DNS query goes to <see cref="IdentityResolverOptions.DnsOverHttpsUrl"/>, and every
/// fetch runs under the identity fetch policy (<see cref="IdentityResolverOptions.AllowPrivateNetworks"/>):
/// the authority and its PDS come from DNS records and DID documents anyone can publish. Nothing
/// is cached; wrap it in a <see cref="CachingLexiconResolver"/>.</para>
/// </remarks>
public sealed class LexiconResolver : ILexiconResolver, IDisposable
{
    private readonly IDidResolver _didResolver;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IdentityResolverOptions _options;
    private readonly Uri? _dnsOverHttpsUrl;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a resolver with its own client under the SDK's identity fetch policy.
    /// </summary>
    /// <param name="didResolver">Resolves authority DIDs; a <see cref="CachingDidResolver"/> in most applications.</param>
    /// <param name="options">
    /// The DNS-over-HTTPS endpoint, the fetch policy and the per-request timeout. Defaults apply when
    /// omitted.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public LexiconResolver(IDidResolver didResolver, IdentityResolverOptions? options = null, ILogger? logger = null)
        : this(didResolver, null, options, logger, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a resolver that sends its requests through <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="didResolver">Resolves authority DIDs.</param>
    /// <param name="httpClient">
    /// The client to use, which the caller owns. Its handler is used as is: the connection-level
    /// address check applies only to the SDK's own handler.
    /// </param>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public LexiconResolver(
        IDidResolver didResolver, HttpClient httpClient, IdentityResolverOptions? options = null, ILogger? logger = null)
        : this(didResolver, httpClient ?? throw new ArgumentNullException(nameof(httpClient)), options, logger, ownsHttpClient: false)
    {
    }

    private LexiconResolver(
        IDidResolver didResolver, HttpClient? httpClient, IdentityResolverOptions? options, ILogger? logger, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(didResolver);

        _didResolver = didResolver;
        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        _dnsOverHttpsUrl = _options.DnsOverHttpsUrl is { } doh
            ? IdentityNetworkPolicy.ValidateServiceUrl(doh, _options.AllowPrivateNetworks, nameof(options))
            : null;
        _ownsHttpClient = ownsHttpClient;
        _httpClient = httpClient ?? IdentityNetworkPolicy.CreateClient(_options.AllowPrivateNetworks);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// The DNS name whose TXT record names the authority of <paramref name="nsid"/>:
    /// <c>_lexicon.</c> followed by the NSID's domain authority, its segments reversed and the name
    /// dropped (<c>app.example.feed.post</c> → <c>_lexicon.feed.example.app</c>).
    /// </summary>
    /// <param name="nsid">The NSID.</param>
    /// <returns>The DNS name, in lowercase.</returns>
    public static string GetDnsName(Nsid nsid)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        var segments = nsid.Authority.Split('.');
        Array.Reverse(segments);
        return "_lexicon." + string.Join('.', segments).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public async Task<ResolvedLexicon> ResolveAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        var authority = await ResolveAuthorityAsync(nsid, cancellationToken).ConfigureAwait(false);
        return await ResolveAsync(nsid, authority, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks up the DID that publishes the schemas of <paramref name="nsid"/>'s authority, from the
    /// <c>_lexicon</c> TXT record at <see cref="GetDnsName"/>.
    /// </summary>
    /// <param name="nsid">The NSID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authority's DID.</returns>
    /// <exception cref="LexiconResolutionException">
    /// Thrown with <see cref="LexiconResolutionErrorKind.AuthorityNotFound"/> when the name has no
    /// <c>did=</c> TXT record, more than one, or one that is not a DID; with
    /// <see cref="LexiconResolutionErrorKind.ResolutionFailed"/> when the query fails or DNS
    /// lookups are disabled.
    /// </exception>
    public async Task<Did> ResolveAuthorityAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        var name = GetDnsName(nsid);
        if (_dnsOverHttpsUrl is null)
        {
            throw new LexiconResolutionException(
                $"Cannot look up {name}: DNS-over-HTTPS is disabled ({nameof(IdentityResolverOptions)}.{nameof(IdentityResolverOptions.DnsOverHttpsUrl)} is null).",
                nsid, LexiconResolutionErrorKind.ResolutionFailed);
        }

        IReadOnlyList<string>? records;
        try
        {
            records = await DnsTxtLookup.QueryAsync(
                _httpClient, _dnsOverHttpsUrl, name, _options.RequestTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DidResolutionException ex)
        {
            throw new LexiconResolutionException(
                $"Looking up {name} failed: {ex.Message}", nsid, LexiconResolutionErrorKind.ResolutionFailed, ex);
        }

        if (records is null)
        {
            throw new LexiconResolutionException(
                $"Looking up {name} failed: the DNS-over-HTTPS endpoint answered with an error.",
                nsid, LexiconResolutionErrorKind.ResolutionFailed);
        }

        // Exactly one did= record, as in the reference implementation: several are not a choice
        // to make, even when they agree.
        var values = records.Where(r => r.StartsWith("did=", StringComparison.Ordinal)).ToList();
        if (values.Count != 1)
        {
            throw new LexiconResolutionException(
                values.Count == 0
                    ? $"{name} has no did= TXT record, so {nsid.Authority} publishes no Lexicons."
                    : $"{name} has {values.Count} did= TXT records; a Lexicon authority names exactly one DID.",
                nsid, LexiconResolutionErrorKind.AuthorityNotFound);
        }

        if (!Did.TryParse(values[0]["did=".Length..], out var did))
        {
            throw new LexiconResolutionException(
                $"{name} names '{values[0]["did=".Length..]}', which is not a DID.",
                nsid, LexiconResolutionErrorKind.AuthorityNotFound);
        }

        return did;
    }

    /// <summary>
    /// Resolves <paramref name="nsid"/> from the repository of a known authority, skipping the DNS
    /// lookup — for a schema whose <c>_lexicon</c> record is not published yet, or an authority
    /// learned some other way.
    /// </summary>
    /// <param name="nsid">The NSID of the schema.</param>
    /// <param name="authority">The DID whose repository holds the schema record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema, verified against the authority's signing key.</returns>
    /// <exception cref="LexiconResolutionException">The schema could not be resolved.</exception>
    public async Task<ResolvedLexicon> ResolveAsync(Nsid nsid, Did authority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        ArgumentNullException.ThrowIfNull(authority);

        DidDocument document;
        try
        {
            document = await _didResolver.ResolveAsync(authority, cancellationToken).ConfigureAwait(false);
        }
        catch (DidResolutionException ex)
        {
            throw new LexiconResolutionException(
                $"The Lexicon authority {authority} could not be resolved: {ex.Message}",
                nsid, LexiconResolutionErrorKind.ResolutionFailed, ex);
        }

        var pds = document.GetPdsEndpoint();
        var signingKey = document.TryGetVerificationKey("#atproto", out var key) == DidDocumentEntryStatus.Found ? key : null;
        if (pds is null || signingKey is null)
        {
            throw new LexiconResolutionException(
                $"The DID document of {authority} publishes no {(pds is null ? "PDS" : "usable signing key")}.",
                nsid, LexiconResolutionErrorKind.ResolutionFailed);
        }

        if (pds.Scheme != Uri.UriSchemeHttps && !_options.AllowPrivateNetworks)
        {
            throw new LexiconResolutionException(
                $"The PDS of {authority} ({pds}) is not HTTPS.", nsid, LexiconResolutionErrorKind.ResolutionFailed);
        }

        var rkey = RecordKey.Parse(nsid.Value);
        var sync = new SyncClient(new XrpcClient(_httpClient, pds, _logger));

        VerifiedRecord verified;
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            budget.CancelAfter(_options.RequestTimeout);
            try
            {
                verified = await sync.GetVerifiedRecordAsync(
                    authority, LexiconSchemaRecord.Collection, rkey, signingKey, budget.Token)
                    .ConfigureAwait(false);
            }
            catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
            {
                throw new LexiconResolutionException(
                    $"{authority} publishes no schema record for {nsid}.", nsid, LexiconResolutionErrorKind.NotFound, ex);
            }
            catch (RepoVerificationException ex)
            {
                throw new LexiconResolutionException(
                    $"The schema record of {nsid} from {pds.Host} does not verify: {ex.Message}",
                    nsid, LexiconResolutionErrorKind.InvalidRecord, ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LexiconResolutionException(
                    $"{pds.Host} did not answer within {_options.RequestTimeout.TotalSeconds:0.#} s.",
                    nsid, LexiconResolutionErrorKind.ResolutionFailed, ex);
            }
            catch (Exception ex) when (ex is AtProtoException or HttpRequestException or IOException)
            {
                var reason = IdentityNetworkPolicy.IsBlocked(ex)
                    ? $"the identity fetch policy refused {pds.Host}"
                    : ex.Message;
                throw new LexiconResolutionException(
                    $"Fetching the schema record of {nsid} from {pds.Host} failed: {reason}",
                    nsid, LexiconResolutionErrorKind.ResolutionFailed, ex);
            }
        }

        if (!verified.Exists)
        {
            throw new LexiconResolutionException(
                $"{authority} publishes no schema record for {nsid}.", nsid, LexiconResolutionErrorKind.NotFound);
        }

        _logger.LogDebug("Resolved Lexicon {Nsid} from {Uri} at revision {Rev}.", nsid, verified.Uri, verified.Rev);
        return Validate(nsid, verified.Uri, verified.Cid!, verified.Value!.Value);
    }

    /// <summary>
    /// Checks that a fetched record is a Lexicon schema for <paramref name="nsid"/>: a
    /// <c>com.atproto.lexicon.schema</c> record of language version 1, whose <c>id</c> is the NSID
    /// and that has definitions.
    /// </summary>
    /// <exception cref="LexiconResolutionException">Thrown with <see cref="LexiconResolutionErrorKind.InvalidRecord"/>.</exception>
    internal static ResolvedLexicon Validate(Nsid nsid, AtUri uri, Cid cid, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("$type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            type.GetString() != LexiconSchemaRecord.Collection.Value)
        {
            throw Invalid(nsid, $"{uri} is not a {LexiconSchemaRecord.Collection} record.");
        }

        LexiconSchemaRecord? schema;
        try
        {
            schema = value.Deserialize<LexiconSchemaRecord>(AtProtoJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw Invalid(nsid, $"{uri} is not a valid Lexicon schema record: {ex.Message}", ex);
        }

        return Validate(nsid, uri, cid, schema);
    }

    /// <inheritdoc cref="Validate(Nsid, AtUri, Cid, JsonElement)"/>
    internal static ResolvedLexicon Validate(Nsid nsid, AtUri uri, Cid cid, LexiconSchemaRecord? schema)
    {
        if (schema is null)
            throw Invalid(nsid, $"{uri} is empty.");
        if (schema.Lexicon != 1)
            throw Invalid(nsid, $"{uri} is written in Lexicon language version {schema.Lexicon}; only version 1 is supported.");
        if (schema.Id != nsid)
            throw Invalid(nsid, $"{uri} declares the id '{schema.Id?.Value ?? "(none)"}', not {nsid}.");
        if (schema.Defs is not { Count: > 0 })
            throw Invalid(nsid, $"{uri} has no definitions.");

        return new ResolvedLexicon { Uri = uri, Cid = cid, Schema = schema };
    }

    private static LexiconResolutionException Invalid(Nsid nsid, string message, Exception? inner = null) =>
        new(message, nsid, LexiconResolutionErrorKind.InvalidRecord, inner);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

/// <summary>Why a Lexicon schema could not be resolved.</summary>
public enum LexiconResolutionErrorKind
{
    /// <summary>
    /// The NSID's authority names no DID: its <c>_lexicon</c> DNS record is missing, names more than
    /// one DID, or names something that is not a DID.
    /// </summary>
    AuthorityNotFound,

    /// <summary>
    /// DNS, the authority's DID document or its PDS could not be reached, refused, or answered
    /// with an error.
    /// </summary>
    ResolutionFailed,

    /// <summary>The authority publishes no schema for the NSID.</summary>
    NotFound,

    /// <summary>
    /// The record's proof does not verify, or it is not a Lexicon schema for the NSID (another
    /// <c>$type</c> or <c>id</c>, an unknown language version, no definitions).
    /// </summary>
    InvalidRecord,

    /// <summary>The schema resolved, but its <c>main</c> definition is not a permission set.</summary>
    NotPermissionSet,
}

/// <summary>
/// Thrown when a Lexicon schema cannot be resolved.
/// </summary>
public sealed class LexiconResolutionException : AtProtoException
{
    /// <summary>Creates an exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="nsid">The NSID being resolved.</param>
    /// <param name="kind">Why resolution failed.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public LexiconResolutionException(
        string message, Nsid nsid, LexiconResolutionErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        Nsid = nsid;
        Kind = kind;
    }

    /// <summary>The NSID being resolved.</summary>
    public Nsid Nsid { get; }

    /// <summary>Why resolution failed.</summary>
    public LexiconResolutionErrorKind Kind { get; }
}

/// <summary>
/// Permission-set lookups on an <see cref="ILexiconResolver"/>: checking that the set an
/// <c>include:</c> scope names exists before asking for it.
/// </summary>
/// <remarks>
/// An authorization server fails an authorization request whose <c>include:</c> scope names a
/// set it cannot resolve, so a client can check its scopes up front — at startup, or in a test —
/// instead of finding out from a failed login.
/// </remarks>
/// <example>
/// <code>
/// await lexicons.ResolvePermissionSetAsync(Nsid.Parse(AtProtoScopes.PermissionSets.FullApp));
/// var scope = AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullApp, "did:web:api.bsky.app#bsky_appview");
/// </code>
/// </example>
public static class LexiconResolverExtensions
{
    /// <summary>
    /// Resolves a permission set by NSID.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="nsid">The permission set's NSID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The permission set: the <c>main</c> definition of the resolved schema.</returns>
    /// <exception cref="LexiconResolutionException">
    /// The schema could not be resolved, or (<see cref="LexiconResolutionErrorKind.NotPermissionSet"/>)
    /// its <c>main</c> definition is not a permission set.
    /// </exception>
    public static async Task<LexiconPermissionSet> ResolvePermissionSetAsync(
        this ILexiconResolver resolver, Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(nsid);

        var resolved = await resolver.ResolveAsync(nsid, cancellationToken).ConfigureAwait(false);

        LexiconPermissionSet? set;
        try
        {
            set = resolved.Schema.GetPermissionSet();
        }
        catch (JsonException ex)
        {
            throw new LexiconResolutionException(
                $"The permission set {nsid} is malformed: {ex.Message}", nsid, LexiconResolutionErrorKind.InvalidRecord, ex);
        }

        return set ?? throw new LexiconResolutionException(
            $"{nsid} is a '{resolved.Schema.MainType ?? "(none)"}' Lexicon, not a permission set.",
            nsid, LexiconResolutionErrorKind.NotPermissionSet);
    }

    /// <summary>
    /// Resolves the permission set an <c>include:</c> scope names.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="scope">
    /// An <c>include</c> scope, as <c>AtProtoScopes.Include</c> builds it
    /// (<c>include:app.example.authFull?aud=…</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The permission set.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not an <c>include</c> scope naming a valid NSID.</exception>
    /// <exception cref="LexiconResolutionException">The set could not be resolved, or the Lexicon is not a permission set.</exception>
    public static Task<LexiconPermissionSet> ResolveIncludeScopeAsync(
        this ILexiconResolver resolver, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return resolver.ResolvePermissionSetAsync(ParseIncludeScope(scope), cancellationToken);
    }

    /// <summary>
    /// The NSID an <c>include</c> scope names, positionally (<c>include:&lt;nsid&gt;</c>) or as its
    /// <c>nsid</c> parameter (<c>include?nsid=&lt;nsid&gt;</c>).
    /// </summary>
    internal static Nsid ParseIncludeScope(string scope)
    {
        const string Resource = "include";

        var query = scope.IndexOf('?');
        var head = query < 0 ? scope : scope[..query];
        string? value = null;

        if (head.StartsWith(Resource + ":", StringComparison.Ordinal))
        {
            value = head[(Resource.Length + 1)..];
        }
        else if (head == Resource && query >= 0)
        {
            foreach (var pair in scope[(query + 1)..].Split('&'))
            {
                if (pair.StartsWith("nsid=", StringComparison.Ordinal))
                    value = pair["nsid=".Length..];
            }
        }

        if (value is null || !Nsid.TryParse(Uri.UnescapeDataString(value), out var nsid))
            throw new ArgumentException($"'{scope}' is not an include scope naming a permission set.", nameof(scope));

        return nsid;
    }
}
