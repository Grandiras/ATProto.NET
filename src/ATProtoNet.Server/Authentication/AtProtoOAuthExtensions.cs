using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Registers and maps the hosted AT Protocol OAuth login, which signs users in to an ASP.NET Core
/// application (MVC, Razor Pages, minimal APIs or Blazor) with a standard authentication cookie.
/// </summary>
/// <remarks>
/// <code>
/// // 1. Configure cookie auth (if not already done)
/// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
///     .AddCookie(options => { options.LoginPath = "/login"; });
///
/// // 2. Register AT Proto OAuth, the client factory and a session store
/// builder.Services.AddAtProto()
///     .WithOAuth()
///     .WithClientFactory()
///     .WithFileSessionStore();
///
/// // 3. Map OAuth endpoints
/// app.MapAtProtoOAuth();
///
/// // 4. Add a login link in your UI
/// // &lt;a href="/atproto/login?handle=alice.bsky.social"&gt;Login with AT Proto&lt;/a&gt;
/// </code>
/// </remarks>
public static class AtProtoOAuthExtensions
{
    // The error code of a login that failed for a reason other than an OAuthException.
    internal const string LoginFailedError = "login_failed";

    /// <summary>
    /// The name of the <see cref="HttpClient"/> the hosted login's <see cref="OAuthClient"/> sends
    /// with: discovery, pushed authorization, token, refresh and revocation requests.
    /// </summary>
    /// <remarks>
    /// <see cref="WithOAuth"/> gives it the identity fetch policy (public addresses only, no
    /// redirects; <c>AddAtProtoIdentity</c>'s <see cref="IdentityResolverOptions.AllowPrivateNetworks"/>
    /// lifts it), a 30-second timeout and the SDK's <c>User-Agent</c>. It connects directly, never
    /// through a proxy: the policy checks the address it connects to. Configure it further with
    /// <c>services.AddHttpClient(AtProtoOAuthExtensions.HttpClientName)</c>, but add no retrying
    /// handler, for the reasons <see cref="IAtProtoBuilder.HttpClient"/> gives; an authorization
    /// code and a refresh token are single-use too.
    /// </remarks>
    public const string HttpClientName = "ATProtoNet.OAuth";

    /// <summary>
    /// Registers the hosted AT Protocol OAuth login. Use with <see cref="MapAtProtoOAuth"/> to map
    /// its endpoints.
    /// </summary>
    /// <remarks>
    /// <para>This registers <see cref="AtProtoOAuthService"/> and its <see cref="OAuthClient"/> as
    /// singletons, the client built from the options on first use, so the sessions the client
    /// factory restores refresh through it even right after a restart. It does not configure
    /// cookie authentication itself:</para>
    /// <code>
    /// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    ///     .AddCookie();
    /// builder.Services.AddAtProto()
    ///     .WithOAuth(o => o.ClientName = "My App")
    ///     .WithClientFactory();
    /// </code>
    /// <para>The options go through <see cref="IOptions{TOptions}"/>, so they bind from
    /// configuration (<c>services.Configure&lt;AtProtoOAuthServerOptions&gt;(section)</c>) and are
    /// validated when the host starts.</para>
    /// <para>For development, loopback client metadata is generated for the server's plain HTTP
    /// address. For production, provide explicit <see cref="AtProtoOAuthServerOptions.ClientMetadata"/>
    /// with a published client_id, and for a confidential client its
    /// <see cref="AtProtoOAuthServerOptions.ClientKeys"/>.</para>
    /// <para>With <see cref="AtProtoBuilderExtensions.WithClientFactory"/>, the login stores each
    /// session in the session store for the factory, and revokes and removes it on sign-out.
    /// Without it, the login only signs users in.</para>
    /// <para>Identities are resolved through the <see cref="IIdentityResolver"/> that
    /// <c>AddAtProtoIdentity()</c> registers, when there is one, and otherwise through the login's
    /// own under the <see cref="IdentityResolverOptions"/>. The issuer check at a callback reads
    /// the account's DID document afresh through <see cref="IDidResolver.InvalidateAsync"/>, so a
    /// resolver registered in their place must honour it.</para>
    /// </remarks>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <param name="configure">Configures the login.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IAtProtoBuilder WithOAuth(
        this IAtProtoBuilder builder,
        Action<AtProtoOAuthServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddValidatedOptions(configure, ValidateOptions);

