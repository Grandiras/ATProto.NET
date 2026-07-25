using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Authorization Server metadata as defined by RFC 8414 and the AT Protocol OAuth spec.
/// Fetched from <c>/.well-known/oauth-authorization-server</c>.
/// </summary>
public sealed class AuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("pushed_authorization_request_endpoint")]
    public string PushedAuthorizationRequestEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public List<string> ResponseTypesSupported { get; set; } = [];

    [JsonPropertyName("grant_types_supported")]
    public List<string> GrantTypesSupported { get; set; } = [];

    [JsonPropertyName("code_challenge_methods_supported")]
    public List<string> CodeChallengeMethodsSupported { get; set; } = [];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public List<string> TokenEndpointAuthMethodsSupported { get; set; } = [];

    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    public List<string> TokenEndpointAuthSigningAlgValuesSupported { get; set; } = [];

    [JsonPropertyName("scopes_supported")]
    public List<string> ScopesSupported { get; set; } = [];

    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public List<string> DpopSigningAlgValuesSupported { get; set; } = [];

    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public bool AuthorizationResponseIssParameterSupported { get; set; }

    [JsonPropertyName("require_pushed_authorization_requests")]
    public bool RequirePushedAuthorizationRequests { get; set; }

    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; set; }

    [JsonPropertyName("require_request_uri_registration")]
    public bool? RequireRequestUriRegistration { get; set; }

    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; set; }
}

/// <summary>
/// Protected Resource (PDS) metadata as defined by draft-ietf-oauth-resource-metadata.
/// Fetched from <c>/.well-known/oauth-protected-resource</c>.
/// </summary>
public sealed class ProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

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
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("application_type")]
    public string ApplicationType { get; set; } = "web";

    [JsonPropertyName("client_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoUri { get; set; }

    [JsonPropertyName("tos_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("dpop_bound_access_tokens")]
    public bool DpopBoundAccessTokens { get; set; } = true;

    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = [];

    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = ["code"];

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = AtProtoScopes.Default;

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "none";

    [JsonPropertyName("token_endpoint_auth_signing_alg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenEndpointAuthSigningAlg { get; set; }

    [JsonPropertyName("jwks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonWebKeySet? Jwks { get; set; }

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
    [JsonPropertyName("keys")]
    public List<JsonWebKey> Keys { get; set; } = [];
}

/// <summary>
/// A JSON Web Key (JWK).
/// </summary>
public sealed class JsonWebKey
{
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = string.Empty;

    [JsonPropertyName("crv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Crv { get; set; }

    [JsonPropertyName("x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? X { get; set; }

    [JsonPropertyName("y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Y { get; set; }

    [JsonPropertyName("kid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kid { get; set; }

    [JsonPropertyName("use")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Use { get; set; }

    [JsonPropertyName("alg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alg { get; set; }
}

/// <summary>
/// Response from a Pushed Authorization Request (PAR).
/// </summary>
public sealed class PushedAuthorizationResponse
{
    [JsonPropertyName("request_uri")]
    public string RequestUri { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// OAuth token response from the token endpoint.
/// </summary>
public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("sub")]
    public string? Sub { get; set; }
}

/// <summary>
/// Error response from OAuth endpoints.
/// </summary>
public sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// DID document as returned from a DID resolution.
/// Simplified model capturing fields needed for PDS discovery.
/// </summary>
public sealed class DidDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; }

    [JsonPropertyName("service")]
    public List<DidService>? Service { get; set; }
}

/// <summary>
/// A service endpoint in a DID document.
/// </summary>
public sealed class DidService
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

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
