using System.Net.Http.Headers;
using System.Net.Http.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Spaces;

/// <summary>
/// A space credential together with the key it is bound to.
/// </summary>
/// <remarks>
/// The credential is not a bearer token. It grants read access to a whole space and is presented
/// to every repo host in it, so as a bearer token it would be a shared secret — a host given one
/// in order to serve its own repo could replay it against every other host in the space. It is
/// bound at issuance to a key held by the requester, and every request carries a DPoP proof
/// signed by that key naming the host it is addressed to.
/// </remarks>
public sealed class SpaceCredential : IDisposable
{
    private bool _disposed;

    internal SpaceCredential(SpaceUri space, SpaceToken token, DPoPProofGenerator key)
    {
        Space = space;
        Token = token;
        Key = key;
    }

    /// <summary>The space this credential reads.</summary>
    public SpaceUri Space { get; }

    /// <summary>The parsed credential.</summary>
    public SpaceToken Token { get; }

    /// <summary>The credential as it should be presented under the <c>DPoP</c> scheme.</summary>
    public string Raw => Token.Raw;

    /// <summary>When the credential expires.</summary>
    public DateTimeOffset ExpiresAt => Token.ExpiresAt;

    /// <summary>
    /// The key the credential is bound to, which signs the DPoP proof on every request made
    /// with it.
    /// </summary>
    /// <remarks>
    /// A fresh keypair is generated per credential and need only outlive it — it is disposed
    /// along with the credential.
    /// </remarks>
    public DPoPProofGenerator Key { get; }

    /// <summary>Whether the credential is expired as of <paramref name="now"/>.</summary>
    /// <param name="now">The instant to check against. Defaults to the current time.</param>
    public bool IsExpired(DateTimeOffset? now = null) => Token.IsExpired(now);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Key.Dispose();
    }
}

/// <summary>
/// Configuration for a <see cref="SpaceCredentialProvider"/>.
/// </summary>
public sealed class SpaceCredentialOptions
{
    /// <summary>
    /// Produces the application's client attestation for a given space host audience, or
    /// <see langword="null"/> when the application is a public client.
    /// </summary>
    /// <remarks>
    /// <para>A space that gates on app identity requires one; a space with open app access does
    /// not, and there is no way to tell in advance. The provider therefore asks without an
    /// attestation first and retries with one if the authority refuses on app grounds, so
    /// setting this costs nothing against spaces that do not need it.</para>
    /// <para>The attestation is a <c>private_key_jwt</c> client assertion — the same shape a
    /// confidential client already presents to its authorization server — but addressed to the
    /// space authority rather than to the PDS.</para>
    /// </remarks>
    public Func<string, CancellationToken, Task<string>>? ClientAttestationFactory { get; init; }

    /// <summary>
    /// How long before expiry a cached credential is renewed. Defaults to five minutes.
    /// </summary>
    public TimeSpan RenewalWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resolves a space authority or repo host DID to its endpoint. Supply this to override DID
    /// document resolution, e.g. to point a test at a local PDS.
    /// </summary>
    public Func<string, CancellationToken, Task<string>>? HostResolver { get; init; }
}

/// <summary>
/// Obtains and caches space credentials, and hands out readers bound to individual repo hosts.
/// </summary>
/// <remarks>
/// <para>This runs the credential flow end to end. The application asks the user's PDS for a
/// <see cref="SpaceTokenType.Delegation">delegation token</see>, presents it to the space
/// authority alongside a DPoP proof (and a client attestation if the space wants one), and
/// receives a <see cref="SpaceCredential"/> bound to the key that signed the proof. From then on
/// it reads each member's repo from that member's own host, with the credential and a fresh
/// proof addressed to that host.</para>
/// <para>An application serving many users of a space does <b>not</b> need a credential per
/// user. It may obtain one using any single user's session and fan the resulting data out from
/// its own copy — which is also what keeps the number of distinct syncers per repo low enough
/// for PDSes to serve without a relay in between. It loses access only when it loses every OAuth
/// session for the space and can no longer renew.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var provider = new SpaceCredentialProvider(client);
///
/// var space = SpaceUri.Parse("at://did:plc:abc123/space/com.atmoboards.forum/default");
/// await foreach (var writer in client.Space.EnumerateReposAsync(space))
/// {
///     using var reader = await provider.CreateReaderForRepoAsync(space, writer.Did);
///     await foreach (var record in reader.Space.EnumerateRecordsAsync(space, writer.Did))
///         Console.WriteLine(record.Path);
/// }
/// </code>
/// </example>
public sealed class SpaceCredentialProvider : IAsyncDisposable, IDisposable
{
    private readonly AtProtoClient _client;
    private readonly SpaceCredentialOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly DidResolver _didResolver;
    private readonly bool _ownsDidResolver;
    private readonly ILogger _logger;