        // The identity options only. Registering the identity resolvers here would put the login
        // on whatever IDidResolver the application registered for other purposes, fresh or not.
        services.AddValidatedOptions<IdentityResolverOptions>(null, static options => options.Validate());

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient(client =>
            {
                // Far below the 100-second default: no browser or reverse proxy waits that long during a login.
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.TryParseAdd(AtProtoHttp.DefaultUserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                IdentityNetworkPolicy.CreateHandler(sp.GetRequiredService<IdentityResolverOptions>().AllowPrivateNetworks));

        services.TryAddSingleton(sp => new AtProtoOAuthService(
            sp.GetRequiredService<AtProtoOAuthServerOptions>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<IIdentityResolver>(),
            sp.GetRequiredService<IdentityResolverOptions>(),
            sp.GetService<IOAuthStateStore>(),
            sp.GetService<IServer>(),
            sp.GetService<ISessionRefreshCoordinator>()));

        // The client factory refreshes and revokes the OAuth sessions it restores with the
        // OAuthClient registered here: the service's own. Both the service and the container
        // dispose it at shutdown, which is harmless.
        services.TryAddSingleton(sp => sp.GetRequiredService<AtProtoOAuthService>().Client);

        return builder;
    }

    // The checks WithOAuth runs on the options when the host starts.
    internal static void ValidateOptions(AtProtoOAuthServerOptions options)
    {
        RequireLocalPath(options.RoutePrefix, nameof(options.RoutePrefix));
        RequireLocalPath(options.LoginPath, nameof(options.LoginPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CookieScheme, nameof(options.CookieScheme));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DefaultReturnUrl, nameof(options.DefaultReturnUrl));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PostLogoutRedirectUri, nameof(options.PostLogoutRedirectUri));

        if (string.IsNullOrWhiteSpace(options.Scopes) ||
            !options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(AtProtoScopes.AtProto))
        {
            throw new ArgumentException($"Must include '{AtProtoScopes.AtProto}'; got '{options.Scopes}'.", nameof(options.Scopes));
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            AtProtoOptionsRegistration.RequireHttpUrl(options.BaseUrl, nameof(options.BaseUrl));

        if (options.CookieExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.CookieExpiration), options.CookieExpiration, "Must be positive.");

        if (options.ClientKeys.Count > 0 && options.ClientMetadata is null)
        {
            throw new InvalidOperationException(
                "ClientKeys are for a confidential client, which needs ClientMetadata: its client_id " +
                "document must publish the keys, which a loopback client has nowhere to do.");
        }

        if (options.ServeClientMetadata)
            _ = ClientDocumentPaths(options);
    }

