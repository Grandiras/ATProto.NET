using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Auth;

/// <summary>Tests for the shared compact-JWS encoding in <see cref="Jwt"/>.</summary>
public class JwtTests
{
    // ── base64url ────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("YQ", "61")]
    [InlineData("YQ==", "61")]
    [InlineData("YWI", "6162")]
    [InlineData("YWI=", "6162")]
    [InlineData("YWJj", "616263")]
    [InlineData("-_8", "FBFF")]
    [InlineData("____", "FFFFFF")]
    public void TryDecodeBase64Url_UnpaddedOrCorrectlyPadded_Decodes(string text, string hex)
    {
        Assert.True(Jwt.TryDecodeBase64Url(text, out var bytes));
        Assert.Equal(hex, Convert.ToHexString(bytes));
    }

    [Theory]
    [InlineData("+/8")] // the standard alphabet
    [InlineData("////")]
    [InlineData("Y Q")] // whitespace, which the BCL decoder alone would skip
    [InlineData(" YQ")]
    [InlineData("YQ\n")]
    [InlineData("YWJjZ")] // a length no encoding produces
    [InlineData("=")]
    [InlineData("YQ===")]
    [InlineData("Y=Q")]
    [InlineData("YR")] // non-zero trailing bits: a second spelling of "YQ"
    [InlineData("!!!!")]
    [InlineData("YQ%3D%3D")]
    public void TryDecodeBase64Url_AnythingElse_IsRejected(string text)
    {
        Assert.False(Jwt.TryDecodeBase64Url(text, out var bytes));
        Assert.Null(bytes);
    }

    // ── Decoding ─────────────────────────────────────────────────

    [Fact]
    public void TryDecode_WellFormedToken_ExposesHeaderPayloadAndSigningInput()
    {
        var jwt = TestJws.Mint(
            new Dictionary<string, object> { ["alg"] = "ES256" },
            new Dictionary<string, object> { ["iss"] = "did:plc:a", ["exp"] = 1 },
            _ => [1, 2, 3]);

        Assert.True(Jwt.TryDecode(jwt, out var token, out var error));
        Assert.Null(error);
        Assert.Equal("ES256", token.Header.GetStringOrNull("alg"));
        Assert.Equal("did:plc:a", token.Payload.GetStringOrNull("iss"));
        Assert.Null(token.Payload.GetStringOrNull("exp"));
        Assert.Equal(Encoding.ASCII.GetBytes(jwt[..jwt.LastIndexOf('.')]), token.SigningInput);
        Assert.Equal(new byte[] { 1, 2, 3 }, token.Signature);
    }

    [Theory]
    [InlineData("", "expected three parts")]
    [InlineData("a.b", "expected three parts")]
    [InlineData("a.b.c.d", "expected three parts")]
    [InlineData("!.e30.", "the header is not base64url")]
    [InlineData("e30.!.", "the payload is not base64url")]
    [InlineData("e30.e30.!", "the signature is not base64url")]
    [InlineData("bm90IGpzb24.e30.", "the header is not JSON")]
    [InlineData("e30.WzFd.", "the payload is not a JSON object")]
    public void TryDecode_MalformedToken_SaysWhy(string jwt, string reason)
    {
        Assert.False(Jwt.TryDecode(jwt, out _, out var error));
        Assert.StartsWith(reason, error, StringComparison.Ordinal);
    }

    // ── Signing ──────────────────────────────────────────────────

    [Fact]
    public void Sign_P256_ProducesAnEs256TokenTheKeyVerifies()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var header = Jwt.EncodeHeader("JWT", key.Curve, keyId: "#atproto");

        var jwt = Jwt.Sign(header, """{"iss":"did:plc:a"}"""u8, key);

        Assert.True(Jwt.TryDecode(jwt, out var token, out _));
        Assert.Equal(
            """{"typ":"JWT","alg":"ES256","kid":"#atproto"}""",
            JsonSerializer.Serialize(token.Header));
        Assert.Equal("did:plc:a", token.Payload.GetStringOrNull("iss"));
        Assert.True(key.Verify(token.SigningInput, token.Signature));
    }

    [Fact]
    public void EncodeHeader_K256Key_IsLabelledEs256K()
    {
        var header = Jwt.EncodeHeader("JWT", KeyCurve.K256);

        var json = JsonSerializer.Deserialize<JsonElement>(TestJws.Decode(Encoding.ASCII.GetString(header)));
        Assert.Equal("ES256K", json.GetStringOrNull("alg"));
    }

    [Fact]
    public void EncodeHeader_WithAJwk_EmbedsItsSetMembersOnly()
    {
        var jwk = new JsonWebKey { Kty = "EC", Crv = "P-256", X = "x-value", Y = "y-value" };

        var header = Jwt.EncodeHeader("dpop+jwt", KeyCurve.P256, jwk: jwk);

        // Compared as JSON: the writer escapes '+' in "dpop+jwt", which any JSON reader undoes.
        var decoded = JsonSerializer.Deserialize<JsonElement>(TestJws.Decode(Encoding.ASCII.GetString(header)));
        Assert.Equal(
            """{"typ":"dpop+jwt","alg":"ES256","jwk":{"kty":"EC","crv":"P-256","x":"x-value","y":"y-value"}}""",
            JsonSerializer.Serialize(decoded, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
    }

    [Fact]
    public void WriteTokenId_WritesAFresh128BitHexJti()
    {
        static string Mint()
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                Jwt.WriteTokenId(writer);
                writer.WriteEndObject();
            }

            return JsonSerializer.Deserialize<JsonElement>(buffer.WrittenSpan).GetProperty("jti").GetString()!;
        }

        var first = Mint();

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, Mint());
    }
}
