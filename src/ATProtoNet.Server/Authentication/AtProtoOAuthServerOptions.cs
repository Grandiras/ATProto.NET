using System.Security.Claims;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Options for the hosted AT Protocol OAuth login, which signs users in with a cookie. Use with
/// <see cref="AtProtoOAuthExtensions.WithOAuth"/> and <see cref="AtProtoOAuthExtensions.MapAtProtoOAuth"/>.
/// </summary>
/// <remarks>
/// The handle resolution budget and the development opt-out for private networks come from
/// <c>AddAtProtoIdentity</c>'s options; the requests to authorization servers go through the named
/// client <see cref="AtProtoOAuthExtensions.HttpClientName"/>.
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddAtProto().WithOAuth(options =>
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
    /// The route prefix of the login endpoints: <c>{RoutePrefix}/login</c>, <c>/callback</c>,
    /// <c>/relay</c> and <c>/logout</c>. Default: <c>/atproto</c>.
    /// </summary>
    public string RoutePrefix { get; set; } = "/atproto";

    /// <summary>
    /// The cookie authentication scheme to sign in with, as registered with
    /// <c>AddAuthentication().AddCookie()</c>. Default: <c>Cookies</c>.
    /// </summary>
    public string CookieScheme { get; set; } = "Cookies";

    /// <summary>Where a login returns to when it names no local <c>returnUrl</c>. Default: <c>/</c>.</summary>
    public string DefaultReturnUrl { get; set; } = "/";

    /// <summary>Where a logout redirects to. Default: <c>/</c>.</summary>
    public string PostLogoutRedirectUri { get; set; } = "/";

    /// <summary>
    /// Where a failed login redirects to, with an <c>error</c> code (the
    /// <see cref="OAuthException.Error"/>, the authorization server's error, or
    /// <c>login_failed</c>), never an exception message. Default: <c>/login</c>.
    /// </summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>
    /// The OAuth scopes to request, which must include <c>atproto</c> (see
    /// <see cref="AtProtoScopes"/>). Default: <see cref="AtProtoScopes.Default"/>.
    /// </summary>
    public string Scopes { get; set; } = AtProtoScopes.Default;

    /// <summary>The application name shown on the authorization server's consent page.</summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// The application's base URL, e.g. <c>https://myapp.example.com</c>; the callback is then
    /// <c>{BaseUrl}{RoutePrefix}/callback</c>.
    /// </summary>
    /// <remarks>
    /// Without it, a client with <see cref="ClientMetadata"/> uses the registered redirect URI on
    /// the request's origin (or the first one), and the development loopback client uses the
    /// server's plain HTTP address on <c>127.0.0.1</c>.
    /// </remarks>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The client metadata of a published <c>client_id</c>, required in production. Without it,
    /// development loopback metadata is generated.
    /// </summary>
    public OAuthClientMetadata? ClientMetadata { get; set; }

    /// <summary>
    /// The keys of a confidential client (<c>private_key_jwt</c>); empty for a public client.
    /// </summary>
    /// <remarks>
    /// <para>With keys, <see cref="ClientMetadata"/> is required and must declare
    /// <c>token_endpoint_auth_method</c> <c>private_key_jwt</c>,
    /// <c>token_endpoint_auth_signing_alg</c> <c>ES256</c>, and the public keys, inline as
    /// <see cref="OAuthClientMetadata.Jwks"/> (see <see cref="OAuthClientKey.CreateKeySet"/>) or
    /// at <see cref="OAuthClientMetadata.JwksUri"/>, which <see cref="ServeClientMetadata"/> can
    /// serve.</para>
    /// <para>A session stays bound to the key that authorized it, so keep retired keys here until
    /// their sessions are gone; new logins use the first key. The service does not dispose them.</para>
    /// </remarks>
    public IList<OAuthClientKey> ClientKeys { get; } = new List<OAuthClientKey>();

    /// <summary>
    /// Whether <see cref="AtProtoOAuthExtensions.MapAtProtoOAuth"/> also serves
    /// <see cref="ClientMetadata"/> at the path of its <c>client_id</c>, and the public halves of
    /// <see cref="ClientKeys"/> at the path of its <see cref="OAuthClientMetadata.JwksUri"/>.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool ServeClientMetadata { get; set; }

    /// <summary>
    /// Builds the claims of a signed-in user from the session. Default: the DID as
    /// <see cref="ClaimTypes.NameIdentifier"/>, the handle as <see cref="ClaimTypes.Name"/>, and the
    /// <see cref="AtProtoClaimTypes"/> <c>did</c>, <c>handle</c>, <c>handle_verified</c>,
    /// <c>pds_url</c> and <c>auth_method</c>.
    /// </summary>
    /// <remarks>
    /// Keep a <see cref="AtProtoClaimTypes.Did"/> (or <see cref="ClaimTypes.NameIdentifier"/>)
    /// claim: the client factory and sign-out find the user's session by it.
    /// </remarks>
    public Func<ATProtoNet.Auth.OAuthSession, IEnumerable<Claim>>? ClaimsFactory { get; set; }

    /// <summary>How long the authentication cookie lasts. Default: 7 days.</summary>
    public TimeSpan CookieExpiration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Whether the authentication cookie outlives the browser session. Default: <see langword="true"/>.</summary>
    public bool IsPersistent { get; set; } = true;
}
