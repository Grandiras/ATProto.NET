using System.Security.Claims;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Blazor.Authentication;

/// <summary>
/// Options for configuring AT Protocol OAuth server-side authentication with cookie integration.
/// Use with <see cref="AtProtoAuthenticationExtensions.AddAtProtoAuthentication"/> and
/// <see cref="AtProtoAuthenticationExtensions.MapAtProtoOAuth"/>.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddAtProtoAuthentication(options =>
/// {
///     options.ClientName = "My App";
///     options.CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
///     options.Scopes = "atproto transition:generic";
/// });
/// app.MapAtProtoOAuth();
/// </code>
/// </example>
public sealed class AtProtoOAuthServerOptions
{
    /// <summary>
    /// The route prefix for AT Proto OAuth endpoints.
    /// Default: "/atproto".
    /// Mapped endpoints: <c>{RoutePrefix}/login</c>, <c>{RoutePrefix}/callback</c>, <c>{RoutePrefix}/logout</c>.
    /// </summary>
    public string RoutePrefix { get; set; } = "/atproto";

    /// <summary>
    /// The cookie authentication scheme to sign in with after successful OAuth.
    /// Must match the scheme configured in <c>AddAuthentication().AddCookie()</c>.
    /// Default: "Cookies" (<c>CookieAuthenticationDefaults.AuthenticationScheme</c>).
    /// </summary>
    public string CookieScheme { get; set; } = "Cookies";

    /// <summary>
    /// Default return URL after successful login when no <c>returnUrl</c> query parameter is provided.
    /// Default: "/".
    /// </summary>
    public string DefaultReturnUrl { get; set; } = "/";

    /// <summary>
    /// URL to redirect to after logout.
    /// Default: "/".
    /// </summary>
    public string PostLogoutRedirectUri { get; set; } = "/";

    /// <summary>
    /// Path to redirect to when OAuth errors occur (e.g., callback failures).
    /// An <c>error</c> query parameter is appended with the error message.
    /// Default: "/login".
    /// </summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>
    /// OAuth scopes to request. Must include "atproto".
    /// Default: "atproto transition:generic".
    /// </summary>
    public string Scopes { get; set; } = "atproto transition:generic";

    /// <summary>
    /// Optional application name shown on the authorization server's consent page.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Optional explicit base URL for the application (e.g., "https://myapp.example.com").
    /// When set, this is used to construct the OAuth callback URL instead of detecting from the request.
    /// Useful behind reverse proxies or in production deployments.
    /// When not set, the callback URL is auto-detected from the incoming HTTP request.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional explicit OAuth client metadata. When provided, this is used directly
    /// instead of auto-generating loopback client metadata.
    /// Required for production deployments with a registered client_id URL.
    /// </summary>
    public OAuthClientMetadata? ClientMetadata { get; set; }

    /// <summary>
    /// Optional callback to customize the claims created from the OAuth result.
    /// When not set, default claims are generated: <c>NameIdentifier</c> (DID),
    /// <c>Name</c> (handle), <c>did</c>, <c>handle</c>, <c>pds_url</c>, <c>auth_method</c>.
    /// </summary>
    public Func<OAuthSessionResult, IEnumerable<Claim>>? ClaimsFactory { get; set; }

    /// <summary>
    /// Expiration duration for the authentication cookie.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan CookieExpiration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether the authentication cookie should persist across browser sessions.
    /// Default: true.
    /// </summary>
    public bool IsPersistent { get; set; } = true;
}
