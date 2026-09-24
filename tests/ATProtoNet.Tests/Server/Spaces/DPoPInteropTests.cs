using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The SDK's client-side <see cref="DPoPProofGenerator"/> against its server-side
/// <see cref="DPoPProofValidator"/>, and the validator against the proofs published in RFC 9449.
/// </summary>
/// <remarks>
/// The two sides are written separately, so nothing else checks that a proof one mints is a
/// proof the other accepts: same <c>htu</c> normalization, same RFC 7638 thumbprint, same
/// <c>ath</c>.
/// </remarks>
public class DPoPInteropTests
{
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";
    private const string Credential = "eyJ0eXAiOiJhdHByb3RvLXNwYWNlLWNyZWRlbnRpYWwrand0In0.e30.c2ln";

    // RFC 9449 section 7.1 (Figure 13): a proof presented with a DPoP-bound access token, and
    // that token. Its key's thumbprint is the jkt of the section 6.1 example.
    private const string RfcAccessToken = "Kz~8mXK1EalYznwH-LC-1fBAo.4Ljp~zsPE_NeO.gxU";
    private const string RfcThumbprint = "0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I";

    private const string RfcResourceProof =
        "eyJ0eXAiOiJkcG9wK2p3dCIsImFsZyI6IkVTMjU2IiwiandrIjp7Imt0eSI6IkVDIiwieCI6Imw4dEZyaHgtMzR0VjNoUklDUkRZOXpDa0RscEJoRjQyVVFVZldWQVdCRnMiLCJ5IjoiOVZFNGpmX09rX282NHpiVFRsY3VOSmFqSG10NnY5VERWclUwQ2R2R1JEQSIsImNydiI6IlAtMjU2In19"
        + ".eyJqdGkiOiJlMWozVl9iS2ljOC1MQUVCIiwiaHRtIjoiR0VUIiwiaHR1IjoiaHR0cHM6Ly9yZXNvdXJjZS5leGFtcGxlLm9yZy9wcm90ZWN0ZWRyZXNvdXJjZSIsImlhdCI6MTU2MjI2MjYxOCwiYXRoIjoiZlVIeU8ycjJaM0RaNTNFc05yV0JiMHhXWG9hTnk1OUlpS0NBcWtzbVFFbyJ9"
        + ".2oW9RP35yRqzhrtNP86L-Ey71EOptxRimPPToA1plemAgR6pxHF8y6-yqyVnmcw6Fy1dqd-jfxSYoMxhAJpLjA";

    private const string RfcResourceUrl = "https://resource.example.org/protectedresource";
    private const long RfcResourceIssuedAt = 1562262618;

    // RFC 9449 section 4.1 (Figure 2): a proof for a token request, which carries no ath.
    private const string RfcTokenRequestProof =
        "eyJ0eXAiOiJkcG9wK2p3dCIsImFsZyI6IkVTMjU2IiwiandrIjp7Imt0eSI6IkVDIiwieCI6Imw4dEZyaHgtMzR0VjNoUklDUkRZOXpDa0RscEJoRjQyVVFVZldWQVdCRnMiLCJ5IjoiOVZFNGpmX09rX282NHpiVFRsY3VOSmFqSG10NnY5VERWclUwQ2R2R1JEQSIsImNydiI6IlAtMjU2In19"
        + ".eyJqdGkiOiItQndDM0VTYzZhY2MybFRjIiwiaHRtIjoiUE9TVCIsImh0dSI6Imh0dHBzOi8vc2VydmVyLmV4YW1wbGUuY29tL3Rva2VuIiwiaWF0IjoxNTYyMjYyNjE2fQ"
        + ".2-GxA6T8lP4vfrg8v-FdWP0A0zdrj8igiMLvqRMUvwnQg4PtFLbdLXiOSsX0x7NVY-FNyJK70nfbV37xRZT3Lg";

    private const string RfcTokenUrl = "https://server.example.com/token";
    private const long RfcTokenIssuedAt = 1562262616;

    private static DPoPProofValidator CreateValidator(TimeProvider? clock = null) =>
        new(new InMemorySpaceReplayStore(), new SpaceServerOptions(), clock);

    // ── Client generator → server validator ──────────────────────

    [Fact]
    public async Task GeneratedProof_WithBoundThumbprintAndAccessToken_Validates()
    {
        using var generator = new DPoPProofGenerator();

        var proof = await CreateValidator().ValidateAsync(
            generator.GenerateProofWithAccessToken("GET", Url, nonce: null, Credential),
            "GET",
            Url,
            boundThumbprint: generator.KeyThumbprint,
            accessToken: Credential);

        // The server derives the thumbprint from the proof's embedded JWK on its own, so the two
        // RFC 7638 implementations agree.
        Assert.Equal(generator.KeyThumbprint, proof.KeyThumbprint);
        Assert.Equal("GET", proof.Method);
        Assert.Equal(Url, proof.Uri);
        Assert.NotNull(proof.AccessTokenHash);
    }

