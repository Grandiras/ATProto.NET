using System.Security.Claims;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Options for the hosted AT Protocol OAuth login, which signs users in with a cookie.
/// Use with <see cref="AtProtoOAuthExtensions.AddAtProtoAuthentication"/> and
/// <see cref="AtProtoOAuthExtensions.MapAtProtoOAuth"/>.
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
    /// Mapped endpoints: <c>{RoutePrefix}/login</c>, <c>{RoutePrefix}/callback</c>,
    /// <c>{RoutePrefix}/relay</c> and <c>{RoutePrefix}/logout</c>.
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
    /// Path to redirect to when a login fails, with an <c>error</c> query parameter carrying a
    /// short error code: the <see cref="OAuthException.Error"/> of an OAuth failure (such as
    /// <c>invalid_handle</c> or <c>login_not_bound</c>), the authorization server's error (such
    /// as <c>access_denied</c>), or <c>login_failed</c> for anything else. Never an exception
    /// message. Default: "/login".
    /// </summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>
    /// OAuth scopes to request. Must include "atproto".
    /// Default: <see cref="AtProtoScopes.Default"/> ("atproto transition:generic").
    /// Use <see cref="AtProtoScopes"/> constants to compose scope values.
    /// </summary>
    public string Scopes { get; set; } = AtProtoScopes.Default;

    /// <summary>
    /// Optional application name shown on the authorization server's consent page.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Optional explicit base URL for the application (e.g., "https://myapp.example.com").
    /// When set, the OAuth callback URL is <c>{BaseUrl}{RoutePrefix}/callback</c>.
    /// </summary>
    /// <remarks>
    /// Without it, a client with <see cref="ClientMetadata"/> uses the registered redirect URI on
    /// the request's origin (or the first one), and the development loopback client uses the
    /// server's plain HTTP address on <c>127.0.0.1</c>.
    /// </remarks>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional explicit OAuth client metadata. When provided, this is used directly
    /// instead of auto-generating loopback client metadata.
    /// Required for production deployments with a registered client_id URL.
    /// </summary>
    public OAuthClientMetadata? ClientMetadata { get; set; }

    /// <summary>
    /// The keys of a confidential client (<c>private_key_jwt</c>), which authenticate every
    /// pushed authorization, token, refresh and revocation request with a client assertion.
    /// Empty (the default) for a public client.
    /// </summary>
    /// <remarks>
    /// <para>With keys, <see cref="ClientMetadata"/> is required and must declare
    /// <c>token_endpoint_auth_method</c> <c>private_key_jwt</c>,
    /// <c>token_endpoint_auth_signing_alg</c> <c>ES256</c>, and the public keys, inline as
    /// <see cref="OAuthClientMetadata.Jwks"/> (see <see cref="OAuthClientKey.CreateKeySet"/>) or
    /// at <see cref="OAuthClientMetadata.JwksUri"/>, which <see cref="ServeClientMetadata"/> can
    /// serve. The client checks this when it is built.</para>
    /// <para>A session stays bound to the key that authorized it, so keep retired keys here until
    /// their sessions are gone; new logins use the first key. The service does not dispose them.</para>
    /// </remarks>
    public IList<OAuthClientKey> ClientKeys { get; } = new List<OAuthClientKey>();

    /// <summary>
    /// Whether <see cref="AtProtoOAuthExtensions.MapAtProtoOAuth"/> also serves the client's
    /// public documents: <see cref="ClientMetadata"/> at the path of its <c>client_id</c> (for
    /// example <c>/oauth-client-metadata.json</c>), and, when the metadata names a
    /// <see cref="OAuthClientMetadata.JwksUri"/>, the public halves of <see cref="ClientKeys"/>
    /// at that URL's path. Default: <see langword="false"/>.
    /// </summary>
    public bool ServeClientMetadata { get; set; }

    /// <summary>
    /// Optional callback to customize the claims created from the OAuth session.
    /// When not set, default claims are generated: <see cref="ClaimTypes.NameIdentifier"/>
    /// (DID), <see cref="ClaimTypes.Name"/> (handle), and the <see cref="AtProtoClaimTypes"/>
    /// <c>did</c>, <c>handle</c>, <c>handle_verified</c>, <c>pds_url</c> and <c>auth_method</c>.
    /// </summary>
    /// <remarks>
    /// Keep a <see cref="AtProtoClaimTypes.Did"/> (or <see cref="ClaimTypes.NameIdentifier"/>)
    /// claim: the client factory and sign-out find the user's session by it.
    /// </remarks>
    public Func<ATProtoNet.Auth.OAuthSession, IEnumerable<Claim>>? ClaimsFactory { get; set; }

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

    /// <summary>
    /// Optional <see cref="System.Net.Http.HttpClient"/> to use for OAuth discovery and
    /// token requests. When set, the caller owns its lifetime (it is not disposed with
    /// the service) and its <see cref="System.Net.Http.HttpClient.Timeout"/> is left
    /// untouched — use this to plug in an <c>IHttpClientFactory</c> client, a proxy, or
    /// custom handlers. It is used as is, so it is also the caller's to keep from reaching
    /// private addresses (see <see cref="OAuthOptions.HttpClient"/>). When not set, the SDK
    /// creates one under its identity fetch policy and applies <see cref="HttpClientTimeout"/>.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Timeout applied to the SDK-created <see cref="System.Net.Http.HttpClient"/> used
    /// for OAuth requests. Ignored when <see cref="HttpClient"/> is supplied.
    /// Default: 30 seconds (the <see cref="System.Net.Http.HttpClient"/> default of
    /// 100 seconds is far longer than any browser or reverse proxy will wait during login).
    /// </summary>
    public TimeSpan HttpClientTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Budget for resolving a handle: its HTTPS well-known and DNS TXT lookups run together
    /// within it. Prevents a handle whose domain silently drops traffic on port 443 from
    /// stalling sign-in.
    /// Default: <see cref="AuthorizationServerDiscovery.DefaultHandleResolutionTimeout"/>
    /// (5 seconds). Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan HandleResolutionTimeout { get; set; } =
        AuthorizationServerDiscovery.DefaultHandleResolutionTimeout;

    /// <summary>
    /// The development opt-out for OAuth discovery and identity resolution: plain HTTP and private
    /// addresses are accepted, for a local PDS or PLC. Defaults to <see langword="false"/>. Never
    /// set it where users can name any handle, DID or PDS. A registered
    /// <see cref="ATProtoNet.Identity.IIdentityResolver"/> brings its own policy.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }
}
