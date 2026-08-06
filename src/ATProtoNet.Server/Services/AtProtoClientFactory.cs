using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Default implementation of <see cref="IAtProtoClientFactory"/> that creates
/// per-request <see cref="AtProtoClient"/> instances from stored OAuth tokens.
/// </summary>
public sealed class AtProtoClientFactory : IAtProtoClientFactory
{
    private readonly IAtProtoTokenStore _tokenStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOAuthClientProvider? _oauthClientProvider;
    private readonly ILogger<AtProtoClientFactory> _logger;

    /// <summary>
    /// Creates a new <see cref="AtProtoClientFactory"/>.
    /// </summary>
    /// <param name="tokenStore">Store for persisted OAuth tokens.</param>
    /// <param name="httpClientFactory">HTTP client factory for outbound requests.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="oauthClientProvider">
    /// Optional provider that yields the shared <see cref="OAuthClient"/> used to
    /// refresh OAuth-bound sessions. When not registered, factory-built clients
    /// will not be able to refresh expired tokens — register an implementation
    /// (the Blazor integration's <c>AtProtoOAuthService</c> registers itself) to
    /// enable transparent refresh.
    /// </param>
    public AtProtoClientFactory(
        IAtProtoTokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOAuthClientProvider? oauthClientProvider = null)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _oauthClientProvider = oauthClientProvider;
        _logger = loggerFactory.CreateLogger<AtProtoClientFactory>();
    }

    /// <inheritdoc/>
    public async Task<AtProtoClient?> CreateClientForUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var did = user.FindFirst("did")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(did))
            return null;

        var tokenData = await _tokenStore.GetAsync(did, cancellationToken);
        if (tokenData is null)
            return null;

        var httpClient = _httpClientFactory.CreateClient("AtProtoClient");
        var logger = _loggerFactory.CreateLogger<AtProtoClient>();

        var options = new AtProtoClientOptions
        {
            // No refresh timer: these clients live for one request, so a background
            // timer would rarely fire before disposal. Nothing refreshes them
            // on-demand either — a caller that sees an ExpiredToken error should call
            // RefreshSessionAsync and retry, which writes the rotated token back to
            // the store passed to ApplyOAuthSessionAsync below.
            AutoRefreshSession = false,
            InstanceUrl = tokenData.PdsUrl,
        };

        var client = new AtProtoClient(options, httpClient, new InMemorySessionStore(), logger);

        // Reconstruct DPoP generator from stored private key
        var dpop = new DPoPProofGenerator(tokenData.DPoPPrivateKey);

        var oauthSession = new OAuthSessionResult
        {
            Did = tokenData.Did,
            Handle = tokenData.Handle,
            IsHandleVerified = tokenData.IsHandleVerified,
            AccessToken = tokenData.AccessToken,
            RefreshToken = tokenData.RefreshToken,
            TokenType = "DPoP",
            ExpiresIn = tokenData.ExpiresIn,
            Scope = tokenData.Scope,
            PdsUrl = tokenData.PdsUrl,
            Issuer = tokenData.Issuer,
            TokenEndpoint = tokenData.TokenEndpoint,
            DPoP = dpop,
            DpopKeyId = dpop.KeyThumbprint,
            // Intentionally null — stored nonces are always stale for per-request clients.
            // The XRPC client's DPoP retry logic will acquire fresh nonces on first request.
            AuthServerDpopNonce = null,
            ResourceServerDpopNonce = null,
            TokenObtainedAt = tokenData.TokenObtainedAt,
        };

        // Hand the per-request client the OAuthClient that issued this session (when
        // available) so RefreshSessionAsync can route token refresh through the
        // OAuth token endpoint instead of throwing. Without a provider this client
        // will be unable to refresh expired tokens.
        var oauthClient = _oauthClientProvider?.TryGetClient();
        if (oauthClient is null)
        {
            _logger.LogWarning(
                "No IOAuthClientProvider is registered (or none has yet produced a client). " +
                "Per-request clients for {Did} will be unable to refresh expired OAuth tokens.",
                tokenData.Did);
        }

        try
        {
            // Pass _tokenStore so the per-request client persists rotated refresh tokens
            // back to durable storage. Without this, the rotated refresh token only lives
            // in the per-request InMemorySessionStore — request B then reads the old
            // (now-invalidated) refresh token and the user is permanently logged out.
            await client.ApplyOAuthSessionAsync(oauthSession, oauthClient, _tokenStore, cancellationToken);
        }
        catch
        {
            // The client owns oauthSession (and its DPoP key) only once Apply succeeds;
            // on failure nothing else will ever dispose either, and every failed request
            // would leak an ECDsa native handle until finalization.
            oauthSession.Dispose();
            client.Dispose();
            throw;
        }

        return client;
    }
}
