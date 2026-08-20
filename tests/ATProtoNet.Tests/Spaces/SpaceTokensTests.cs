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
    private const string Thumbprint = "0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I";

    private static JsonElement DecodePart(string jwt, int index)
    {
        var part = jwt.Split('.')[index].Replace('-', '+').Replace('_', '/');
        var padding = (4 - (part.Length % 4)) % 4;
        var bytes = Convert.FromBase64String(part + new string('=', padding));
        return JsonSerializer.Deserialize<JsonElement>(bytes);
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
            SpaceTokenType.Credential, AuthorityDid, Space, key, dpopThumbprint: Thumbprint);

        var payload = DecodePart(jwt, 1);

        Assert.Equal("atproto-space-credential+jwt", DecodePart(jwt, 0).GetProperty("typ").GetString());
        Assert.Equal(Thumbprint, payload.GetProperty("cnf").GetProperty("jkt").GetString());
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
    public void Create_Credential_DefaultsToTwoHours()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var token = SpaceTokens.Parse(
            SpaceTokenType.Credential,
            SpaceTokens.Create(SpaceTokenType.Credential, AuthorityDid, Space, key, dpopThumbprint: Thumbprint));

        Assert.Equal(7200, (token.ExpiresAt - token.IssuedAt).TotalSeconds, 1);
    }

    [Fact]
    public void Create_CredentialWithADedicatedSpaceKey_NamesItInTheKid()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space, key,
            dpopThumbprint: Thumbprint, keyId: SpaceAuthority.SigningKeyId);

        Assert.Equal("#atproto_space", DecodePart(jwt, 0).GetProperty("kid").GetString());
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
            SpaceTokenType.Credential, AuthorityDid, Space, key, dpopThumbprint: Thumbprint);

        var ex = Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Parse(SpaceTokenType.Delegation, credential));

        Assert.Contains("Wrong token type", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not.a.jwt.at.all")]
    [InlineData("onlyonepart")]
    [InlineData("!!!.!!!.!!!")]
    public void TryParse_Malformed_ReturnsFalse(string jwt)
    {
        Assert.False(SpaceTokens.TryParse(SpaceTokenType.Delegation, jwt, out _));
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

    [Fact]
    public void ToSpaceUri_ParsesTheSubjectAsASpace()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var token = SpaceTokens.Parse(
            SpaceTokenType.Delegation,
            SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience));

        Assert.Equal(SpaceUri.Parse(Space), token.ToSpaceUri());
    }

    // ── Verification ─────────────────────────────────────────────

    [Fact]
    public void Verify_AGoodToken_Succeeds()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        var token = SpaceTokens.Verify(
            SpaceTokenType.Delegation, jwt, key.ToDidKey(), HostAudience, Space);

        Assert.Equal(UserDid, token.Issuer);
    }

    [Fact]
    public void Verify_WithTheWrongKey_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        Assert.Throws<SpaceTokenException>(
            () => SpaceTokens.Verify(SpaceTokenType.Delegation, jwt, other.ToDidKey()));
    }

    [Fact]
    public void Verify_ForADifferentAudience_Throws()
    {
        // The audience is derived from the subject space, so a token minted for one authority
        // cannot be presented at another.
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            SpaceTokenType.Delegation, jwt, key.ToDidKey(),
            expectedAudience: "did:plc:z72i7hdynmk6r22z27h6tvur#atproto_space_host"));
    }

    [Fact]
    public void Verify_ForADifferentSpace_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key, audience: HostAudience);

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            SpaceTokenType.Delegation, jwt, key.ToDidKey(),
            expectedSubject: $"at://{AuthorityDid}/space/com.atmoboards.forum/other"));
    }

    [Fact]
    public void Verify_AnExpiredToken_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space, key,
            audience: HostAudience, lifetime: TimeSpan.FromSeconds(1));

        Assert.Throws<SpaceTokenException>(() => SpaceTokens.Verify(
            SpaceTokenType.Delegation, jwt, key.ToDidKey(), now: DateTimeOffset.UtcNow.AddMinutes(5)));
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
            SpaceTokenType.Delegation, $"{parts[0]}.{forged}.{parts[2]}", key.ToDidKey()));
    }
}
