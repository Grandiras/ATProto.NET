using System.Collections.Concurrent;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A client attestation that verified.
/// </summary>
/// <param name="ClientId">
/// The application's OAuth client ID — its <c>iss</c>, and the value an
/// <c>AllowListAppAccess</c> policy is evaluated against.
/// </param>
public sealed record VerifiedClientAttestation(string ClientId);

/// <summary>
/// Resolves an OAuth <c>client_id</c> to the public keys its attestations verify against.
/// </summary>
/// <remarks>
/// In AT Protocol OAuth a <c>client_id</c> <em>is</em> the URL of the client's metadata
/// document, so resolution is a fetch of that URL, followed by its <c>jwks_uri</c> when the keys
/// are not inline. That makes it an outbound request to a host the attestation chose, which is
/// why this is a seam: a production deployment wants caching, a timeout, and an egress policy on
/// it, and a test wants none of it.
/// </remarks>
public interface ISpaceClientMetadataResolver
{
    /// <summary>
    /// Resolves the keys published for a client ID.
    /// </summary>
    /// <param name="clientId">The OAuth client ID, which is the metadata document's URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client's published signing keys.</returns>
    /// <exception cref="SpaceVerificationException">Thrown when the client publishes no usable keys.</exception>
    Task<IReadOnlyList<JsonWebKey>> ResolveKeysAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISpaceClientMetadataResolver"/>: fetches <c>client-metadata.json</c>
/// over HTTPS and follows its <c>jwks_uri</c> when the keys are not inline.
/// </summary>
public sealed class HttpSpaceClientMetadataResolver : ISpaceClientMetadataResolver
{
    private readonly HttpClient _httpClient;
    private readonly SpaceServerOptions _options;

    /// <summary>
    /// Creates a resolver.
    /// </summary>
    /// <param name="httpClient">The client used for the outbound fetches.</param>
    /// <param name="options">Server options; supplies the document size ceiling.</param>
    public HttpSpaceClientMetadataResolver(HttpClient httpClient, SpaceServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _options = options ?? new SpaceServerOptions();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JsonWebKey>> ResolveKeysAsync(
        string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        // A client that can attest is a confidential client, which by definition publishes its
        // metadata at an https URL. The loopback client IDs OAuth allows for development are
        // public clients with no keys, so they cannot attest and are rejected here rather than
        // producing a confusing fetch failure.
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var metadataUri) ||
            !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw Invalid($"Client ID '{clientId}' is not an https URL, so it publishes no attestation keys.");
        }

        var metadata = await FetchAsync<OAuthClientMetadata>(metadataUri, "client metadata", cancellationToken);

