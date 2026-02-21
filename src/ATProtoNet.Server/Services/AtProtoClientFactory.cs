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

    /// <summary>
    /// Creates a new <see cref="AtProtoClientFactory"/>.
    /// </summary>
    public AtProtoClientFactory(
        IAtProtoTokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
            InstanceUrl = tokenData.PdsUrl,
            // Disable auto-refresh timer — per-request clients are short-lived.
            // Token refresh is handled on-demand by the XRPC client when it receives
            // an ExpiredToken error.
            AutoRefreshSession = false,
        };

        var client = new AtProtoClient(options, httpClient, new InMemorySessionStore(), logger);

        // Reconstruct DPoP generator from stored private key
        var dpop = new DPoPProofGenerator(tokenData.DPoPPrivateKey);

        var oauthSession = new OAuthSessionResult
        {
            Did = tokenData.Did,
            Handle = tokenData.Handle,
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

        await client.ApplyOAuthSessionAsync(oauthSession, cancellationToken);

        return client;
    }
}
