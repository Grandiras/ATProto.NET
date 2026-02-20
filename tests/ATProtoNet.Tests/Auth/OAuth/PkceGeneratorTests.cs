using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

public class PkceGeneratorTests
{
    [Fact]
    public void GenerateCodeVerifier_ReturnsValidLength()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        Assert.NotNull(verifier);
        // 32 bytes → 43 base64url characters
        Assert.Equal(43, verifier.Length);
    }

    [Fact]
    public void GenerateCodeVerifier_ReturnsBase64UrlSafe()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        Assert.DoesNotContain("+", verifier);
        Assert.DoesNotContain("/", verifier);
        Assert.DoesNotContain("=", verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_IsRandom()
    {
        var verifier1 = PkceGenerator.GenerateCodeVerifier();
        var verifier2 = PkceGenerator.GenerateCodeVerifier();

        Assert.NotEqual(verifier1, verifier2);
    }

    [Fact]
    public void ComputeCodeChallenge_ReturnsBase64UrlSafe()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();
        var challenge = PkceGenerator.ComputeCodeChallenge(verifier);

        Assert.NotNull(challenge);
        Assert.NotEmpty(challenge);
        Assert.DoesNotContain("+", challenge);
        Assert.DoesNotContain("/", challenge);
        Assert.DoesNotContain("=", challenge);
    }

    [Fact]
    public void ComputeCodeChallenge_IsConsistent()
    {
        var verifier = "test-verifier-value";
        var challenge1 = PkceGenerator.ComputeCodeChallenge(verifier);
        var challenge2 = PkceGenerator.ComputeCodeChallenge(verifier);

        Assert.Equal(challenge1, challenge2);
    }

    [Fact]
    public void ComputeCodeChallenge_DifferentVerifiersDifferentChallenges()
    {
        var verifier1 = PkceGenerator.GenerateCodeVerifier();
        var verifier2 = PkceGenerator.GenerateCodeVerifier();

        var challenge1 = PkceGenerator.ComputeCodeChallenge(verifier1);
        var challenge2 = PkceGenerator.ComputeCodeChallenge(verifier2);

        Assert.NotEqual(challenge1, challenge2);
    }

    [Fact]
    public void ComputeCodeChallenge_ProducesCorrectLength()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();
        var challenge = PkceGenerator.ComputeCodeChallenge(verifier);

        // SHA-256 produces 32 bytes → 43 base64url characters
        Assert.Equal(43, challenge.Length);
    }

    [Fact]
    public void GenerateState_ReturnsRandomValue()
    {
        var state1 = PkceGenerator.GenerateState();
        var state2 = PkceGenerator.GenerateState();

        Assert.NotNull(state1);
        Assert.NotEmpty(state1);
        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void GenerateState_ReturnsBase64UrlSafe()
    {
        var state = PkceGenerator.GenerateState();

        Assert.DoesNotContain("+", state);
        Assert.DoesNotContain("/", state);
        Assert.DoesNotContain("=", state);
    }

    [Fact]
    public void GenerateState_ReturnsCorrectLength()
    {
        var state = PkceGenerator.GenerateState();
        // 32 bytes → 43 base64url characters
        Assert.Equal(43, state.Length);
    }

    [Fact]
    public void KnownVector_S256()
    {
        // RFC 7636 Appendix B test vector
        // code_verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
        // code_challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = PkceGenerator.ComputeCodeChallenge(verifier);

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }
}
