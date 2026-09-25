using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Default implementation of <see cref="IAtProtoClientFactory"/> that creates
/// per-request <see cref="AtProtoClient"/> instances from stored sessions.
/// </summary>
/// <remarks>
/// Each client refreshes its session on demand and writes the rotated tokens back to the
/// store, so the next request's client starts from them.
/// </remarks>
public sealed class AtProtoClientFactory : IAtProtoClientFactory
{
    private readonly IAtProtoSessionStore _sessionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOAuthClientProvider? _oauthClientProvider;
    private readonly ILogger<AtProtoClientFactory> _logger;

    /// <summary>
    /// Creates a new <see cref="AtProtoClientFactory"/>.
    /// </summary>
    /// <param name="sessionStore">Store of the users' sessions.</param>
    /// <param name="httpClientFactory">HTTP client factory for outbound requests.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="oauthClientProvider">
    /// Optional provider that yields the shared <see cref="Auth.OAuth.OAuthClient"/> used to
    /// refresh OAuth-bound sessions. When not registered, factory-built clients
    /// will not be able to refresh expired tokens — register an implementation
    /// (the Blazor integration's <c>AtProtoOAuthService</c> registers itself) to
    /// enable transparent refresh.
    /// </param>
    public AtProtoClientFactory(
        IAtProtoSessionStore sessionStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOAuthClientProvider? oauthClientProvider = null)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
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

        var claim = user.FindFirst("did")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Did.TryParse(claim, out var did))
            return null;

        var httpClient = _httpClientFactory.CreateClient("AtProtoClient");
        var logger = _loggerFactory.CreateLogger<AtProtoClient>();

        // Refreshes on demand and persists rotated tokens to the same store, so a refresh made
        // by this request is what the next request's client reads.
        var client = new AtProtoClient(new AtProtoClientOptions(), httpClient, _sessionStore, logger);

        try
        {
            // Hand the per-request client the OAuthClient that issued the session (when
            // available) so it can refresh and revoke an OAuth session.
            var oauthClient = _oauthClientProvider?.TryGetClient();

            if (!await client.TryRestoreSessionAsync(did, oauthClient, cancellationToken))
            {
                client.Dispose();
                return null;
            }

            if (client.Session is OAuthSession && oauthClient is null)
            {
                _logger.LogWarning(
                    "No IOAuthClientProvider is registered (or none has yet produced a client). " +
                    "The per-request client for {Did} cannot refresh its OAuth session once the access token expires.",
                    did);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }
}
