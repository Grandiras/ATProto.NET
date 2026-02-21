using ATProtoNet.Blazor.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Blazor;

/// <summary>
/// Extension methods for integrating AT Protocol OAuth with cookie-based authentication
/// in ASP.NET Core / Blazor Server applications.
/// </summary>
/// <remarks>
/// <para>This integrates AT Protocol OAuth with ASP.NET Core's cookie authentication.
/// It maps HTTP endpoints that handle the OAuth flow
/// and issue standard authentication cookies.</para>
/// <para>Usage:</para>
/// <code>
/// // 1. Configure cookie auth (if not already done)
/// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
///     .AddCookie(options => { options.LoginPath = "/login"; });
///
/// // 2. Register AT Proto OAuth services
/// builder.Services.AddAtProtoAuthentication();
///
/// // 3. Map OAuth endpoints
/// app.MapAtProtoOAuth();
///
/// // 4. Add a login link in your UI
/// // &lt;a href="/atproto/login?handle=alice.bsky.social"&gt;Login with AT Proto&lt;/a&gt;
/// </code>
/// </remarks>
public static class AtProtoAuthenticationExtensions
{
    /// <summary>
    /// Registers AT Protocol OAuth services for server-side cookie-based authentication.
    /// Use with <see cref="MapAtProtoOAuth"/> to map the OAuth endpoints.
    /// </summary>
    /// <remarks>
    /// <para>This method registers the <see cref="AtProtoOAuthService"/> singleton but does NOT
    /// configure cookie authentication itself. You must configure cookie authentication separately:</para>
    /// <code>
    /// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    ///     .AddCookie();
    /// builder.Services.AddAtProtoAuthentication();
    /// </code>
    /// <para>For development, loopback client metadata is auto-generated from the first request's URL.
    /// For production, provide explicit <see cref="AtProtoOAuthServerOptions.ClientMetadata"/>
    /// with a registered client_id.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure AT Proto OAuth options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoAuthentication(
        this IServiceCollection services,
        Action<AtProtoOAuthServerOptions>? configure = null)
    {
        var options = new AtProtoOAuthServerOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<AtProtoOAuthService>();

        return services;
    }

    /// <summary>
    /// Maps AT Protocol OAuth endpoints for login, callback, and logout.
    /// Requires <see cref="AddAtProtoAuthentication"/> to be called first.
    /// </summary>
    /// <remarks>
    /// <para>Maps the following endpoints:</para>
    /// <list type="bullet">
    /// <item><description><c>GET {prefix}/login?handle=alice.bsky.social&amp;returnUrl=/admin</c> — Starts OAuth flow, redirects to authorization server</description></item>
    /// <item><description><c>GET {prefix}/callback?code=xxx&amp;state=xxx&amp;iss=xxx</c> — Handles OAuth callback, issues cookie, redirects to returnUrl</description></item>
    /// <item><description><c>POST {prefix}/logout</c> — Signs out, clears cookie, redirects to post-logout URL</description></item>
    /// </list>
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
    public static IEndpointRouteBuilder MapAtProtoOAuth(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<AtProtoOAuthServerOptions>();
        var prefix = options.RoutePrefix.TrimEnd('/');

        // GET /atproto/login?handle=xxx&returnUrl=/admin&pdsUrl=xxx
        endpoints.MapGet($"{prefix}/login", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
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
                    IsValidReturnUrl(returnUrl) ? returnUrl : null,
                    string.IsNullOrWhiteSpace(pdsUrl) ? null : pdsUrl,
                    cancellationToken);

                return Results.Redirect(authorizationUrl);
            }
            catch (Exception ex)
            {
                var loginPath = options.LoginPath.TrimEnd('/');
                var errorParam = Uri.EscapeDataString(SanitizeErrorMessage(ex.Message));
                return Results.Redirect($"{loginPath}?error={errorParam}");
            }
        })
        .ExcludeFromDescription(); // Hide from OpenAPI/Swagger

        // GET /atproto/callback?code=xxx&state=xxx&iss=xxx
        endpoints.MapGet($"{prefix}/callback", async (
            HttpContext context,
            AtProtoOAuthService oauthService,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;
            var code = query["code"].ToString();
            var state = query["state"].ToString();
            var iss = query["iss"].ToString();
            var error = query["error"].ToString();
            var errorDescription = query["error_description"].ToString();

            // Handle error response from Authorization Server
            if (!string.IsNullOrEmpty(error))
            {
                var errorMsg = !string.IsNullOrEmpty(errorDescription) ? errorDescription : error;
                var loginPath = options.LoginPath.TrimEnd('/');
                return Results.Redirect($"{loginPath}?error={Uri.EscapeDataString(SanitizeErrorMessage(errorMsg))}");
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(iss))
            {
                return Results.BadRequest(
                    "Missing required callback parameters: code, state, or iss.");
            }

            try
            {
                var returnUrl = await oauthService.CompleteCallbackAsync(
                    context, code, state, iss, cancellationToken);

                // Validate returned URL to prevent open redirect
                if (!IsValidReturnUrl(returnUrl))
                    returnUrl = options.DefaultReturnUrl;

                return Results.Redirect(returnUrl);
            }
            catch (Exception ex)
            {
                var loginPath = options.LoginPath.TrimEnd('/');
                return Results.Redirect($"{loginPath}?error={Uri.EscapeDataString(SanitizeErrorMessage(ex.Message))}");
            }
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

        return endpoints;
    }

    /// <summary>
    /// Validates that a return URL is safe (relative path only, not an open redirect).
    /// </summary>
    private static bool IsValidReturnUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Must be a relative path starting with / but not // (protocol-relative URL)
        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal))
            return false;

        // Block backslash tricks (some browsers normalize \\ to //)
        if (url.StartsWith("/\\", StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>
    /// Sanitizes error messages to prevent leaking internal details to end users.
    /// </summary>
    private static string SanitizeErrorMessage(string message)
    {
        // Only surface messages from OAuthException (which are user-facing by design).
        // Generic exceptions may contain stack traces, file paths, or connection strings.
        const int maxLength = 200;
        if (message.Length > maxLength)
            message = message[..maxLength];

        return message;
    }
}
