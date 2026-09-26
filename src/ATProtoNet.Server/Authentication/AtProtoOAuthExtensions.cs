using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
/// // 2. Register AT Proto OAuth, and the session store and client factory
/// builder.Services.AddAtProtoAuthentication();
/// builder.Services.AddAtProtoServer();
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
    /// <summary>The error code of a login that failed for a reason other than an <see cref="OAuthException"/>.</summary>
    internal const string LoginFailedError = "login_failed";

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
    /// builder.Services.AddAtProtoAuthentication();
    /// </code>
    /// <para>For development, loopback client metadata is generated for the server's plain HTTP
    /// address. For production, provide explicit <see cref="AtProtoOAuthServerOptions.ClientMetadata"/>
    /// with a published client_id, and for a confidential client its
    /// <see cref="AtProtoOAuthServerOptions.ClientKeys"/>.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure AT Proto OAuth options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoAuthentication(
        this IServiceCollection services,
        Action<AtProtoOAuthServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AtProtoOAuthServerOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<AtProtoOAuthService>();

        // The client factory refreshes and revokes the OAuth sessions it restores with the
        // OAuthClient registered here: the service's own. Both the service and the container
        // dispose it at shutdown, which is harmless.
        services.TryAddSingleton(sp => sp.GetRequiredService<AtProtoOAuthService>().Client);

        return services;
    }

    /// <summary>
    /// Maps the AT Protocol OAuth endpoints: login, callback, relay and logout, and on request the
    /// client's metadata document and key set. Requires <see cref="AddAtProtoAuthentication"/>.
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
                    cancellationToken);

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
                var result = await oauthService.CompleteCallbackAsync(context, code, state, iss, cancellationToken);
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
            var returnUrl = await oauthService.TryRedeemRelayCodeAsync(context, code, cancellationToken);

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
            var redirectUrl = await oauthService.LogoutAsync(context, cancellationToken);
            return Results.Redirect(redirectUrl);
        })
        .ExcludeFromDescription();

        if (options.ServeClientMetadata)
            MapClientDocuments(endpoints, options);

        return endpoints;
    }

    /// <summary>Serves the client metadata at its <c>client_id</c>, and the key set at its <c>jwks_uri</c>.</summary>
    private static void MapClientDocuments(IEndpointRouteBuilder endpoints, AtProtoOAuthServerOptions options)
    {
        var metadata = options.ClientMetadata ?? throw new InvalidOperationException(
            "ServeClientMetadata serves the configured ClientMetadata, and none is configured.");

        // Written once: the options are fixed by the time the endpoints are mapped.
        var metadataJson = metadata.ToJson();
        endpoints.MapGet(DocumentPath(metadata.ClientId, "client_id"), (HttpContext context) => Document(context, metadataJson))
            .ExcludeFromDescription();

        if (metadata.JwksUri is { } jwksUri)
        {
            var keySetJson = JsonSerializer.Serialize(OAuthClientKey.CreateKeySet(options.ClientKeys));
            endpoints.MapGet(DocumentPath(jwksUri, "jwks_uri"), (HttpContext context) => Document(context, keySetJson))
                .ExcludeFromDescription();
        }
    }

    /// <summary>
    /// A client document, cacheable for <see cref="ClientDocumentMaxAge"/>: authorization servers
    /// fetch it at every login, and a key added for rotation reaches them within that time.
    /// </summary>
    private static IResult Document(HttpContext context, string json)
    {
        context.Response.Headers.CacheControl = $"public, max-age={(int)ClientDocumentMaxAge.TotalSeconds}";
        return Results.Text(json, "application/json");
    }

    /// <summary>How long the served client metadata and key set may be cached (5 minutes).</summary>
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

    /// <summary>
    /// An error code fit for the login URL: a short token of letters, digits, <c>_</c>, <c>-</c>
    /// and <c>.</c>, as OAuth error codes are, and <see cref="LoginFailedError"/> otherwise.
    /// </summary>
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