    [Fact]
    public async Task GeneratedProof_WithoutAccessToken_ValidatesAsACredentialExchangeProof()
    {
        using var generator = new DPoPProofGenerator();

        var proof = await CreateValidator().ValidateAsync(
            generator.GenerateProof("POST", Url, nonce: "server-nonce"), "POST", Url);

        Assert.Equal(generator.KeyThumbprint, proof.KeyThumbprint);
        Assert.Equal("server-nonce", proof.Nonce);
        Assert.Null(proof.AccessTokenHash);
    }

    [Fact]
    public async Task GeneratedProof_ForAUrlWithAQuery_ValidatesAgainstTheRequest()
    {
        using var generator = new DPoPProofGenerator();
        var withQuery = Url + "?space=at://did:plc:a/space/com.example.t/s&repo=did:plc:b#frag";

        var proof = await CreateValidator().ValidateAsync(
            generator.GenerateProofWithAccessToken("get", withQuery, nonce: null, Credential),
            "GET",
            withQuery,
            boundThumbprint: generator.KeyThumbprint,
            accessToken: Credential);

        Assert.Equal(Url, proof.Uri);
    }

    [Fact]
    public async Task GeneratedProof_ForAUrlWithUserinfo_NamesAndMatchesTheUrlWithoutIt()
    {
        // Both sides normalize htu the same way, so the claim the client sends is already the
        // form the server compares, and a stricter verifier comparing it verbatim agrees too.
        using var generator = new DPoPProofGenerator();

        var proof = await CreateValidator().ValidateAsync(
            generator.GenerateProofWithAccessToken(
                "GET", "https://user:secret@pds.example.com/xrpc/com.atproto.space.getRecord", null, Credential),
            "GET",
            Url,
            boundThumbprint: generator.KeyThumbprint,
            accessToken: Credential);

        Assert.Equal(Url, proof.Uri);
    }

    [Fact]
    public async Task GeneratedProof_ForAnotherAccessToken_IsRejected()
    {
        using var generator = new DPoPProofGenerator();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => CreateValidator().ValidateAsync(
            generator.GenerateProofWithAccessToken("GET", Url, nonce: null, "another-credential"),
            "GET",
            Url,
            boundThumbprint: generator.KeyThumbprint,
            accessToken: Credential));

        Assert.Contains("ath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedProof_WhenTheCredentialIsBoundToAnotherKey_IsRejected()
    {
        using var holder = new DPoPProofGenerator();
        using var attacker = new DPoPProofGenerator();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => CreateValidator().ValidateAsync(
            attacker.GenerateProofWithAccessToken("GET", Url, nonce: null, Credential),
            "GET",
            Url,
            boundThumbprint: holder.KeyThumbprint,
            accessToken: Credential));

        Assert.Contains("not bound", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedProof_AfterExportAndImport_ValidatesUnderTheOriginalBinding()
    {
        using var original = new DPoPProofGenerator();
        using var resumed = new DPoPProofGenerator(original.ExportPrivateKey());

        var proof = await CreateValidator().ValidateAsync(
            resumed.GenerateProofWithAccessToken("GET", Url, nonce: null, Credential),
            "GET",
            Url,
            boundThumbprint: original.KeyThumbprint,
            accessToken: Credential);

        Assert.Equal(original.KeyThumbprint, proof.KeyThumbprint);
    }

    [Fact]
    public async Task GeneratedProofs_EachCarryAFreshJti()
    {
        using var generator = new DPoPProofGenerator();
        var validator = CreateValidator();

        await validator.ValidateAsync(generator.GenerateProof("GET", Url), "GET", Url);
        await validator.ValidateAsync(generator.GenerateProof("GET", Url), "GET", Url);
    }

    // ── RFC 9449 examples → server validator ─────────────────────

    [Fact]
    public async Task RfcResourceRequestProof_Validates()
    {
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(RfcResourceIssuedAt + 1));

        var proof = await CreateValidator(clock).ValidateAsync(
            RfcResourceProof,
            "GET",
            RfcResourceUrl,
            boundThumbprint: RfcThumbprint,
            accessToken: RfcAccessToken);

        Assert.Equal(RfcThumbprint, proof.KeyThumbprint);
        Assert.Equal("e1j3V_bKic8-LAEB", proof.TokenId);
    }

    [Fact]
    public async Task RfcResourceRequestProof_PresentedWithAnotherAccessToken_IsRejected()
    {
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(RfcResourceIssuedAt + 1));

        await Assert.ThrowsAsync<SpaceVerificationException>(() => CreateValidator(clock).ValidateAsync(
            RfcResourceProof,
            "GET",
            RfcResourceUrl,
            boundThumbprint: RfcThumbprint,
            accessToken: RfcAccessToken + "x"));
    }

    [Fact]
    public async Task RfcTokenRequestProof_Validates()
    {
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(RfcTokenIssuedAt + 1));

        var proof = await CreateValidator(clock).ValidateAsync(RfcTokenRequestProof, "POST", RfcTokenUrl);

        Assert.Equal(RfcThumbprint, proof.KeyThumbprint);
        Assert.Null(proof.AccessTokenHash);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