        if (!string.Equals(metadata.ClientId, clientId, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The metadata at '{clientId}' declares client_id '{metadata.ClientId}', which does not match.");
        }

        if (metadata.Jwks is { Keys.Count: > 0 })
            return metadata.Jwks.Keys;

        if (string.IsNullOrEmpty(metadata.JwksUri))
            throw Invalid($"Client '{clientId}' publishes neither inline keys nor a jwks_uri.");

        if (!Uri.TryCreate(metadata.JwksUri, UriKind.Absolute, out var jwksUri) ||
            !string.Equals(jwksUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw Invalid($"Client '{clientId}' publishes a jwks_uri that is not an https URL.");
        }

        var jwks = await FetchAsync<JsonWebKeySet>(jwksUri, "JWKS", cancellationToken);

        return jwks.Keys.Count > 0
            ? jwks.Keys
            : throw Invalid($"The JWKS published for client '{clientId}' is empty.");
    }

    private async Task<T> FetchAsync<T>(Uri uri, string what, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw Invalid($"Fetching {what} from '{uri}' answered {(int)response.StatusCode}.");

            // The fetch is directed by the attestation, so it needs a ceiling whether or not the
            // server declares a length.
            var body = await response.Content.ReadBoundedAsync(_options.MaxClientMetadataBytes, cancellationToken)
                ?? throw Invalid($"The {what} at '{uri}' exceeds {_options.MaxClientMetadataBytes} bytes.");

            return JsonSerializer.Deserialize<T>(body.Span, JsonOptions)
                   ?? throw Invalid($"The {what} at '{uri}' is empty.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SpaceVerificationException(
                SpaceErrors.InvalidClientAttestation, $"Could not fetch {what} from '{uri}': {ex.Message}", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.InvalidClientAttestation, message);
}

/// <summary>
/// Verifies the client attestations presented to a space authority.
/// </summary>
/// <remarks>
/// <para>A client attestation establishes <em>which application</em> is acting, independently of
/// which user it acts for. The two are presented together but signed by different parties and
/// evaluated separately: a delegation token says the user consented to this app, and an
/// attestation says the app is the one it claims to be. Only a space that gates on app identity
/// needs the second.</para>
/// <para>The attestation is a <c>private_key_jwt</c> client assertion — the same shape a
/// confidential client already presents to its authorization server — addressed to the space
/// authority rather than to the PDS. It verifies against the key its <c>kid</c> names in the
/// JWKS the client publishes at its own <c>client_id</c> URL, which is what makes an allow-list
/// of client IDs enforceable rather than advisory: only the holder of the published key can
/// produce one.</para>
/// <para>A client's published keys are remembered for
/// <see cref="SpaceServerOptions.ClientMetadataCacheLifetime"/>, so an app renewing its
/// credentials does not cost a fetch of its metadata (and JWKS) each time. The flip side is that
/// a key the client removes from its JWKS keeps verifying here for up to that long. An
/// attestation naming a key the remembered set lacks, or failing against it, is checked once more
/// against a fresh fetch — a client that rotated its key is not locked out until the entry
/// expires — but a client's metadata is fetched at most once every 30 seconds however those
/// fetches end, so forged attestations cannot turn into a fetch each even while the client's host
/// is failing. Concurrent requests for one client share a single fetch.</para>
/// </remarks>
public sealed class SpaceClientAttestationVerifier
{
    private const int ClientKeyCacheCapacity = 1024;
    private static readonly TimeSpan MinRefetchInterval = TimeSpan.FromSeconds(30);

    private readonly ISpaceClientMetadataResolver _metadataResolver;
    private readonly IJtiReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ClientKeys> _clientKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<JsonWebKey>>>> _fetches = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="metadataResolver">Resolves a client ID to its published keys.</param>
    /// <param name="replayStore">The store that consumes each attestation's <c>jti</c>.</param>
    /// <param name="options">Server options; supplies the accepted attestation lifetime.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceClientAttestationVerifier(
        ISpaceClientMetadataResolver metadataResolver,
        IJtiReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(metadataResolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        _metadataResolver = metadataResolver;
        _replayStore = replayStore;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies a client attestation.
    /// </summary>
    /// <param name="jwt">The attestation, from the <c>clientAttestation</c> request field.</param>
    /// <param name="expectedAudience">
    /// The audience this authority answers to:
    /// <see cref="SpaceAuthority.HostAudience"/> for its own DID.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedClientAttestation> VerifyAsync(
        string jwt, string expectedAudience, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);

        if (string.IsNullOrWhiteSpace(jwt))
            throw Invalid("The request carries no client attestation.");

        SpaceToken parsed;
        try
        {
            // Parse already enforces that iss and sub are both the client ID, which is what
            // makes the assertion self-describing.
            parsed = SpaceTokens.Parse(SpaceTokenType.ClientAttestation, jwt);
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.InvalidClientAttestation, ex.Message, ex);
        }

        if (!string.Equals(parsed.Audience, expectedAudience, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The client attestation is addressed to '{parsed.Audience}', not to '{expectedAudience}'.");
        }

        var now = _timeProvider.GetUtcNow();
        if (parsed.IsExpired(now))
            throw Invalid("The client attestation is expired.");

        // An attestation lives 60 seconds; the client picks the `exp` it carries, so one dated
        // far ahead is refused rather than held in the replay store until then.
        if (!_options.IsWithinSingleUseWindow(parsed.ExpiresAt, now))
        {
            throw Invalid(
                $"The client attestation is valid for longer than the {_options.MaxSingleUseTokenLifetime} " +
                "this service accepts.");
        }

        var (keys, cached) = await GetKeysAsync(parsed.Issuer, now, cancellationToken);
        var failure = Check(keys, parsed);

        // A key the client has since rotated in is not in a remembered set yet. A refetch that
        // fails leaves the original refusal standing.
        if (failure is not null && cached && IsFetchDue(parsed.Issuer, now))
        {
            try
            {
                failure = Check(await FetchKeysAsync(parsed.Issuer, now, cancellationToken), parsed);
            }
            catch (SpaceVerificationException)
            {
            }
        }

        if (failure is not null)
            throw failure;

        if (!await _replayStore.TryConsumeAsync(
                parsed.Issuer, parsed.TokenId!, _options.ReplayRetention(parsed.ExpiresAt), cancellationToken))
        {
            throw Invalid("The client attestation has already been used; attestations are single-use.");
        }

        return new VerifiedClientAttestation(parsed.Issuer);
    }

    private async Task<(IReadOnlyList<JsonWebKey> Keys, bool Cached)> GetKeysAsync(
        string clientId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_clientKeys.TryGetValue(clientId, out var entry))
        {
            if (entry.Keys is { } keys && now - entry.FetchedAt < _options.ClientMetadataCacheLifetime)
                return (keys, true);

            // The last fetch failed, and recently: refuse without asking again. One still in
            // flight is joined instead.
            if (entry.Keys is null && !IsFetchDue(clientId, now) && !_fetches.ContainsKey(clientId))
                throw Invalid($"The metadata of client '{clientId}' could not be fetched; try again later.");
        }

        return (await FetchKeysAsync(clientId, now, cancellationToken), false);
    }

