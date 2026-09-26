using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Authorization Server metadata as defined by RFC 8414 and the AT Protocol OAuth spec.
/// Fetched from <c>/.well-known/oauth-authorization-server</c>.
/// </summary>
public sealed class AuthorizationServerMetadata
{
    /// <summary>The issuer identifier of the authorization server.</summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>URL of the authorization endpoint.</summary>
    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    /// <summary>URL of the token endpoint.</summary>
    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>URL of the pushed authorization request (PAR) endpoint.</summary>
    [JsonPropertyName("pushed_authorization_request_endpoint")]
    public string PushedAuthorizationRequestEndpoint { get; set; } = string.Empty;

    /// <summary>The response types the authorization server supports.</summary>
    [JsonPropertyName("response_types_supported")]
    public List<string> ResponseTypesSupported { get; set; } = [];

    /// <summary>The grant types the authorization server supports.</summary>
    [JsonPropertyName("grant_types_supported")]
    public List<string> GrantTypesSupported { get; set; } = [];

    /// <summary>The PKCE code challenge methods supported.</summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public List<string> CodeChallengeMethodsSupported { get; set; } = [];

    /// <summary>The client authentication methods the token endpoint supports.</summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public List<string> TokenEndpointAuthMethodsSupported { get; set; } = [];

    /// <summary>The signing algorithms accepted for client authentication assertions.</summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    public List<string> TokenEndpointAuthSigningAlgValuesSupported { get; set; } = [];

    /// <summary>The scopes the authorization server supports.</summary>
    [JsonPropertyName("scopes_supported")]
    public List<string> ScopesSupported { get; set; } = [];

    /// <summary>The signing algorithms accepted for DPoP proofs.</summary>
    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public List<string> DpopSigningAlgValuesSupported { get; set; } = [];

    /// <summary>
    /// Whether the authorization server returns the <c>iss</c> parameter in responses.
    /// </summary>
    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public bool AuthorizationResponseIssParameterSupported { get; set; }

    /// <summary>Whether the authorization server requires pushed authorization requests.</summary>
    [JsonPropertyName("require_pushed_authorization_requests")]
    public bool RequirePushedAuthorizationRequests { get; set; }

    /// <summary>
    /// Whether the authorization server supports AT Protocol client-ID metadata documents.
    /// </summary>
    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; set; }

    /// <summary>Whether request URIs must be pre-registered.</summary>
    [JsonPropertyName("require_request_uri_registration")]
    public bool? RequireRequestUriRegistration { get; set; }

    /// <summary>URL of the token revocation endpoint, if offered.</summary>
    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; set; }

    /// <summary>
    /// The protected resources the authorization server serves, when it lists them (RFC 9728
    /// section 4); a PDS it does not list is not one it authorizes for.
    /// </summary>
    [JsonPropertyName("protected_resources")]
    public List<string>? ProtectedResources { get; set; }
}

/// <summary>
/// Protected Resource (PDS) metadata as defined by draft-ietf-oauth-resource-metadata.
/// Fetched from <c>/.well-known/oauth-protected-resource</c>.
/// </summary>
public sealed class ProtectedResourceMetadata
{
    /// <summary>The identifier of the protected resource.</summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    /// <summary>The issuers of the authorization servers that protect this resource.</summary>
    [JsonPropertyName("authorization_servers")]
    public List<string> AuthorizationServers { get; set; } = [];
}

/// <summary>
/// OAuth client metadata document as defined by draft-parecki-oauth-client-id-metadata-document.
/// The <c>client_id</c> is the URL at which this document is served.
/// </summary>
/// <remarks>
/// Optional properties are annotated with
/// <see cref="JsonIgnoreAttribute"/> (<see cref="JsonIgnoreCondition.WhenWritingNull"/>) so that
/// serializing this type — with any <see cref="JsonSerializerOptions"/>, including the ASP.NET
/// Core defaults used by <c>Results.Json</c> — omits unset fields instead of writing JSON
/// <c>null</c>. Authorization servers distinguish absent from null and reject a document that
/// contains, for example, <c>"jwks_uri": null</c> with <c>invalid_client_metadata</c>.
/// Use <see cref="ToJson"/> to render the document directly.
/// </remarks>
public sealed class OAuthClientMetadata
{
    /// <summary>
    /// The OAuth client identifier — for AT Protocol, the URL the client metadata document is
    /// served from.
    /// </summary>
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client application type (<c>web</c> or <c>native</c>).</summary>
    [JsonPropertyName("application_type")]
    public string ApplicationType { get; set; } = "web";

