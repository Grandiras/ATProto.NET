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
/// DID document as returned from a DID resolution.
/// Simplified model capturing fields needed for PDS discovery.
/// </summary>
public sealed class DidDocument
{
    /// <summary>The DID this document describes.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The <c>alsoKnownAs</c> entries (handles) for the DID document.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; }

    /// <summary>The services declared by the DID document.</summary>
    [JsonPropertyName("service")]
    public List<DidService>? Service { get; set; }
}

/// <summary>
/// A service endpoint in a DID document.
/// </summary>
public sealed class DidService
{
    /// <summary>
    /// The identifier of the service within the DID document (for example <c>#atproto_pds</c>).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The service type (for example <c>AtprotoPersonalDataServer</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>The endpoint URL of the service.</summary>
    [JsonPropertyName("serviceEndpoint")]
    public string ServiceEndpoint { get; set; } = string.Empty;
}

/// <summary>
/// Options for configuring the AT Protocol OAuth client.
/// </summary>
public sealed class OAuthOptions
{
    /// <summary>
    /// The OAuth client metadata. The <c>client_id</c> must be a fully-qualified HTTPS URL
    /// at which the client metadata JSON document can be fetched by Authorization Servers.
    /// </summary>
    public OAuthClientMetadata ClientMetadata { get; set; } = new();

    /// <summary>
    /// The scopes to request. Must include "atproto".
    /// Default: <see cref="AtProtoScopes.Default"/> ("atproto transition:generic")
    /// </summary>
    public string Scope { get; set; } = AtProtoScopes.Default;

    /// <summary>
    /// Default PDS URL shown in the login form. Users can override this.
    /// Default: "https://bsky.social"
    /// </summary>
    public string DefaultPdsUrl { get; set; } = "https://bsky.social";

    /// <summary>
    /// Budget for each handle resolution round during discovery. Keeps a handle
    /// domain that silently drops traffic (parked apex, firewall) from stalling the
    /// login flow for the full <see cref="HttpClient.Timeout"/>.
    /// Default: <see cref="AuthorizationServerDiscovery.DefaultHandleResolutionTimeout"/>
    /// (5 seconds). Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan HandleResolutionTimeout { get; set; } =
        AuthorizationServerDiscovery.DefaultHandleResolutionTimeout;
}

/// <summary>
/// Represents a pending OAuth authorization that is awaiting callback.
/// </summary>
public sealed class OAuthAuthorizationState
{
    /// <summary>Unique state parameter for CSRF protection.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>PKCE code verifier (raw secret).</summary>
    public string CodeVerifier { get; init; } = string.Empty;

    /// <summary>The resolved DID of the user, if known (when starting from handle).</summary>
    public string? ExpectedDid { get; init; }

    /// <summary>The Authorization Server issuer URL.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>The token endpoint URL.</summary>
    public string TokenEndpoint { get; init; } = string.Empty;

    /// <summary>The PDS (Resource Server) URL.</summary>
    public string PdsUrl { get; init; } = string.Empty;

    /// <summary>The DPoP key thumbprint bound to this session.</summary>
    public string DpopKeyId { get; init; } = string.Empty;

    /// <summary>Timestamp when this state was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The redirect URI used for this authorization.</summary>
    public string RedirectUri { get; init; } = string.Empty;

    /// <summary>The client ID used.</summary>
    public string ClientId { get; init; } = string.Empty;
}