    /// <summary>Whether the last fetch for a client, however it ended, is at least 30 seconds old.</summary>
    private bool IsFetchDue(string clientId, DateTimeOffset now) =>
        !_clientKeys.TryGetValue(clientId, out var entry) || now - entry.AttemptedAt >= MinRefetchInterval;

    /// <summary>
    /// Fetches a client's keys, sharing one fetch among concurrent callers, and records the
    /// attempt whether it succeeds or not.
    /// </summary>
    private async Task<IReadOnlyList<JsonWebKey>> FetchKeysAsync(
        string clientId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var caching = _options.ClientMetadataCacheLifetime > TimeSpan.Zero;
        if (caching)
        {
            // Past the bound, start over rather than track recency: the working set is the apps
            // that attested in the last few minutes, and it refills on their next request.
            if (_clientKeys.Count >= ClientKeyCacheCapacity)
                _clientKeys.Clear();

            // Recorded before the fetch, so requests arriving while it runs, or after it fails,
            // do not start another.
            _clientKeys.AddOrUpdate(
                clientId,
                static (_, at) => new ClientKeys(null, at, at),
                static (_, existing, at) => existing with { AttemptedAt = at },
                now);
        }

        // Not tied to one caller's cancellation, since others may be waiting on the same fetch;
        // the named client's timeout bounds it.
        var fetch = _fetches.GetOrAdd(
            clientId, id => new Lazy<Task<IReadOnlyList<JsonWebKey>>>(() => FetchSharedAsync(id)));

        var keys = await fetch.Value.WaitAsync(cancellationToken);
        if (caching)
            _clientKeys[clientId] = new ClientKeys(keys, now, now);
        return keys;
    }

    private async Task<IReadOnlyList<JsonWebKey>> FetchSharedAsync(string clientId)
    {
        try
        {
            return await _metadataResolver.ResolveKeysAsync(clientId, CancellationToken.None);
        }
        finally
        {
            // Gone as soon as it settles, whoever is still waiting, so the next fetch is a new one.
            _fetches.TryRemove(clientId, out _);
        }
    }

    /// <summary>Checks the attestation against a key set, returning the refusal rather than throwing it.</summary>
    private static SpaceVerificationException? Check(IReadOnlyList<JsonWebKey> keys, SpaceToken parsed)
    {
        try
        {
            var key = SelectKey(keys, parsed.KeyId, parsed.Issuer);
            return JsonWebKeyVerifier.Verify(key, parsed.Algorithm, parsed.SigningInput, parsed.Signature, Invalid)
                ? null
                : Invalid($"The client attestation's signature does not verify against client '{parsed.Issuer}'.");
        }
        catch (SpaceVerificationException ex)
        {
            return ex;
        }
    }

    private static JsonWebKey SelectKey(IReadOnlyList<JsonWebKey> keys, string? keyId, string clientId)
    {
        if (keyId is not null)
        {
            return keys.FirstOrDefault(k => string.Equals(k.Kid, keyId, StringComparison.Ordinal))
                   ?? throw Invalid($"Client '{clientId}' publishes no key with kid '{keyId}'.");
        }

        // A kid is only omissible when the choice is unambiguous. Trying every published key
        // instead would let a client with one compromised key keep attesting under another.
        return keys.Count == 1
            ? keys[0]
            : throw Invalid(
                $"The client attestation names no kid and client '{clientId}' publishes {keys.Count} keys.");
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.InvalidClientAttestation, message);

    /// <summary>
    /// What is known of a client's keys: the last set fetched (none if every fetch failed), when it
    /// was fetched, and when a fetch was last attempted.
    /// </summary>
    private sealed record ClientKeys(IReadOnlyList<JsonWebKey>? Keys, DateTimeOffset FetchedAt, DateTimeOffset AttemptedAt);
}
