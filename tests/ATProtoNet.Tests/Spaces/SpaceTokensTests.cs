using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceTokensTests
{
    private const string UserDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string AuthorityDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Space = $"at://{AuthorityDid}/space/com.atmoboards.forum/default";
    private const string HostAudience = $"{AuthorityDid}#atproto_space_host";
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private const string HolderKeyId = "did:key:zDnaehpewypegZXBqk8rh1EGpfU5c2vmdP6bXvg7sE2B2XiCW";

    private static JsonElement DecodePart(string jwt, int index) => TestJws.DecodeJson(jwt, index);

    /// <summary>
    /// Mints a delegation token without <see cref="SpaceTokens.Create"/>, so its encoding is
    /// independent of the code under test.
    /// </summary>
    private static string MintDelegation(AtProtoKey key, JwsSegments padded = JwsSegments.None)
    {
        var now = DateTimeOffset.UtcNow;
        return TestJws.Mint(
            new Dictionary<string, object>
            {
                ["typ"] = SpaceTokens.DelegationType,
                ["alg"] = "ES256",
                ["kid"] = "#atproto",
            },
            new Dictionary<string, object>
            {
                ["iss"] = UserDid,
                ["sub"] = Space,
                ["aud"] = HostAudience,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddSeconds(60).ToUnixTimeSeconds(),
                ["jti"] = "a-token-id",
            },
            input => key.Sign(input),
            padded);
    }

    /// <summary>
    /// Rewrites one claim of a minted token. The signature no longer covers it, which parsing
    /// does not check.
    /// </summary>
    private static string WithClaim(string jwt, string name, object value)
    {
        var claims = JsonSerializer.Deserialize<Dictionary<string, object>>(TestJws.Decode(jwt.Split('.')[1]))!;
        claims[name] = value;
        return TestJws.WithSegment(jwt, 1, TestJws.Encode(JsonSerializer.SerializeToUtf8Bytes(claims)));
    }

    // ── Delegation tokens ────────────────────────────────────────

    [Fact]
    public void Create_DelegationToken_HasTheSpecifiedHeaderAndClaims()
    {
        using var key = AtProtoCrypto.GenerateK256Key();

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        var header = DecodePart(jwt, 0);
        var payload = DecodePart(jwt, 1);

        Assert.Equal("atproto-space-delegation+jwt", header.GetProperty("typ").GetString());
        Assert.Equal("ES256K", header.GetProperty("alg").GetString());
        Assert.Equal("#atproto", header.GetProperty("kid").GetString());
        Assert.Equal(UserDid, payload.GetProperty("iss").GetString());
        Assert.Equal(Space, payload.GetProperty("sub").GetString());
        Assert.Equal(HostAudience, payload.GetProperty("aud").GetString());
        // A delegation token is single-use, so it must carry a nonce to be consumed by.
        Assert.NotEmpty(payload.GetProperty("jti").GetString()!);
        // It carries no lxm — that is one of the things that makes it its own credential class
        // rather than an interchangeable service auth token.
        Assert.False(payload.TryGetProperty("lxm", out _));
    }

    [Fact]
    public void Create_DelegationToken_DefaultsToSixtySeconds()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var token = SpaceTokens.Parse(
            SpaceTokenType.Delegation,
            SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience));

        Assert.Equal(60, (token.ExpiresAt - token.IssuedAt).TotalSeconds, 1);
    }

    [Fact]
    public void Create_DelegationTokenWithoutAnAudience_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<ArgumentException>(
            () => SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key));
    }

    // ── Space credentials ────────────────────────────────────────

    [Fact]
    public void Create_Credential_IsBoundToAKeyAndHasNoAudience()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key, confirmationKeyId: HolderKeyId);

        var payload = DecodePart(jwt, 1);

        Assert.Equal("atproto-space-credential+jwt", DecodePart(jwt, 0).GetProperty("typ").GetString());
        Assert.Equal(HolderKeyId, payload.GetProperty("cnf").GetProperty("kid").GetString());
        Assert.False(payload.GetProperty("cnf").TryGetProperty("jkt", out _));
        // A credential is presented to every repo host in the space, so it names no single one.
        Assert.False(payload.TryGetProperty("aud", out _));
    }

    [Fact]
    public void Create_CredentialWithoutABoundKey_Throws()
    {
        // Without the binding a credential would be a bearer token: a host given one to serve
        // its own repo could replay it against every other host in the space.
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<ArgumentException>(
            () => SpaceTokens.Create(SpaceTokenType.Credential, AuthorityDid, Space, key));
    }

    [Fact]
    public void Create_Credential_DefaultsToTenMinutes()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var token = SpaceTokens.Parse(
            SpaceTokenType.Credential,
            SpaceTokens.Create(SpaceTokenType.Credential, AuthorityDid, Space, key, confirmationKeyId: HolderKeyId));

        Assert.Equal(600, (token.ExpiresAt - token.IssuedAt).TotalSeconds, 1);
    }

    [Theory]
    [InlineData("not a did")]
    [InlineData("")]
    public void Create_CredentialBoundToSomethingThatIsNotADid_Throws(string keyId)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<ArgumentException>(() => SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key, confirmationKeyId: keyId));
    }

    [Theory]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void Create_CredentialLifetime_IsBoundedByAnHour(int seconds, bool allowed)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        void Mint() => SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key,
            confirmationKeyId: HolderKeyId, lifetime: TimeSpan.FromSeconds(seconds));

        if (allowed)
            Mint();
        else
            Assert.Throws<ArgumentOutOfRangeException>(Mint);
    }

    [Fact]
    public void Create_CredentialWithADedicatedSpaceKey_NamesItInTheKid()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key,
            confirmationKeyId: HolderKeyId, keyId: SpaceAuthority.SigningKeyId);

        Assert.Equal("#atproto_space", DecodePart(jwt, 0).GetProperty("kid").GetString());
    }

    /// <summary>Mints a credential with claims a test chooses, signed by <paramref name="key"/>.</summary>
    private static string MintCredential(AtProtoKey key, Action<Dictionary<string, object>>? edit = null)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = AuthorityDid,
            ["sub"] = Space,
            ["cnf"] = new Dictionary<string, string> { ["kid"] = HolderKeyId },
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["jti"] = "a-credential-id",
        };
        edit?.Invoke(claims);

        return TestJws.Mint(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.CredentialType, ["alg"] = "ES256", ["kid"] = "#atproto" },
            claims,
            input => key.Sign(input));
    }

    [Fact]
    public void Parse_Credential_ReadsItsKeyBindingAndTokenId()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var token = SpaceTokens.Parse(SpaceTokenType.Credential, MintCredential(key));

        Assert.Equal(HolderKeyId, token.ConfirmationKeyId);
        Assert.Equal("a-credential-id", token.TokenId);
    }

    [Theory]
    [InlineData("jkt")] // the DPoP-era binding, which names no key a signature could be checked against
    [InlineData("other")]
    public void Parse_CredentialBoundByAnythingButCnfKid_Throws(string member)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = MintCredential(key, claims => claims["cnf"] = new Dictionary<string, string> { [member] = HolderKeyId });

        var ex = Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Credential, jwt));

        Assert.Contains("cnf.kid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CredentialWithoutAnyBinding_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = MintCredential(key, claims => claims.Remove("cnf"));

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Credential, jwt));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Parse_CredentialWithoutAUsableJti_Throws(string jti)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Parse(SpaceTokenType.Credential, MintCredential(key, claims => claims["jti"] = jti)));
        Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Parse(SpaceTokenType.Credential, MintCredential(key, claims => claims.Remove("jti"))));
    }

    [Fact]
    public void Parse_CredentialWithoutIat_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Parse(SpaceTokenType.Credential, MintCredential(key, claims => claims.Remove("iat"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Parse_CredentialLifetimeOutsideTheBound_Throws(int lifetimeSeconds)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var jwt = MintCredential(key, claims =>
        {
            claims["iat"] = iat;
            claims["exp"] = iat + lifetimeSeconds;
        });

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Credential, jwt));
    }

    [Fact]
    public void Parse_CredentialLastingExactlyAnHour_IsAccepted()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var token = SpaceTokens.Parse(
            SpaceTokenType.Credential,
            MintCredential(key, claims =>
            {
                claims["iat"] = iat;
                claims["exp"] = iat + 3600;
            }));

        Assert.Equal(TimeSpan.FromHours(1), token.ExpiresAt - token.IssuedAt);
    }

    [Theory]
    [InlineData(4, true)]   // inside the 5 second skew
    [InlineData(6, false)]  // past it
    [InlineData(600, false)]
    public void Verify_CredentialIssuedInTheFuture_IsRefusedBeyondTheSkew(int secondsAhead, bool accepted)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var iat = DateTimeOffset.UtcNow.AddSeconds(secondsAhead).ToUnixTimeSeconds();
        var parsed = SpaceTokens.Parse(
            SpaceTokenType.Credential,
            MintCredential(key, claims =>
            {
                claims["iat"] = iat;
                claims["exp"] = iat + 600;
            }));

        if (accepted)
            SpaceTokens.Verify(parsed, key.ToDidKey());
        else
            Assert.Contains("future", Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(parsed, key.ToDidKey())).Message, StringComparison.Ordinal);
    }

    // ── Client attestations ──────────────────────────────────────

    [Fact]
    public void Create_ClientAttestation_UsesTheClientIdAsBothIssuerAndSubject()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var jwt = SpaceTokens.Create(
            SpaceTokenType.ClientAttestation, ClientId, ClientId, key, audience: HostAudience);

        var header = DecodePart(jwt, 0);
        var payload = DecodePart(jwt, 1);

        Assert.Equal("atproto-client-attestation+jwt", header.GetProperty("typ").GetString());
        // Its key comes from the client's published JWKS, not a DID document, so there is no
        // default kid to assume.
        Assert.False(header.TryGetProperty("kid", out _));
        Assert.Equal(ClientId, payload.GetProperty("iss").GetString());
        Assert.Equal(ClientId, payload.GetProperty("sub").GetString());
    }

    [Fact]
    public void Create_ClientAttestationWithMismatchedIssuerAndSubject_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<ArgumentException>(() => SpaceTokens.Create(
            SpaceTokenType.ClientAttestation, ClientId, Space, key, audience: HostAudience));
    }

    // ── Parsing ──────────────────────────────────────────────────

    [Fact]
    public void Parse_WrongTokenType_Throws()
    {
        // The three classes share a wire shape, so the typ header is the only thing keeping a
        // credential from being presented where a delegation token belongs.
        using var key = AtProtoCrypto.GenerateP256Key();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key, confirmationKeyId: HolderKeyId);

        var ex = Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Parse(SpaceTokenType.Delegation, credential));

        Assert.Contains("Wrong token type", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("onlyonepart")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.!!!.!!!")]
    [InlineData("e30.WzFd.AAAA")] // a payload that is not a JSON object
    public void Parse_MalformedToken_ThrowsSpaceTokenException(string jwt)
    {
        // How each segment decodes is JwtTests'; this pins that a failure is this parser's error.
        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Delegation, jwt));
    }

    [Theory]
    [InlineData("exp", 253402300800L)] // 10000-01-01, one second past what DateTimeOffset holds
    [InlineData("exp", long.MaxValue)]
    [InlineData("exp", long.MinValue)]
    [InlineData("iat", 253402300800L)]
    [InlineData("iat", -62135596801L)] // one second before 0001-01-01
    public void Parse_TimeClaimOutsideTheRepresentableRange_Throws(string claim, long seconds)
    {
        // Regression: DateTimeOffset.FromUnixTimeSeconds threw ArgumentOutOfRangeException, which
        // reached a space server's host as a 500 instead of a refusal.
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = WithClaim(MintDelegation(key), claim, seconds);

        var ex = Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Delegation, jwt));
        Assert.Contains("not a valid time", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t\t")]
    [InlineData("a\u0000b")]
    public void Parse_JtiThatCannotBeSpent_Throws(string jti)
    {
        // A replay store refuses to key on it, so it has to be refused here, not surface there.
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = WithClaim(MintDelegation(key), "jti", jti);

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Delegation, jwt));
    }

    [Fact]
    public void Parse_OverlongJti_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = WithClaim(MintDelegation(key), "jti", new string('a', 256));

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Parse(SpaceTokenType.Delegation, jwt));
    }

    [Fact]
    public void Parse_TheLastRepresentableSecond_IsAccepted()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = WithClaim(MintDelegation(key), "exp", 253402300799L);

        var token = SpaceTokens.Parse(SpaceTokenType.Delegation, jwt);

        Assert.Equal(DateTimeOffset.MaxValue.ToUnixTimeSeconds(), token.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void Create_EveryToken_CarriesA128BitHexJti()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var first = SpaceTokens.Parse(
            SpaceTokenType.Delegation,
            SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience));
        var second = SpaceTokens.Parse(
            SpaceTokenType.Delegation,
            SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience));

        Assert.Matches("^[0-9a-f]{32}$", first.TokenId!);
        Assert.NotEqual(first.TokenId, second.TokenId);
    }

    [Fact]
    public void Parse_ExposesTheSigningInputTheSignatureCovers()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        var token = SpaceTokens.Parse(SpaceTokenType.Delegation, jwt);

        var parts = jwt.Split('.');
        Assert.Equal(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"), token.SigningInput);
        Assert.True(key.Verify(token.SigningInput, token.Signature));
    }

    // ── Verification ─────────────────────────────────────────────

    private static SpaceToken Parsed(AtProtoKey key) => SpaceTokens.Parse(SpaceTokenType.Delegation, MintDelegation(key));

    [Fact]
    public void Verify_AGoodToken_ReturnsTheParsedToken()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var parsed = Parsed(key);

        var verified = SpaceTokens.Verify(parsed, key.ToDidKey(), HostAudience, SpaceUri.Parse(Space));

        Assert.Same(parsed, verified);
    }

    [Fact]
    public void Verify_WithTheWrongKey_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(Parsed(key), other.ToDidKey()));
    }

    [Fact]
    public void Verify_ForADifferentAudience_Throws()
    {
        // The audience is derived from the subject space, so a token minted for one authority
        // cannot be presented at another.
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            Parsed(key), key.ToDidKey(), expectedAudience: "did:plc:z72i7hdynmk6r22z27h6tvur#atproto_space_host"));
    }

    [Fact]
    public void Verify_ForADifferentSpace_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            Parsed(key), key.ToDidKey(),
            expectedSubject: SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other")));
    }

    [Fact]
    public void Verify_AnExpiredToken_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var parsed = Parsed(key);

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(parsed, key.ToDidKey(), now: parsed.ExpiresAt.AddMinutes(1)));
    }

    [Fact]
    public void IsExpired_AllowsForClockSkew()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var token = SpaceTokens.Parse(
            SpaceTokenType.Delegation,
            SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience));

        Assert.False(token.IsExpired(token.ExpiresAt.AddSeconds(-10)));
        Assert.True(token.IsExpired(token.ExpiresAt.AddSeconds(10)));
    }

    [Fact]
    public void Verify_ATamperedPayload_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        var parts = jwt.Split('.');
        var forged = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $$"""{"iss":"{{UserDid}}","sub":"{{Space}}","aud":"{{HostAudience}}","iat":1,"exp":99999999999,"jti":"x"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            SpaceTokens.Parse(SpaceTokenType.Delegation, $"{parts[0]}.{forged}.{parts[2]}"), key.ToDidKey()));
    }
}