    private static void RequireLocalPath(string? value, string name)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException($"Must be a path on this application, starting with '/'; got '{value}'.", name);
    }

    /// <summary>
    /// Maps the AT Protocol OAuth endpoints: login, callback, relay and logout, and on request the
    /// client's metadata document and key set. Requires <see cref="WithOAuth"/>.
    /// </summary>
    /// <remarks>
    /// <para>Maps the following endpoints:</para>
    /// <list type="bullet">
    /// <item><description><c>GET {prefix}/login?handle=alice.bsky.social&amp;returnUrl=/admin</c> — starts the OAuth flow and redirects to the authorization server</description></item>
    /// <item><description><c>GET {prefix}/callback?code=xxx&amp;state=xxx&amp;iss=xxx</c> — completes it, issues the cookie and redirects to the return URL</description></item>
    /// <item><description><c>GET {prefix}/relay?code=xxx</c> — issues the cookie on the login's origin when the callback arrived on another loopback origin</description></item>
    /// <item><description><c>POST {prefix}/logout</c> — signs out, clears the cookie and redirects to the post-logout URL</description></item>
    /// </list>
    /// <para>A failed login redirects to <see cref="AtProtoOAuthServerOptions.LoginPath"/> with an
    /// <c>error</c> code, never an exception message. With
    /// <see cref="AtProtoOAuthServerOptions.ServeClientMetadata"/>, the client metadata is also
    /// served at the path of its <c>client_id</c>, and the public client keys at the path of its
    /// <c>jwks_uri</c>.</para>
    /// <para>Login form example:</para>
    /// <code>
    /// &lt;form action="/atproto/login" method="get"&gt;
    ///     &lt;input name="handle" placeholder="alice.bsky.social" required /&gt;
    ///     &lt;input type="hidden" name="returnUrl" value="/admin" /&gt;
    ///     &lt;button type="submit"&gt;Login with AT Proto&lt;/button&gt;
    /// &lt;/form&gt;
    /// </code>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder (typically <c>app</c>).</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AtProtoOAuthServerOptions.ServeClientMetadata"/> is set, but there is no
    /// <see cref="AtProtoOAuthServerOptions.ClientMetadata"/>, or its <c>client_id</c> or
    /// <c>jwks_uri</c> is not an HTTPS URL with a path to serve it at.
    /// </exception>
    public static IEndpointRouteBuilder MapAtProtoOAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<AtProtoOAuthServerOptions>();
        var prefix = options.RoutePrefix.TrimEnd('/');

        // GET /atproto/login?handle=xxx&returnUrl=/admin&pdsUrl=xxx
        endpoints.MapGet($"{prefix}/login", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
            ILogger<AtProtoOAuthService> logger,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;
            var handle = query["handle"].ToString();
            var returnUrl = query["returnUrl"].ToString();
            var pdsUrl = query["pdsUrl"].ToString();

            if (string.IsNullOrWhiteSpace(handle))
            {
                return Results.BadRequest(
                    "The 'handle' query parameter is required. " +
                    "Example: /atproto/login?handle=alice.bsky.social");
            }

            try
            {
                var authorizationUrl = await oauthService.StartLoginAsync(
                    context,
                    handle,
                    OAuthLoginBinding.IsLocalUrl(returnUrl) ? returnUrl : null,
                    string.IsNullOrWhiteSpace(pdsUrl) ? null : pdsUrl,
                    cancellationToken).ConfigureAwait(false);

                return Results.Redirect(authorizationUrl);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return LoginFailed(options, logger, ex, "Starting an OAuth login failed");
            }
        })
        .ExcludeFromDescription(); // Hide from OpenAPI/Swagger

        // GET /atproto/callback?code=xxx&state=xxx&iss=xxx
        endpoints.MapGet($"{prefix}/callback", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
            ILogger<AtProtoOAuthService> logger,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;
            var code = query["code"].ToString();
            var state = query["state"].ToString();
            var iss = query["iss"].ToString();
            var error = query["error"].ToString();

            // The authorization server's error code goes on; its description is its own text,
            // which the login page has no business displaying.
            if (!string.IsNullOrEmpty(error))
            {
                logger.LogInformation("The authorization server ended an OAuth login with {Error}", error);
                return RedirectToLogin(options, ErrorCode(error));
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(iss))
            {
                return Results.BadRequest(
                    "Missing required callback parameters: code, state, or iss.");
            }

            try
            {
                var result = await oauthService.CompleteCallbackAsync(context, code, state, iss, cancellationToken).ConfigureAwait(false);
                return Results.Redirect(result.RedirectUrl);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return LoginFailed(options, logger, ex, "Completing an OAuth login failed");
            }
        })
        .ExcludeFromDescription();

        // GET /atproto/relay?code=xxx (internal: relays auth cookie to the correct domain)
        // When the OAuth callback arrives on a different origin (e.g., http://127.0.0.1)
        // than the user's browser (e.g., https://localhost), the callback handler generates
        // a one-time code and redirects here to issue the cookie on the correct domain.
        endpoints.MapGet($"{prefix}/relay", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
            CancellationToken cancellationToken) =>
        {
            var code = context.Request.Query["code"].ToString();
            var returnUrl = await oauthService.TryRedeemRelayCodeAsync(context, code, cancellationToken).ConfigureAwait(false);

            if (returnUrl is not null)
                return Results.Redirect(returnUrl);

            // Invalid or expired relay code — redirect to login
            return Results.Redirect(options.LoginPath);
        })
        .ExcludeFromDescription();

        // POST /atproto/logout
        endpoints.MapPost($"{prefix}/logout", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
            CancellationToken cancellationToken) =>
        {
            var redirectUrl = await oauthService.LogoutAsync(context, cancellationToken).ConfigureAwait(false);
            return Results.Redirect(redirectUrl);
        })
        .ExcludeFromDescription();

        if (options.ServeClientMetadata)
            MapClientDocuments(endpoints, options);

        return endpoints;
    }

    // Serves the client metadata at its client_id, and the key set at its jwks_uri.
    private static void MapClientDocuments(IEndpointRouteBuilder endpoints, AtProtoOAuthServerOptions options)
    {
        var (metadataPath, keySetPath) = ClientDocumentPaths(options);

        // Written once: the options are fixed by the time the endpoints are mapped.
        var metadataJson = options.ClientMetadata!.ToJson();
        endpoints.MapGet(metadataPath, (HttpContext context) => Document(context, metadataJson))
            .ExcludeFromDescription();

        if (keySetPath is not null)
        {
            var keySetJson = JsonSerializer.Serialize(OAuthClientKey.CreateKeySet(options.ClientKeys));
            endpoints.MapGet(keySetPath, (HttpContext context) => Document(context, keySetJson))
                .ExcludeFromDescription();
        }
    }

    // The paths AtProtoOAuthServerOptions.ServeClientMetadata serves the client's documents at: its
    // client_id's, and its jwks_uri's when it names one.
    //
    // Throws InvalidOperationException: No client metadata is configured, or a URL cannot be served.
    private static (string Metadata, string? KeySet) ClientDocumentPaths(AtProtoOAuthServerOptions options)
    {
        var metadata = options.ClientMetadata ?? throw new InvalidOperationException(
            "ServeClientMetadata serves the configured ClientMetadata, and none is configured.");

        return (DocumentPath(metadata.ClientId, "client_id"),
            metadata.JwksUri is { } jwksUri ? DocumentPath(jwksUri, "jwks_uri") : null);
    }

    // A client document, cacheable for ClientDocumentMaxAge: authorization servers fetch it at every
    // login, and a key added for rotation reaches them within that time.
    private static IResult Document(HttpContext context, string json)
    {
        context.Response.Headers.CacheControl = $"public, max-age={(int)ClientDocumentMaxAge.TotalSeconds}";
        return Results.Text(json, "application/json");
    }

    // How long the served client metadata and key set may be cached (5 minutes).
    internal static readonly TimeSpan ClientDocumentMaxAge = TimeSpan.FromMinutes(5);

    private static string DocumentPath(string url, string field)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.AbsolutePath is "/" or "")
        {
            throw new InvalidOperationException(
                $"To be served, the client's {field} must be an HTTPS URL with a path, such as " +
                $"https://app.example.com/oauth-client-metadata.json; got '{url}'.");
        }

        return uri.AbsolutePath;
    }

    private static IResult LoginFailed(AtProtoOAuthServerOptions options, ILogger logger, Exception ex, string message)
    {
        if (ex is OAuthException oauth)
        {
            logger.LogInformation(ex, "{Message}: {Error}", message, oauth.Error);
            return RedirectToLogin(options, ErrorCode(oauth.Error));
        }

        // Anything else is this application's failure, whose message may name hosts, paths or
        // configuration: logged here, and only a fixed code goes to the browser.
        logger.LogError(ex, "{Message}", message);
        return RedirectToLogin(options, LoginFailedError);
    }

    private static IResult RedirectToLogin(AtProtoOAuthServerOptions options, string error) =>
        Results.Redirect($"{options.LoginPath.TrimEnd('/')}?error={Uri.EscapeDataString(error)}");

    // An error code fit for the login URL: a short token of letters, digits, _, - and ., as OAuth error
    // codes are, and LoginFailedError otherwise.
    internal static string ErrorCode(string? error)
    {
        if (string.IsNullOrEmpty(error) || error.Length > 64)
            return LoginFailedError;

        foreach (var c in error)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                return LoginFailedError;
        }

        return error;
    }
}