    private readonly Dictionary<string, SpaceCredential> _credentials = new(StringComparer.Ordinal);
    private readonly List<SpaceCredential> _superseded = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    // One connection pool across every repo host a syncer talks to. Each SpaceReader needs its
    // own HttpClient, because XrpcClient addresses a host through BaseAddress, but a syncer
    // walking a large space would otherwise open a fresh pool per member.
    private readonly SocketsHttpHandler _readerHandler = new();
    private bool _disposed;

    /// <summary>
    /// Creates a provider that mints delegation tokens through <paramref name="client"/>'s session.
    /// </summary>
    /// <param name="client">
    /// An authenticated client for the acting user's PDS. Its session must hold a covering
    /// <c>space:</c> scope with a <c>read</c> grant, which is what confers
    /// <c>getDelegationToken</c>; <c>read_self</c> alone does not.
    /// </param>
    /// <param name="options">Optional configuration.</param>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> for talking to space authorities and repo hosts. One is
    /// created and owned when omitted. Requests are sent to absolute URLs, so any
    /// <see cref="HttpClient.BaseAddress"/> is ignored.
    /// </param>
    /// <param name="didResolver">A DID resolver. One is created and owned when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public SpaceCredentialProvider(
        AtProtoClient client,
        SpaceCredentialOptions? options = null,
        HttpClient? httpClient = null,
        DidResolver? didResolver = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _options = options ?? new SpaceCredentialOptions();
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _ownsDidResolver = didResolver is null;
        _didResolver = didResolver ?? new DidResolver();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Returns a credential for a space, minting one if none is cached or the cached one is
    /// about to expire.
    /// </summary>
    /// <param name="space">The space to read.</param>
    /// <param name="forceRenew">Discard any cached credential and mint a fresh one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceCredentialException">Thrown when the authority refuses to issue one.</exception>
    public async Task<SpaceCredential> GetCredentialAsync(
        SpaceUri space,
        bool forceRenew = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRenew &&
                _credentials.TryGetValue(space.Value, out var cached) &&
                !cached.IsExpired(DateTimeOffset.UtcNow + _options.RenewalWindow))
            {
                return cached;
            }

            var credential = await MintAsync(space, cancellationToken);

            // A renewed credential does not invalidate the readers already holding the old one —
            // they keep signing with its key until they are disposed — so the superseded
            // credential is retained rather than disposed here, and released with the provider.
            if (_credentials.Remove(space.Value, out var previous))
                _superseded.Add(previous);
            _credentials[space.Value] = credential;

            return credential;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates a reader for one repo host, authenticated with this space's credential.
    /// </summary>
    /// <param name="space">The space to read.</param>
    /// <param name="hostUrl">The repo host's base URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reader the caller is responsible for disposing.</returns>
    public async Task<SpaceReader> CreateReaderAsync(
        SpaceUri space, string hostUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUrl);

