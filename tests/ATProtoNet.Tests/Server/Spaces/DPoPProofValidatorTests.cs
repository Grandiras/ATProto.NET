using ATProtoNet.Server.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

public class DPoPProofValidatorTests
{
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";

    private static DPoPProofValidator CreateValidator(
        ISpaceReplayStore? replayStore = null, SpaceServerOptions? options = null) =>
        new(replayStore ?? new InMemorySpaceReplayStore(), options ?? new SpaceServerOptions());

    [Fact]
    public async Task ValidateAsync_ValidProof_ReturnsThumbprintOfEmbeddedKey()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var proof = await validator.ValidateAsync(key.Proof("GET", Url), "GET", Url);

        Assert.Equal(key.Thumbprint, proof.KeyThumbprint);
        Assert.Equal("GET", proof.Method);
    }

    [Fact]
    public async Task ValidateAsync_QueryStringOnRequest_StillMatchesProofWithoutIt()
    {
        // RFC 9449 compares htu against the request URI with query and fragment removed, so one
        // proof covers any query on a path — which is what lets a client mint a proof before it
        // has built the query string.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var proof = await validator.ValidateAsync(
            key.Proof("GET", Url), "GET", Url + "?space=at://did:plc:a/space/com.example.t/s&repo=did:plc:b");

        Assert.Equal(key.Thumbprint, proof.KeyThumbprint);
    }

    [Fact]
    public async Task ValidateAsync_ProofForAnotherHost_IsRejected()
    {
        // The whole point of DPoP here: a proof captured by one repo host cannot be replayed by
        // it against a different host in the same space.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var minted = key.Proof("GET", "https://other.example.com/xrpc/com.atproto.space.getRecord");

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_ProofForAnotherMethod_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(key.Proof("POST", Url), "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_SameProofTwice_IsRejectedTheSecondTime()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();
        var minted = key.Proof("GET", Url);

        await validator.ValidateAsync(minted, "GET", Url);

        var replay = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url));
        Assert.Contains("already been used", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_StaleProof_IsRejected()
    {
        using var key = new TestDPoPKey();
        var options = new SpaceServerOptions { ProofLifetime = TimeSpan.FromMinutes(5) };
        var validator = CreateValidator(options: options);

        var minted = key.Proof("GET", Url, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url));
        Assert.Contains("aged out", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_FutureDatedProof_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var minted = key.Proof("GET", Url, issuedAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url));
        Assert.Contains("future", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_KeyOtherThanTheCredentialIsBoundTo_IsRejected()
    {
        // An attacker holding a captured credential can mint proofs all day; what they cannot do
        // is mint one whose key matches the credential's cnf.jkt.
        using var bound = new TestDPoPKey();
        using var attacker = new TestDPoPKey();
        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(
                attacker.Proof("GET", Url), "GET", Url, boundThumbprint: bound.Thumbprint));

        Assert.Contains("not bound", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_MatchingThumbprint_IsAccepted()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var proof = await validator.ValidateAsync(
            key.Proof("GET", Url), "GET", Url, boundThumbprint: key.Thumbprint);

        Assert.Equal(key.Thumbprint, proof.KeyThumbprint);
    }

    [Fact]
    public async Task ValidateAsync_ProofMintedForAnotherCredential_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var minted = key.Proof("GET", Url, accessToken: "credential-one");

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url, accessToken: "credential-two"));

        Assert.Contains("ath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_MissingAthWhenCredentialPresented_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(key.Proof("GET", Url), "GET", Url, accessToken: "a-credential"));
    }

    [Fact]
    public async Task ValidateAsync_TamperedPayload_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        // A well-formed proof for this request, wearing another proof's signature.
        var target = key.Proof("GET", Url).Split('.');
        var donor = key.Proof("GET", Url).Split('.');
        var tampered = $"{target[0]}.{target[1]}.{donor[2]}";

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(tampered, "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_ProofLeakingItsPrivateKey_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(key.Proof("GET", Url, includePrivateKey: true), "GET", Url));

        Assert.Contains("private key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_AlgorithmNotMatchingTheCurve_IsRejected()
    {
        // The alg header is attacker-controlled; pinning it to the key's curve is what stops a
        // signature being validated under an algorithm the key was never meant for.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(key.Proof("GET", Url, algorithm: "ES256K"), "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_TruncatedSignature_IsRejectedRatherThanFaulting()
    {
        // A wrong-length r||s is a rejected signature, not a server fault.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var parts = key.Proof("GET", Url).Split('.');
        var truncated = $"{parts[0]}.{parts[1]}.{parts[2][..20]}";

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(truncated, "GET", Url));

        Assert.Equal("NotAuthorized", ex.Error);
    }

    [Theory]
    [InlineData(JwsSegments.Header)]
    [InlineData(JwsSegments.Payload)]
    [InlineData(JwsSegments.Signature)]
    [InlineData(JwsSegments.All)]
    public async Task ValidateAsync_SegmentsWithBase64Padding_AreAccepted(JwsSegments padded)
    {
        // RFC 7515 omits the padding, but a correctly padded segment decodes to the same bytes,
        // and the signature covers the header and payload exactly as they were sent.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var proof = await validator.ValidateAsync(
            key.Proof("GET", Url, accessToken: "a-credential", padded: padded),
            "GET",
            Url,
            boundThumbprint: key.Thumbprint,
            accessToken: "a-credential");

        Assert.Equal(key.Thumbprint, proof.KeyThumbprint);
    }

    [Theory]
    [InlineData(0, "!!!!")]
    [InlineData(1, "!!!!")]
    [InlineData(2, "!!!!")]
    [InlineData(0, "AAAAA")]
    [InlineData(1, "AAAAA")]
    [InlineData(2, "AAAAA")]
    public async Task ValidateAsync_SegmentThatIsNotBase64Url_IsRejected(int index, string segment)
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var malformed = TestJws.WithSegment(key.Proof("GET", Url), index, segment);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(malformed, "GET", Url));

        Assert.Equal("NotAuthorized", ex.Error);
    }

    [Fact]
    public async Task ValidateAsync_JwkWithPaddedCoordinates_IsAccepted()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var minted = key.Proof("GET", Url, editJwk: jwk =>
        {
            jwk["x"] += "=";
            jwk["y"] += "=";
        });

        await validator.ValidateAsync(minted, "GET", Url);
    }

    [Theory]
    [InlineData("x", "!!!!")]
    [InlineData("y", "!!!!")]
    [InlineData("x", "AAAAA")]
    [InlineData("y", "AAAAA")]
    public async Task ValidateAsync_JwkCoordinateThatIsNotBase64Url_IsRejected(string member, string value)
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var minted = key.Proof("GET", Url, editJwk: jwk => jwk[member] = value);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(minted, "GET", Url));

        Assert.Equal("NotAuthorized", ex.Error);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("not-a-jwt")]
    public async Task ValidateAsync_NotThreeSegments_IsRejected(string proof)
    {
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(proof, "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_HeaderThatIsNotJson_IsRejected()
    {
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var malformed = TestJws.WithSegment(
            key.Proof("GET", Url), 0, TestJws.Encode("not json"u8));

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(malformed, "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_NoProof_IsRejected()
    {
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(string.Empty, "GET", Url));
    }

    [Fact]
    public async Task ValidateAsync_RelativeHtu_IsRejectedRatherThanComparedVerbatim()
    {
        // A htu that does not normalize has not been canonicalized on scheme, host, or port, so
        // it is refused outright rather than compared as the raw string it arrived as.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(key.Proof("GET", "/xrpc/com.atproto.space.getRecord"), "GET", Url));

        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RequestUriThatIsNotAbsolute_FaultsRatherThanMatchingAnything()
    {
        // The trusted side of the comparison. A misconfigured PublicBaseUrl is this service's
        // bug, not a caller's, and must not silently turn into a comparison of two raw strings.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        await Assert.ThrowsAsync<ArgumentException>(
            () => validator.ValidateAsync(
                key.Proof("GET", "pds.example.com/xrpc/a"), "GET", "pds.example.com/xrpc/a"));
    }

    [Theory]
    [InlineData("https://user:pw@pds.example.com/xrpc/com.atproto.space.getRecord", Url)]
    [InlineData(Url, "https://user:pw@pds.example.com/xrpc/com.atproto.space.getRecord")]
    public async Task ValidateAsync_UserinfoOnEitherSide_IsIgnored(string proofUrl, string requestUrl)
    {
        // Userinfo never belongs in an htu (RFC 9110 section 4.2.4), so it plays no part in the
        // comparison on either side.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        await validator.ValidateAsync(key.Proof("GET", proofUrl), "GET", requestUrl);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("/")]
    public async Task ValidateAsync_SignatureInTheStandardBase64Alphabet_IsRejected(string character)
    {
        // Base64url has one alphabet. A segment spelled with the standard alphabet's '+' or '/'
        // is not a JWS segment, whatever it would decode to.
        using var key = new TestDPoPKey();
        var validator = CreateValidator();

        var proof = key.Proof("GET", Url);
        var signature = proof.Split('.')[2];
        var respelled = TestJws.WithSegment(proof, 2, character + signature[1..]);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => validator.ValidateAsync(respelled, "GET", Url));

        Assert.Contains("not base64url", ex.Message, StringComparison.Ordinal);
    }
}
