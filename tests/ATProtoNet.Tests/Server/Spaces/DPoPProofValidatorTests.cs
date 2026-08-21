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
    [InlineData("/xrpc/a")]
    [InlineData("pds.example.com/xrpc/a")]
    [InlineData("not a url at all")]
    public void NormalizeUri_NonAbsoluteUrl_IsNull(string url) =>
        Assert.Null(DPoPProofValidator.NormalizeUri(url));

    [Theory]
    [InlineData("https://pds.example.com/xrpc/a", "https://PDS.EXAMPLE.COM/xrpc/a")]
    [InlineData("https://pds.example.com:443/xrpc/a", "https://pds.example.com/xrpc/a")]
    public void NormalizeUri_EquivalentForms_Agree(string left, string right) =>
        Assert.Equal(DPoPProofValidator.NormalizeUri(left), DPoPProofValidator.NormalizeUri(right));

    [Fact]
    public void NormalizeUri_StripsQueryAndFragment() =>
        Assert.Equal(
            "https://pds.example.com/xrpc/a",
            DPoPProofValidator.NormalizeUri("https://pds.example.com/xrpc/a?b=c#d"));
}