        var credential = await GetCredentialAsync(space, cancellationToken: cancellationToken);
        return new SpaceReader(hostUrl, credential, _readerHandler, _logger);
    }

    /// <summary>
    /// Resolves an account's repo host from its DID document and creates a reader for it.
    /// </summary>
    /// <param name="space">The space to read.</param>
    /// <param name="repoDid">The DID of the account whose repo is to be read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reader the caller is responsible for disposing.</returns>
    public async Task<SpaceReader> CreateReaderForRepoAsync(
        SpaceUri space, string repoDid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);

        var host = await ResolveHostAsync(repoDid, cancellationToken);
        return await CreateReaderAsync(space, host, cancellationToken);
    }

    /// <summary>
    /// Resolves the endpoint that answers for a DID: its <c>#atproto_space_host</c> service
    /// entry, or its PDS when it publishes none.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceCredentialException">Thrown when the DID publishes no usable endpoint.</exception>
    public async Task<string> ResolveHostAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        if (_options.HostResolver is not null)
            return await _options.HostResolver(did, cancellationToken);

        Identity.DidDocument document;
        try
        {
            document = await _didResolver.ResolveDidAsync(did, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or DidWebException or InvalidOperationException)
        {
            throw new SpaceCredentialException($"Could not resolve '{did}': {ex.Message}", ex);
        }

        return SpaceAuthority.GetHostEndpoint(document)
            ?? throw new SpaceCredentialException(
                $"'{did}' publishes neither an {SpaceAuthority.HostServiceId} service entry nor a PDS endpoint.");
    }

    private async Task<SpaceCredential> MintAsync(SpaceUri space, CancellationToken cancellationToken)
    {
        var authorityHost = await ResolveHostAsync(space.Authority, cancellationToken);
        var endpoint = new Uri(
            new Uri(authorityHost.TrimEnd('/') + "/"),
            "xrpc/com.atproto.space.getSpaceCredential");

        // A fresh keypair per credential, discarded when the credential expires.
        var key = new DPoPProofGenerator();
        try
        {
            // Whether the space gates on app identity is not advertised, so ask without an
            // attestation first and add one only if the authority refuses on app grounds.
            var response = await ExchangeAsync(space, endpoint, key, clientAttestation: null, cancellationToken);

            if (response is null && _options.ClientAttestationFactory is not null)
            {
                var attestation = await _options.ClientAttestationFactory(space.HostAudience, cancellationToken);
                _logger.LogDebug("Space {Space} gates on client identity; retrying with a client attestation.", space);
                response = await ExchangeAsync(space, endpoint, key, attestation, cancellationToken);
            }

            if (response is null)
            {
                throw new SpaceCredentialException(
                    $"The authority for {space} refused a credential on the basis of the requesting app, and " +
                    $"no {nameof(SpaceCredentialOptions.ClientAttestationFactory)} is configured.",
                    SpaceErrors.AppNotAuthorized);
            }

            var token = SpaceTokens.Parse(SpaceTokenType.Credential, response.Credential);

            if (!string.Equals(token.ConfirmationThumbprint, key.KeyThumbprint, StringComparison.Ordinal))
            {
                throw new SpaceCredentialException(
                    "The authority issued a credential bound to a different key than the one this " +
                    "application proved possession of.");
            }

            if (!string.Equals(token.Subject, space.Value, StringComparison.Ordinal))
                throw new SpaceCredentialException($"The authority issued a credential for '{token.Subject}', not {space}.");

            return new SpaceCredential(space, token, key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Performs one <c>getSpaceCredential</c> exchange. Returns <see langword="null"/> when the
    /// authority refused on app-identity grounds, which is the signal to retry with an attestation.
    /// </summary>
    private async Task<GetSpaceCredentialResponse?> ExchangeAsync(
        SpaceUri space,
        Uri endpoint,
        DPoPProofGenerator key,
        string? clientAttestation,
        CancellationToken cancellationToken)
    {
        // Single-use and 60 seconds long, so it is fetched immediately before the exchange.
        var delegation = await _client.Space.GetDelegationTokenAsync(space.Value, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(
                new GetSpaceCredentialRequest { Space = space.Value, ClientAttestation = clientAttestation },
                options: AtProtoJsonDefaults.Options),
        };

        // The delegation token is an authorization grant rather than an access token, so it
        // travels as a bearer token and the proof carries no `ath`.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegation.Token);
        request.Headers.TryAddWithoutValidation(
            "DPoP", key.GenerateProof(HttpMethod.Post.Method, endpoint.ToString()));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<GetSpaceCredentialResponse>(
                AtProtoJsonDefaults.Options, cancellationToken)
                ?? throw new SpaceCredentialException("The authority returned an empty credential response.");
        }

        var error = await ReadErrorAsync(response, cancellationToken);

        if (string.Equals(error, SpaceErrors.AppNotAuthorized, StringComparison.Ordinal) &&
            clientAttestation is null)
        {
            return null;
        }

        throw new SpaceCredentialException(
            $"The authority for {space} refused a credential: {error ?? response.StatusCode.ToString()}.",
            error);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<XrpcErrorBody>(cancellationToken);
            return body?.Error;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or HttpRequestException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class XrpcErrorBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Error { get; init; }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var credential in _credentials.Values)
            credential.Dispose();
        _credentials.Clear();

        foreach (var credential in _superseded)
            credential.Dispose();
        _superseded.Clear();

        _readerHandler.Dispose();
        _lock.Dispose();
        if (_ownsDidResolver)
            _didResolver.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Reads a space from one repo host, authenticated with a space credential rather than OAuth.
/// </summary>
/// <remarks>
/// <para>Every request carries <c>Authorization: DPoP &lt;credential&gt;</c> together with a proof
/// signed by the credential's bound key and naming this host, so the credential cannot be
/// replayed by this host against any other host in the space.</para>
/// <para>A reader holds the credential it was created with and does not renew it, so one kept
/// past that credential's expiry (two hours by default) starts failing. Create readers per sync
/// pass rather than caching them for the lifetime of a long-running syncer.</para>
/// </remarks>
public sealed class SpaceReader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private bool _disposed;

    internal SpaceReader(
        string hostUrl, SpaceCredential credential, HttpMessageHandler handler, ILogger logger)
    {
        Credential = credential;
        HostUrl = hostUrl.TrimEnd('/');

        // XrpcClient addresses one host through its BaseAddress and a syncer talks to many, so
        // each reader gets its own HttpClient — but over the provider's shared handler, so they
        // share one connection pool rather than opening one per member.
        _httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(HostUrl + "/"),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
            $"ATProtoNet/{typeof(SpaceReader).Assembly.GetName().Version}");

        _xrpc = new XrpcClient(_httpClient, logger, AtProtoJsonDefaults.Options);
        _xrpc.SetOAuthTokens(credential.Raw, refreshToken: null, credential.Key);

        Space = new SpaceClient(_xrpc);
        SimpleSpace = new Lexicon.Com.AtProto.SimpleSpace.SimpleSpaceClient(_xrpc);
    }

    /// <summary>The repo host this reader talks to.</summary>
    public string HostUrl { get; }

    /// <summary>The credential this reader presents.</summary>
    public SpaceCredential Credential { get; }

    /// <summary>The <c>com.atproto.space.*</c> endpoints on this host.</summary>
    public SpaceClient Space { get; }

    /// <summary>The <c>com.atproto.simplespace.*</c> endpoints on this host.</summary>
    public Lexicon.Com.AtProto.SimpleSpace.SimpleSpaceClient SimpleSpace { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _xrpc.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>
/// Thrown when a space credential cannot be obtained, or the authority issues one that does not
/// match what was asked for.
/// </summary>
public sealed class SpaceCredentialException : Exception
{
    /// <summary>Creates a new exception with the given message and optional XRPC error name.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="error">The XRPC error name the authority returned, if any. See <see cref="SpaceErrors"/>.</param>
    public SpaceCredentialException(string message, string? error = null) : base(message)
    {
        Error = error;
    }

    /// <summary>Creates a new exception with the given message and cause.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SpaceCredentialException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// The XRPC error name the authority returned, if any. See <see cref="SpaceErrors"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SpaceErrors.SpaceDeleted"/> is the durable signal that a space is gone: a
    /// syncer that missed the deletion notification learns it here, on its next renewal, and
    /// should drop every copy it holds. A renewal that fails for any other reason says nothing
    /// about the space, and the syncer keeps its copy.
    /// </remarks>
    public string? Error { get; }
}
