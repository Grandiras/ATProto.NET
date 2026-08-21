using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A client attestation that verified.
/// </summary>
/// <param name="Token">The parsed attestation.</param>
/// <param name="ClientId">
/// The application's OAuth client ID — its <c>iss</c>, and the value an
/// <c>AllowListAppAccess</c> policy is evaluated against.
/// </param>
public sealed record VerifiedClientAttestation(SpaceToken Token, string ClientId);

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
            if (response.Content.Headers.ContentLength > _options.MaxClientMetadataBytes)
                throw Invalid($"The {what} at '{uri}' exceeds {_options.MaxClientMetadataBytes} bytes.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LengthLimitedStream(stream, _options.MaxClientMetadataBytes);

            return await JsonSerializer.DeserializeAsync<T>(limited, JsonOptions, cancellationToken)
                   ?? throw Invalid($"The {what} at '{uri}' is empty.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            throw new SpaceVerificationException(
                SpaceErrors.InvalidClientAttestation, $"Could not fetch {what} from '{uri}': {ex.Message}", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.InvalidClientAttestation, message);

    /// <summary>A read-only stream that fails rather than reading past a byte ceiling.</summary>
    private sealed class LengthLimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            Count(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            Count(n);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void Count(int n)
        {
            _read += n;
            if (_read > limit)
                throw new InvalidDataException($"Response exceeded the {limit}-byte ceiling.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
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
/// </remarks>
public sealed class SpaceClientAttestationVerifier
{
    private readonly ISpaceClientMetadataResolver _metadataResolver;
    private readonly ISpaceReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="metadataResolver">Resolves a client ID to its published keys.</param>
    /// <param name="replayStore">The store that consumes each attestation's <c>jti</c>.</param>
    /// <param name="options">Server options; supplies the accepted attestation lifetime.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceClientAttestationVerifier(
        ISpaceClientMetadataResolver metadataResolver,
        ISpaceReplayStore replayStore,
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
    /// <see cref="SpaceAuthority.HostAudience(string)"/> for its own DID.
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

        var keys = await _metadataResolver.ResolveKeysAsync(parsed.Issuer, cancellationToken);
        var key = SelectKey(keys, parsed.KeyId, parsed.Issuer);

        if (!JsonWebKeyVerifier.Verify(key, parsed.Algorithm, parsed.SigningInput, parsed.Signature, Invalid))
            throw Invalid($"The client attestation's signature does not verify against client '{parsed.Issuer}'.");

        if (!await _replayStore.TryConsumeAsync(
                parsed.Issuer, parsed.TokenId!, parsed.ExpiresAt, cancellationToken))
        {
            throw Invalid("The client attestation has already been used; attestations are single-use.");
        }

        return new VerifiedClientAttestation(parsed, parsed.Issuer);
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
}
