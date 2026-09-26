using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;
using ATProtoNet.Tests.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Confidential clients (<c>private_key_jwt</c>): an ES256 client assertion in the RFC 7523 shape
/// on the pushed authorization, token, refresh and revocation requests, signed by the key the
/// session was issued to; and the default public client, which sends none.
/// </summary>
public sealed class ConfidentialClientTests : IDisposable
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    // RFC 7517 appendix A.2: the example P-256 private key (also indigo's JWK parsing fixture).
    private const string Rfc7517X = "MKBCTNIcKUSDii11ySs3526iDZ8AiTo7Tu6KPAqv7D4";
    private const string Rfc7517Y = "4Etl6SRW2YiLUrN5vfvVHuhp7x8PxltmWWlbbM4IFyM";
    private const string Rfc7517D = "870MB6gfuTJ4HtUnUvYMyJpr5eUZNP4Bk43bVdj3eAE";

    private readonly StubServer _server = new();
    private readonly HttpClient _http;
    private readonly List<IDisposable> _owned = [];

    public ConfidentialClientTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        _http.Dispose();
        _server.Dispose();
    }

    private OAuthClientKey Key(string keyId)
    {
        var key = OAuthClientKey.Generate(keyId);
        _owned.Add(key);
        return key;
    }

    private static OAuthClientMetadata ConfidentialMetadata(params OAuthClientKey[] published) => new()
    {
        ClientId = ClientId,
        RedirectUris = [RedirectUri],
        TokenEndpointAuthMethod = "private_key_jwt",
        TokenEndpointAuthSigningAlg = "ES256",
        Jwks = OAuthClientKey.CreateKeySet(published),
    };

    private OAuthClient Confidential(params OAuthClientKey[] keys)
    {
        var client = new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = ConfidentialMetadata(keys),
                ClientKeys = keys,
                HttpClient = _http,
                IdentityResolver = AliceIdentity(),
            },
            NullLogger.Instance)
        {
            NonceCache = new DPoPNonceCache(),
        };
        _owned.Add(client);
        return client;
    }

    /// <summary>
    /// Checks a request's client authentication against RFC 7523 section 3 as AT Protocol uses it,
    /// verifying the signature with the published public key, and returns the assertion's claims.
    /// </summary>
    private static JsonElement AssertClientAssertion(StubRequest request, JsonWebKey publicKey, string audience)
    {
        Assert.Equal(ClientId, request.Form["client_id"]);
        Assert.Equal(AssertionType, request.Form["client_assertion_type"]);
        Assert.True(Jwt.TryDecode(request.Form["client_assertion"], out var jwt, out var error), error);

        // alg and kid, as the reference client sends: no typ.
        Assert.Equal(["alg", "kid"], jwt.Header.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("ES256", jwt.Header.GetProperty("alg").GetString());
        Assert.Equal(publicKey.Kid, jwt.Header.GetProperty("kid").GetString());

        var claims = jwt.Payload;
        Assert.Equal(ClientId, claims.GetProperty("iss").GetString());
        Assert.Equal(ClientId, claims.GetProperty("sub").GetString());
        Assert.Equal(audience, claims.GetProperty("aud").GetString());
        Assert.Matches("^[0-9a-f]{32}$", claims.GetProperty("jti").GetString());
        var issuedAt = claims.GetProperty("iat").GetInt64();
        Assert.InRange(
            issuedAt,
            DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds());
        Assert.Equal(issuedAt + 60, claims.GetProperty("exp").GetInt64());

        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url.DecodeFromChars(publicKey.X), Y = Base64Url.DecodeFromChars(publicKey.Y) },
        });
        Assert.True(verifier.VerifyData(
            jwt.SigningInput, jwt.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        return claims;
    }

    private static void AssertNoClientAssertion(StubRequest request)
    {
        Assert.Equal(ClientId, request.Form["client_id"]);
        Assert.False(request.Form.ContainsKey("client_assertion"));
        Assert.False(request.Form.ContainsKey("client_assertion_type"));
    }

    // ──────────────────────────────────────────────────────────
    //  The flow
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_ThePushedRequestAndTheExchangeCarryFreshAssertions()
    {
        _server.Respond = AuthorizationServer;
        var key = Key("key-1");
        var oauth = Confidential(key);

        var (session, _) = await SignInAsync(oauth);

        var par = AssertClientAssertion(_server.To("/oauth/par").Single(), key.PublicJwk, Issuer);
        var token = AssertClientAssertion(_server.To("/oauth/token").Single(), key.PublicJwk, Issuer);
        Assert.NotEqual(par.GetProperty("jti").GetString(), token.GetProperty("jti").GetString());
        Assert.Equal("key-1", session.ClientKeyId);
    }

    [Fact]
    public async Task ANonceRetry_SendsANewAssertion()
    {
        var calls = 0;
        _server.Respond = r =>
        {
            if (r.Path == "/oauth/par" && Interlocked.Increment(ref calls) == 1)
            {
                var challenge = OAuthError("use_dpop_nonce");
                challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "n-1");
                return challenge;
            }

            return AuthorizationServer(r);
        };
        var key = Key("key-1");

        await SignInAsync(Confidential(key));

        var attempts = _server.To("/oauth/par");
        Assert.Equal(2, attempts.Count);
        Assert.NotEqual(
            AssertClientAssertion(attempts[0], key.PublicJwk, Issuer).GetProperty("jti").GetString(),
            AssertClientAssertion(attempts[1], key.PublicJwk, Issuer).GetProperty("jti").GetString());
    }

    [Fact]
    public async Task RefreshAndRevocation_AreSignedByTheKeyTheSessionWasIssuedTo()
    {
        // Rotation: new authorizations use the first key, older sessions keep theirs.
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath ? TokenResponse("at-2", "rt-2") : AuthorizationServer(r);
        var current = Key("key-2026");
        var previous = Key("key-2025");
        var oauth = Confidential(current, previous);
        var session = OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint) with { ClientKeyId = "key-2025" };

        var refreshed = await oauth.RefreshAsync(session);
        await oauth.RevokeAsync(refreshed);

        AssertClientAssertion(_server.To(TokenEndpoint.AbsolutePath).Single(), previous.PublicJwk, Issuer);
        AssertClientAssertion(_server.To(RevocationEndpoint.AbsolutePath).Single(), previous.PublicJwk, Issuer);
        Assert.Equal("key-2025", refreshed.ClientKeyId);

        var (fresh, _) = await SignInAsync(oauth);
        Assert.Equal("key-2026", fresh.ClientKeyId);
    }

    [Fact]
    public async Task ASessionWhoseKeyIsNoLongerConfigured_IsNotSent()
    {
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        var oauth = Confidential(Key("key-2026"));
        var session = OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint) with { ClientKeyId = "key-2024" };

        var refresh = await Assert.ThrowsAsync<OAuthException>(() => oauth.RefreshAsync(session));
        var revoke = await Assert.ThrowsAsync<OAuthException>(() => oauth.RevokeAsync(session));

        Assert.Equal("client_key_unavailable", refresh.Error);
        Assert.Equal("client_key_unavailable", revoke.Error);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task APublicClientsSession_IsStillSentAsAPublicClients()
    {
        // A session issued before the client became confidential was bound to no key.
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        var oauth = Confidential(Key("key-1"));

        await oauth.RefreshAsync(OAuthSession(NewDPoPKey()));

        AssertNoClientAssertion(_server.To(TokenEndpoint.AbsolutePath).Single());
    }

    [Fact]
    public async Task AServerThatDoesNotAcceptPrivateKeyJwt_IsRefusedBeforeThePushedRequest()
    {
        _server.ServesOAuthMetadata = false;
        _server.Respond = r => r.Path switch
        {
            "/.well-known/oauth-protected-resource" =>
                JsonResponse($$"""{"resource":"https://pds.example.com","authorization_servers":["{{Issuer}}"]}"""),
            "/.well-known/oauth-authorization-server" => JsonResponse(AuthorizationServerMetadataJson(privateKeyJwt: false)),
            _ => AuthorizationServer(r),
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => SignInAsync(Confidential(Key("key-1"))));

        Assert.Equal("unsupported_client_auth", ex.Error);
        Assert.Empty(_server.To("/oauth/par"));
    }

    [Fact]
    public async Task APublicClient_SendsOnlyItsClientId()
    {
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath && r.Form["grant_type"] == "refresh_token"
            ? TokenResponse("at-2", "rt-2")
            : AuthorizationServer(r);
        using var oauth = OAuthClient(_http);

        var (session, _) = await SignInAsync(oauth);
        var refreshed = await oauth.RefreshAsync(session);
        await oauth.RevokeAsync(refreshed);

        Assert.Null(session.ClientKeyId);
        Assert.Equal(4, _server.Requests.Count);
        Assert.All(_server.Requests, AssertNoClientAssertion);
    }

    // ──────────────────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────────────────

    public static TheoryData<string, Func<OAuthClientKey, OAuthOptions>> Misconfigurations => new()
    {
        {
            "keys for a public client",
            key => new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata { ClientId = ClientId },
                ClientKeys = [key],
            }
        },
        {
            "no keys",
            key => new OAuthOptions { ClientMetadata = ConfidentialMetadata(key) }
        },
        {
            "another signing algorithm",
            key => new OAuthOptions
            {
                ClientMetadata = WithAlg(ConfidentialMetadata(key), "ES256K"),
                ClientKeys = [key],
            }
        },
        {
            "no published keys",
            key => new OAuthOptions
            {
                ClientMetadata = WithoutJwks(ConfidentialMetadata(key)),
                ClientKeys = [key],
            }
        },
        {
            "a key the metadata does not publish",
            key => new OAuthOptions
            {
                ClientMetadata = ConfidentialMetadata(),
                ClientKeys = [key],
            }
        },
        {
            "an unsupported authentication method",
            key => new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, TokenEndpointAuthMethod = "client_secret_basic" },
            }
        },
    };

    [Theory]
    [MemberData(nameof(Misconfigurations))]
    public void Constructor_InconsistentClientAuthentication_Throws(string because, Func<OAuthClientKey, OAuthOptions> options)
    {
        Assert.NotEmpty(because);
        Assert.Throws<ArgumentException>(() => new OAuthClient(options(Key("key-1"))));
    }

    [Fact]
    public void Constructor_TwoKeysWithOneKeyId_Throws()
    {
        var first = Key("key-1");
        var second = Key("key-1");

        Assert.Throws<ArgumentException>(() => new OAuthClient(new OAuthOptions
        {
            ClientMetadata = ConfidentialMetadata(first, second),
            ClientKeys = [first, second],
        }));
    }

    [Fact]
    public void Constructor_KeysPublishedAtAJwksUri_AreAccepted()
    {
        var key = Key("key-1");

        using var client = new OAuthClient(new OAuthOptions
        {
            ClientMetadata = WithoutJwks(ConfidentialMetadata(key), jwksUri: "https://app.example.com/jwks.json"),
            ClientKeys = [key],
        });
    }

    private static OAuthClientMetadata WithAlg(OAuthClientMetadata metadata, string alg)
    {
        metadata.TokenEndpointAuthSigningAlg = alg;
        return metadata;
    }

    private static OAuthClientMetadata WithoutJwks(OAuthClientMetadata metadata, string? jwksUri = null)
    {
        metadata.Jwks = null;
        metadata.JwksUri = jwksUri;
        return metadata;
    }

    // ──────────────────────────────────────────────────────────
    //  Keys
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Import_TheRfc7517ExampleKey_PublishesItsPublicJwk()
    {
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars(Rfc7517D),
            Q = new ECPoint { X = Base64Url.DecodeFromChars(Rfc7517X), Y = Base64Url.DecodeFromChars(Rfc7517Y) },
        });
        using var key = OAuthClientKey.Import("rfc7517", ecdsa.ExportPkcs8PrivateKey());

        var jwk = key.PublicJwk;

        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.Equal(Rfc7517X, jwk.X);
        Assert.Equal(Rfc7517Y, jwk.Y);
        Assert.Equal("rfc7517", jwk.Kid);
        Assert.Equal("sig", jwk.Use);
        Assert.Equal("ES256", jwk.Alg);
    }

    [Fact]
    public void CreateAssertion_VerifiesAgainstTheRfc7517ExamplePublicKey()
    {
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars(Rfc7517D),
            Q = new ECPoint { X = Base64Url.DecodeFromChars(Rfc7517X), Y = Base64Url.DecodeFromChars(Rfc7517Y) },
        });
        using var key = OAuthClientKey.Import("rfc7517", ecdsa.ExportPkcs8PrivateKey());
        var published = new JsonWebKey { Kty = "EC", Crv = "P-256", X = Rfc7517X, Y = Rfc7517Y, Kid = "rfc7517" };

        var assertion = key.CreateAssertion(ClientId, Issuer, DateTimeOffset.UtcNow);
        var request = new StubRequest(
            "POST", TokenEndpoint, null, null, new Dictionary<string, string>(),
            $"client_id={Uri.EscapeDataString(ClientId)}&client_assertion_type={Uri.EscapeDataString(AssertionType)}&client_assertion={assertion}");

        AssertClientAssertion(request, published, Issuer);
    }

    [Fact]
    public void ExportPrivateKey_ImportsAsTheSameKey()
    {
        var generated = Key("key-1");

        using var imported = OAuthClientKey.Import("key-1", generated.ExportPrivateKey());

        Assert.Equal(generated.PublicJwk.X, imported.PublicJwk.X);
        Assert.Equal(generated.PublicJwk.Y, imported.PublicJwk.Y);
    }

    [Fact]
    public void Import_AK256Key_Throws()
    {
        using var k256 = AtProtoCrypto.GenerateK256Key();

        Assert.Throws<ArgumentException>(() => OAuthClientKey.Import("key-1", k256.ExportPrivateKey()));
    }

    [Fact]
    public void CreateKeySet_SerializesThePublicHalvesOnly()
    {
        var key = Key("key-1");

        var json = new OAuthClientMetadata { ClientId = ClientId, Jwks = OAuthClientKey.CreateKeySet([key]) }.ToJson();
        var published = JsonDocument.Parse(json).RootElement.GetProperty("jwks").GetProperty("keys")[0];

        Assert.Equal(["alg", "crv", "kid", "kty", "use", "x", "y"], published.EnumerateObject().Select(p => p.Name).Order());
    }
}
