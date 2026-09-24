using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The DPoP computations the proof generator and the server-side validator share, pinned to the
/// RFC 9449 examples where the RFC gives one.
/// </summary>
public class DPoPTests
{
    // RFC 9449 section 4.1 (Figure 2) embeds this key; section 6.1 gives its thumbprint.
    private static JsonWebKey RfcKey() => new()
    {
        Kty = "EC",
        Crv = "P-256",
        X = "l8tFrhx-34tV3hRICRDY9zCkDlpBhF42UQUfWVAWBFs",
        Y = "9VE4jf_Ok_o64zbTTlcuNJajHmt6v9TDVrU0CdvGRDA",
    };

    [Fact]
    public void Thumbprint_Rfc9449Key_MatchesTheRfc()
    {
        Assert.Equal("0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I", DPoP.Thumbprint(RfcKey()));
    }

    [Fact]
    public void Thumbprint_IgnoresMembersRfc7638DoesNotRequire()
    {
        var decorated = RfcKey();
        decorated.Kid = "key-1";
        decorated.Use = "sig";
        decorated.Alg = "ES256";

        Assert.Equal(DPoP.Thumbprint(RfcKey()), DPoP.Thumbprint(decorated));
    }

    [Fact]
    public void Thumbprint_KeyThatIsNotACompleteEcKey_Throws()
    {
        var rsa = RfcKey();
        rsa.Kty = "RSA";
        var noY = RfcKey();
        noY.Y = null;

        Assert.Throws<ArgumentException>(() => DPoP.Thumbprint(rsa));
        Assert.Throws<ArgumentException>(() => DPoP.Thumbprint(noY));
    }

    [Fact]
    public void AccessTokenHash_Rfc9449Token_MatchesTheRfc()
    {
        // RFC 9449 section 7.1.
        Assert.Equal(
            "fUHyO2r2Z3DZ53EsNrWBb0xWXoaNy59IiKCAqksmQEo",
            DPoP.AccessTokenHash("Kz~8mXK1EalYznwH-LC-1fBAo.4Ljp~zsPE_NeO.gxU"));
    }

    [Theory]
    [InlineData("https://pds.example.com/xrpc/a", "https://pds.example.com/xrpc/a")]
    [InlineData("https://pds.example.com/xrpc/a?b=c#d", "https://pds.example.com/xrpc/a")]
    [InlineData("https://user:pw@pds.example.com/xrpc/a", "https://pds.example.com/xrpc/a")]
    [InlineData("https://user@pds.example.com:8443/xrpc/a", "https://pds.example.com:8443/xrpc/a")]
    [InlineData("HTTPS://PDS.EXAMPLE.COM/xrpc/a", "https://pds.example.com/xrpc/a")]
    [InlineData("https://pds.example.com:443/xrpc/a", "https://pds.example.com/xrpc/a")]
    [InlineData("http://pds.example.com:80/xrpc/a", "http://pds.example.com/xrpc/a")]
    [InlineData("http://[::1]:2583/xrpc/a", "http://[::1]:2583/xrpc/a")]
    [InlineData("https://pds.example.com", "https://pds.example.com/")]
    public void NormalizeHtu_AbsoluteUrl_IsSchemeHostPortAndPath(string url, string expected)
    {
        Assert.Equal(expected, DPoP.NormalizeHtu(url));
    }

    [Theory]
    [InlineData("/xrpc/a")]
    [InlineData("pds.example.com/xrpc/a")]
    [InlineData("not a url at all")]
    [InlineData("file:///xrpc/a")]
    public void NormalizeHtu_UrlThatIsNotAbsoluteWithAHost_IsNull(string url)
    {
        Assert.Null(DPoP.NormalizeHtu(url));
    }
}