    /// <summary>Human-readable name of the client, shown on the consent screen.</summary>
    [JsonPropertyName("client_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; set; }

    /// <summary>URL of the client's home page.</summary>
    [JsonPropertyName("client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientUri { get; set; }

    /// <summary>URL of the client's logo, shown on the consent screen.</summary>
    [JsonPropertyName("logo_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoUri { get; set; }

    /// <summary>URL of the client's terms of service.</summary>
    [JsonPropertyName("tos_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TosUri { get; set; }

    /// <summary>URL of the client's privacy policy.</summary>
    [JsonPropertyName("policy_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PolicyUri { get; set; }

    /// <summary>
    /// Whether access tokens are DPoP-bound. Always <see langword="true"/> for AT Protocol.
    /// </summary>
    [JsonPropertyName("dpop_bound_access_tokens")]
    public bool DpopBoundAccessTokens { get; set; } = true;

    /// <summary>The OAuth grant types the client uses.</summary>
    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = ["authorization_code", "refresh_token"];

    /// <summary>The redirect URIs the client may be sent back to.</summary>
    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>The OAuth response types the client uses.</summary>
    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = ["code"];

    /// <summary>The space-separated OAuth scopes.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = AtProtoScopes.Default;

    /// <summary>
    /// How the client authenticates to the token endpoint (<c>none</c> or <c>private_key_jwt</c>).
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "none";

    /// <summary>
    /// The signing algorithm used for <c>private_key_jwt</c> client authentication.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary>The client's public keys, embedded inline.</summary>
    [JsonPropertyName("jwks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonWebKeySet? Jwks { get; set; }

    /// <summary>URL the client's public keys are served from.</summary>
    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; set; }

    /// <summary>
    /// Serializes this document to the JSON that must be served at the <c>client_id</c> URL.
    /// Unset optional fields are omitted rather than written as <c>null</c>, as authorization
    /// servers require.
    /// </summary>
    /// <param name="writeIndented">Whether to pretty-print the JSON. Default: <c>false</c>.</param>
    /// <returns>The client-metadata document as a JSON string.</returns>
    public string ToJson(bool writeIndented = false)
        => JsonSerializer.Serialize(this, writeIndented ? IndentedJsonOptions : JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}

/// <summary>
/// JSON Web Key Set wrapper.
/// </summary>
public sealed class JsonWebKeySet
{
    /// <summary>The keys in the set.</summary>
    [JsonPropertyName("keys")]
    public List<JsonWebKey> Keys { get; set; } = [];
}

/// <summary>
/// A JSON Web Key (JWK).
/// </summary>
public sealed class JsonWebKey
{
    /// <summary>The key type (<c>EC</c>, <c>RSA</c>, …).</summary>
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = string.Empty;

    /// <summary>The elliptic curve the key is on (for example <c>P-256</c>).</summary>
    [JsonPropertyName("crv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Crv { get; set; }

    /// <summary>The base64url-encoded x coordinate of an elliptic-curve key.</summary>
    [JsonPropertyName("x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? X { get; set; }

    /// <summary>The base64url-encoded y coordinate of an elliptic-curve key.</summary>
    [JsonPropertyName("y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Y { get; set; }

    /// <summary>The key identifier.</summary>
    [JsonPropertyName("kid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kid { get; set; }

    /// <summary>The intended use of the key (for example <c>sig</c>).</summary>
    [JsonPropertyName("use")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Use { get; set; }

    /// <summary>The algorithm the key is used with (for example <c>ES256</c>).</summary>
    [JsonPropertyName("alg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alg { get; set; }
}

/// <summary>
/// Response from a Pushed Authorization Request (PAR).
/// </summary>
public sealed class PushedAuthorizationResponse
{
    /// <summary>The request URI to pass to the authorization endpoint.</summary>
    [JsonPropertyName("request_uri")]
    public string RequestUri { get; set; } = string.Empty;

    /// <summary>Lifetime of the token in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// OAuth token response from the token endpoint.
/// </summary>
public sealed class OAuthTokenResponse
{
    /// <summary>The access token.</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The token type; always <c>DPoP</c> for AT Protocol.</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    /// <summary>The refresh token, when the server issues one.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>Lifetime of the token in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    /// <summary>The space-separated OAuth scopes.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>The DID of the authenticated account.</summary>
    [JsonPropertyName("sub")]
    public string? Sub { get; set; }
}

/// <summary>
/// Error response from OAuth endpoints.
/// </summary>
public sealed class OAuthErrorResponse
{
    /// <summary>The OAuth error code (for example <c>invalid_grant</c>).</summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    /// <summary>A human-readable description of the error.</summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Options for configuring the AT Protocol OAuth client.
/// </summary>
public sealed class OAuthOptions
{
    /// <summary>
    /// The OAuth client metadata. The <c>client_id</c> must be a fully-qualified HTTPS URL
    /// at which the client metadata JSON document can be fetched by Authorization Servers;
    /// by convention <c>https://{host}/oauth-client-metadata.json</c>, which consent screens show
    /// as the bare host.
    /// </summary>
    /// <remarks>
    /// A confidential client sets <see cref="OAuthClientMetadata.TokenEndpointAuthMethod"/> to
    /// <c>private_key_jwt</c> and <see cref="OAuthClientMetadata.TokenEndpointAuthSigningAlg"/>
    /// to <c>ES256</c>, publishes its keys as <see cref="OAuthClientMetadata.Jwks"/> or at
    /// <see cref="OAuthClientMetadata.JwksUri"/>, and supplies them in <see cref="ClientKeys"/>.
    /// </remarks>
    public OAuthClientMetadata ClientMetadata { get; set; } = new();

    /// <summary>
    /// The scopes to request. Must include "atproto".
    /// Default: <see cref="AtProtoScopes.Default"/> ("atproto transition:generic"), the legacy
    /// broad grant; prefer granular scopes or a preset from <see cref="AtProtoScopes.Presets"/>.
    /// </summary>
    public string Scope { get; set; } = AtProtoScopes.Default;

    /// <summary>
    /// Budget for each handle resolution round during discovery. Keeps a handle
    /// domain that silently drops traffic (parked apex, firewall) from stalling the
    /// login flow for the full <see cref="System.Net.Http.HttpClient.Timeout"/>.
    /// Default: <see cref="AuthorizationServerDiscovery.DefaultHandleResolutionTimeout"/>
    /// (5 seconds). Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan HandleResolutionTimeout { get; set; } =
        AuthorizationServerDiscovery.DefaultHandleResolutionTimeout;

    /// <summary>
    /// Resolves the identities the flow handles: the handle or DID a login starts from, and the
    /// account the tokens are issued for. When <see langword="null"/> (the default), the client
    /// creates one with <see cref="Identity.IdentityResolver.CreateDefault"/>, applying
    /// <see cref="HandleResolutionTimeout"/>; a supplied resolver brings its own timeouts. Supply
    /// a shared one to reuse its DID document cache, or one with
    /// <see cref="Identity.IdentityResolverOptions.AllowPrivateNetworks"/> for a local PDS and PLC.
    /// </summary>
    public Identity.IIdentityResolver? IdentityResolver { get; set; }

    /// <summary>
    /// The development opt-out for every request the client makes to a PDS or an authorization
    /// server (metadata, pushed authorization, token, refresh and revocation), and for the
    /// identity resolver it creates when <see cref="IdentityResolver"/> is <see langword="null"/>:
    /// plain HTTP and private addresses are accepted, for a local PDS. Defaults to
    /// <see langword="false"/>. See <see cref="Identity.IdentityResolverOptions.AllowPrivateNetworks"/>.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// The client every request to a PDS or an authorization server goes through: the
    /// protected-resource and authorization-server metadata, and the pushed authorization, token,
    /// refresh and revocation requests. Used as is and never disposed by the client.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (the default) the requests go through the SDK's identity fetch
    /// policy: the PDS URL comes from a DID document anyone can write and the endpoints from that
    /// PDS's authorization server, so only public addresses are reached, over HTTPS, without
    /// following redirects. Supply a client only if it is already safe for such URLs, or to route
    /// through a proxy you control. The URL rules (HTTPS, no query or fragment) and the response
    /// size caps apply either way.
    /// </remarks>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// The longest one pushed authorization, token, refresh or revocation request may take,
    /// reading the response included, retry included. Default: 30 seconds.
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it, leaving only the caller's token.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> stops applying once a response's headers
    /// have arrived, so without this budget a server that stalls mid-body would hold the request
    /// open for as long as the caller waits.
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Where pending authorizations wait for their callbacks. When <see langword="null"/> (the
    /// default), the client keeps them in an <see cref="InMemoryOAuthStateStore"/> of its own;
    /// several instances of an application, or one that may restart during a login, need a
    /// shared store such as <see cref="DistributedCacheOAuthStateStore"/>.
    /// </summary>
    public IOAuthStateStore? StateStore { get; set; }

    /// <summary>
    /// The keys a confidential (<c>private_key_jwt</c>) client signs its client assertions with.
    /// New authorizations use the first; a session keeps using the key it was issued with
    /// (<see cref="Auth.OAuthSession.ClientKeyId"/>), so a key being rotated out stays listed until
    /// its sessions have ended. Empty for a public client. The keys are the caller's and are not
    /// disposed with the client.
    /// </summary>
    public IList<OAuthClientKey> ClientKeys { get; set; } = [];
}

/// <summary>
/// Options for one <see cref="OAuthClient.StartAuthorizationAsync"/> call.
/// </summary>
public sealed class OAuthAuthorizationOptions
{
    /// <summary>
    /// The server to sign in at, skipping resolution of the identifier, which is still sent as
    /// the login hint: a PDS, or an authorization server (such as an entryway) that serves no
    /// protected-resource metadata.
    /// </summary>
    public string? ServerUrl { get; set; }

    /// <summary>
    /// Who is signing in, typically derived from the remote address of the request that started
    /// the flow with <see cref="RequesterIdFor"/>. The state store limits how many pending
    /// authorizations one requester can hold, so one address cannot crowd out everyone else's
    /// logins; without it only the store's overall bound applies.
    /// </summary>
    /// <remarks>
    /// Behind a reverse proxy the remote address is the proxy's unless the application restores
    /// the client's (in ASP.NET Core, <c>UseForwardedHeaders</c>); otherwise every user shares
    /// one requester's limit.
    /// </remarks>
    public string? RequesterId { get; set; }

    /// <summary>
    /// Application data kept with the pending authorization and handed back by
    /// <see cref="OAuthClient.CompleteAuthorizationWithAppStateAsync"/>, such as where to send the
    /// user afterwards. It stays on the server, in the state store; at most
    /// <see cref="MaxAppStateLength"/> characters.
    /// </summary>
    public string? AppState { get; set; }

    /// <summary>The longest <see cref="AppState"/> accepted.</summary>
    public const int MaxAppStateLength = 4096;

    /// <summary>
    /// The requester id for a remote address: an IPv4 address as is (an IPv4-mapped IPv6 address
    /// as its IPv4 form), and an IPv6 address by its /64 prefix, the block a single subscriber is
    /// usually given, so one subscriber cannot mint unlimited requesters.
    /// </summary>
    /// <param name="remoteAddress">The remote address, or <see langword="null"/> when unknown.</param>
    /// <returns>The requester id, or <see langword="null"/> for an unknown address.</returns>
    public static string? RequesterIdFor(System.Net.IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
            return null;

        if (remoteAddress.IsIPv4MappedToIPv6)
            remoteAddress = remoteAddress.MapToIPv4();

        if (remoteAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            return remoteAddress.ToString();

        Span<byte> bytes = stackalloc byte[16];
        remoteAddress.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new System.Net.IPAddress(bytes)}/64";
    }
}

/// <summary>
/// A completed authorization: the session, and the application data the authorization was
/// started with.
/// </summary>
/// <param name="Session">The session.</param>
/// <param name="AppState">The <see cref="OAuthAuthorizationOptions.AppState"/> the authorization was started with.</param>
public sealed record OAuthAuthorizationResult(OAuthSession Session, string? AppState);

/// <summary>
/// A started authorization: where to send the user, and the state its callback will carry.
/// </summary>
/// <param name="AuthorizationUrl">The authorization server's authorization endpoint, with the
/// pushed request's <c>request_uri</c> and the <c>client_id</c>. Redirect the user to its
/// <see cref="Uri.AbsoluteUri"/>.</param>
/// <param name="State">The <c>state</c> parameter the callback will carry back.</param>
/// <param name="ExpiresAt">When the authorization stops being accepted; a callback after it fails with <c>state_expired</c>.</param>
public sealed record OAuthAuthorizationRequest(Uri AuthorizationUrl, string State, DateTimeOffset ExpiresAt);
