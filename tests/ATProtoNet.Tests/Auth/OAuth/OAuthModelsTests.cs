using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Auth.OAuth;

public class OAuthModelsTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    [Fact]
    public void AuthorizationServerMetadata_Deserialization()
    {
        var json = """
        {
            "issuer": "https://bsky.social",
            "authorization_endpoint": "https://bsky.social/oauth/authorize",
            "token_endpoint": "https://bsky.social/oauth/token",
            "pushed_authorization_request_endpoint": "https://bsky.social/oauth/par",
            "response_types_supported": ["code"],
            "grant_types_supported": ["authorization_code", "refresh_token"],
            "code_challenge_methods_supported": ["S256"],
            "token_endpoint_auth_methods_supported": ["none", "private_key_jwt"],
            "scopes_supported": ["atproto", "transition:generic"],
            "dpop_signing_alg_values_supported": ["ES256"],
            "authorization_response_iss_parameter_supported": true,
            "require_pushed_authorization_requests": true,
            "client_id_metadata_document_supported": true
        }
        """;

        var metadata = JsonSerializer.Deserialize<AuthorizationServerMetadata>(json, _options)!;

        Assert.Equal("https://bsky.social", metadata.Issuer);
        Assert.Equal("https://bsky.social/oauth/authorize", metadata.AuthorizationEndpoint);
        Assert.Equal("https://bsky.social/oauth/token", metadata.TokenEndpoint);
        Assert.Equal("https://bsky.social/oauth/par", metadata.PushedAuthorizationRequestEndpoint);
        Assert.Contains("code", metadata.ResponseTypesSupported);
        Assert.Contains("authorization_code", metadata.GrantTypesSupported);
        Assert.Contains("S256", metadata.CodeChallengeMethodsSupported);
        Assert.Contains("atproto", metadata.ScopesSupported);
        Assert.Contains("ES256", metadata.DpopSigningAlgValuesSupported);
        Assert.True(metadata.AuthorizationResponseIssParameterSupported);
        Assert.True(metadata.RequirePushedAuthorizationRequests);
        Assert.True(metadata.ClientIdMetadataDocumentSupported);
    }

    [Fact]
    public void ProtectedResourceMetadata_Deserialization()
    {
        var json = """
        {
            "resource": "https://bsky.social",
            "authorization_servers": ["https://bsky.social"]
        }
        """;

        var metadata = JsonSerializer.Deserialize<ProtectedResourceMetadata>(json, _options)!;

        Assert.Equal("https://bsky.social", metadata.Resource);
        Assert.Single(metadata.AuthorizationServers);
        Assert.Equal("https://bsky.social", metadata.AuthorizationServers[0]);
    }

    [Fact]
    public void OAuthClientMetadata_DefaultValues()
    {
        var metadata = new OAuthClientMetadata();

        Assert.Equal("web", metadata.ApplicationType);
        Assert.True(metadata.DpopBoundAccessTokens);
        Assert.Contains("authorization_code", metadata.GrantTypes);
        Assert.Contains("refresh_token", metadata.GrantTypes);
        Assert.Contains("code", metadata.ResponseTypes);
        Assert.Equal("atproto transition:generic", metadata.Scope);
        Assert.Equal("none", metadata.TokenEndpointAuthMethod);
    }

    [Fact]
    public void OAuthClientMetadata_Serialization()
    {
        var metadata = new OAuthClientMetadata
        {
            ClientId = "https://myapp.example.com/oauth/client-metadata.json",
            ClientName = "My App",
            ClientUri = "https://myapp.example.com",
            RedirectUris = ["https://myapp.example.com/oauth/callback"],
        };

        var json = JsonSerializer.Serialize(metadata, _options);
        var deserialized = JsonSerializer.Deserialize<OAuthClientMetadata>(json, _options)!;

        Assert.Equal(metadata.ClientId, deserialized.ClientId);
        Assert.Equal(metadata.ClientName, deserialized.ClientName);
        Assert.Equal(metadata.ClientUri, deserialized.ClientUri);
        Assert.Single(deserialized.RedirectUris);
        Assert.Equal(metadata.RedirectUris[0], deserialized.RedirectUris[0]);
    }

    [Fact]
    public void OAuthTokenResponse_Deserialization()
    {
        var json = """
        {
            "access_token": "at-token-123",
            "token_type": "DPoP",
            "refresh_token": "rt-token-456",
            "expires_in": 3600,
            "scope": "atproto transition:generic",
            "sub": "did:plc:abc123"
        }
        """;

        var response = JsonSerializer.Deserialize<OAuthTokenResponse>(json, _options)!;

        Assert.Equal("at-token-123", response.AccessToken);
        Assert.Equal("DPoP", response.TokenType);
        Assert.Equal("rt-token-456", response.RefreshToken);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("atproto transition:generic", response.Scope);
        Assert.Equal("did:plc:abc123", response.Sub);
    }

    [Fact]
    public void PushedAuthorizationResponse_Deserialization()
    {
        var json = """
        {
            "request_uri": "urn:ietf:params:oauth:request_uri:abc123",
            "expires_in": 60
        }
        """;

        var response = JsonSerializer.Deserialize<PushedAuthorizationResponse>(json, _options)!;

        Assert.Equal("urn:ietf:params:oauth:request_uri:abc123", response.RequestUri);
        Assert.Equal(60, response.ExpiresIn);
    }

    [Fact]
    public void OAuthErrorResponse_Deserialization()
    {
        var json = """
        {
            "error": "invalid_request",
            "error_description": "Missing required parameter"
        }
        """;

        var response = JsonSerializer.Deserialize<OAuthErrorResponse>(json, _options)!;

        Assert.Equal("invalid_request", response.Error);
        Assert.Equal("Missing required parameter", response.ErrorDescription);
    }

    [Fact]
    public void DidDocument_Deserialization()
    {
        var json = """
        {
            "id": "did:plc:abc123",
            "alsoKnownAs": ["at://alice.bsky.social"],
            "service": [
                {
                    "id": "#atproto_pds",
                    "type": "AtprotoPersonalDataServer",
                    "serviceEndpoint": "https://pds.example.com"
                }
            ]
        }
        """;

        var doc = JsonSerializer.Deserialize<DidDocument>(json, _options)!;

        Assert.Equal("did:plc:abc123", doc.Id);
        Assert.Single(doc.AlsoKnownAs!);
        Assert.Equal("at://alice.bsky.social", doc.AlsoKnownAs![0]);
        Assert.Single(doc.Service!);
        Assert.Equal("#atproto_pds", doc.Service![0].Id);
        Assert.Equal("AtprotoPersonalDataServer", doc.Service![0].Type);
        Assert.Equal("https://pds.example.com", doc.Service![0].ServiceEndpoint);
    }

    [Fact]
    public void OAuthOptions_Defaults()
    {
        var options = new OAuthOptions();

        Assert.Equal("atproto transition:generic", options.Scope);
        Assert.Equal("https://bsky.social", options.DefaultPdsUrl);
        Assert.NotNull(options.ClientMetadata);
    }

    [Fact]
    public void OAuthAuthorizationState_Defaults()
    {
        var state = new OAuthAuthorizationState();

        Assert.Equal(string.Empty, state.State);
        Assert.Equal(string.Empty, state.CodeVerifier);
        Assert.Null(state.ExpectedDid);
        Assert.Equal(string.Empty, state.Issuer);
        Assert.Equal(string.Empty, state.TokenEndpoint);
        Assert.Equal(string.Empty, state.PdsUrl);
        Assert.Equal(string.Empty, state.DpopKeyId);
        Assert.Equal(string.Empty, state.RedirectUri);
        Assert.Equal(string.Empty, state.ClientId);
        Assert.True(state.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void OAuthException_HasErrorCode()
    {
        var ex = new OAuthException("Test error", "test_error");

        Assert.Equal("Test error", ex.Message);
        Assert.Equal("test_error", ex.ErrorCode);
    }

    [Fact]
    public void OAuthException_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new OAuthException("Test error", "test_error", inner);

        Assert.Equal("Test error", ex.Message);
        Assert.Equal("test_error", ex.ErrorCode);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void OAuthSessionResult_Dispose_DisposeDPoP()
    {
        var dpop = new DPoPProofGenerator();
        var session = new OAuthSessionResult
        {
            Did = "did:plc:test",
            Handle = "test.bsky.social",
            AccessToken = "token",
            PdsUrl = "https://pds.example.com",
            DPoP = dpop,
        };

        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            dpop.GenerateProof("POST", "https://example.com"));
    }

    [Fact]
    public void JsonWebKey_Properties()
    {
        var jwk = new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = "base64url-x",
            Y = "base64url-y",
            Kid = "key-id",
            Use = "sig",
            Alg = "ES256",
        };

        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.Equal("base64url-x", jwk.X);
        Assert.Equal("base64url-y", jwk.Y);
        Assert.Equal("key-id", jwk.Kid);
        Assert.Equal("sig", jwk.Use);
        Assert.Equal("ES256", jwk.Alg);
    }

    [Fact]
    public void JsonWebKeySet_Properties()
    {
        var jwks = new JsonWebKeySet
        {
            Keys = [new JsonWebKey { Kty = "EC", Crv = "P-256" }],
        };

        Assert.Single(jwks.Keys);
        Assert.Equal("EC", jwks.Keys[0].Kty);
    }
}
