using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.HttpSignatures;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Crypto.HttpSignatures;

/// <summary>
/// The generic RFC 9421 scenarios: the signature base, the covered components, parameters and time. The space
/// request rules (label, components, P-256) are conformance vectors (<c>spaces/http-signatures.json</c>).
/// </summary>
public sealed class HttpMessageSignatureTests
{
    private const string Label = "sig1";
    private static readonly string[] Components = ["authorization", "content-type"];
    private static readonly DateTimeOffset Created = DateTimeOffset.FromUnixTimeSeconds(1618884473);

    public static TheoryData<KeyCurve> Curves() => [KeyCurve.P256, KeyCurve.K256];

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-54"), Trait("Spec", "CRY-55"), Trait("Spec", "CRY-45")]
    public void Sign_ThenVerify_AcceptsWithTheParameters(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);

        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key);
        var verdict = HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), key.PublicKey);

        var accepted = Assert.IsType<HttpSignatureVerdict.Accepted>(verdict);
        Assert.Equal(Components, accepted.Parameters.Components);
        Assert.Equal((Label, key.PublicKey.DidKey.Value, Created), (accepted.Parameters.Label, accepted.Parameters.KeyId, accepted.Parameters.Created));
        Assert.Same(key.PublicKey, accepted.Key);
        Assert.NotEmpty(key.Sign("still usable"u8));
    }

    [Fact]
    [Trait("Spec", "CRY-54")]
    public void Sign_WritesTheRfc9421BaseAndDictionaries()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key);

        Assert.Equal($"{Label}={Input(key)}", fields.SignatureInput);
        var signature = Convert.FromBase64String(fields.Signature[(Label.Length + 2)..^1]);
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = Point(key) });
        Assert.True(ecdsa.VerifyData(Base(key), signature, HashAlgorithmName.SHA256));
    }

    [Theory]
    [InlineData("@method")]
    [InlineData("Authorization")]
    [InlineData("missing")]
    [Trait("Spec", "CRY-54")]
    public void Sign_ComponentNotAFieldOfTheMessage_Throws(string component)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<ArgumentException>(() => HttpMessageSigner.Sign(Request(), Parameters(key) with { Components = [component] }, key));
    }

    [Fact]
    [Trait("Spec", "CRY-55")]
    public void Verify_HighS_IsAcceptedAndDerIsRefused()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key);
        var signature = Convert.FromBase64String(fields.Signature[(Label.Length + 2)..^1]);
        using var signer = ECDsa.Create();
        signer.ImportPkcs8PrivateKey(key.ExportPkcs8(), out _);
        var der = signer.SignData(Base(key), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.IsType<HttpSignatureVerdict.Accepted>(Verify(key, fields with { Signature = Sig(SignatureTests.HighS(signature, KeyCurve.P256)) }));
        Assert.IsType<HttpSignatureVerdict.Refused>(Verify(key, fields with { Signature = Sig(der) }));
    }

    [Fact]
    [Trait("Spec", "CRY-55")]
    public void Verify_RepeatedLabel_IsRefused()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key);
        // A second field line repeats the label: RFC 8941 would take the last, which two parties may read differently.
        var request = Request(fields, ("Signature", fields.Signature));

        Assert.IsType<HttpSignatureVerdict.Refused>(HttpMessageSignatureVerifier.Verify(request, Requirements(), key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "CRY-55")]
    public void Verify_RequiredComponentNotCovered_IsRefused()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key) with { Components = ["authorization"] }, key);

        var verdict = HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), key.PublicKey);

        Assert.Contains("content-type", Assert.IsType<HttpSignatureVerdict.Refused>(verdict).Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "CRY-55")]
    public void Verify_KeyIdNamingAnotherKey_IsRefused()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        using var other = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key) with { KeyId = other.PublicKey.DidKey.Value }, key);

        Assert.IsType<HttpSignatureVerdict.Refused>(HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), key.PublicKey));
        Assert.IsType<HttpSignatureVerdict.Refused>(HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), id => id == other.PublicKey.DidKey ? other.PublicKey : null));
    }

    [Theory]
    [InlineData(KeyCurve.P256, "ecdsa-p256-sha256", true)]
    [InlineData(KeyCurve.P256, "ecdsa-p384-sha384", false)]
    [InlineData(KeyCurve.K256, "ecdsa-p256-sha256", false)]
    [Trait("Spec", "CRY-55")]
    public void Verify_AlgParameter_MustBeTheKeys(KeyCurve curve, string algorithm, bool accepted)
    {
        using var key = SigningKey.Generate(curve);
        var input = $"{Label}=(\"authorization\");alg=\"{algorithm}\"";
        var signatureBase = $"\"authorization\": Bearer token\n\"@signature-params\": (\"authorization\");alg=\"{algorithm}\"";
        var fields = new HttpSignatureFields(input, Sig(key.Sign(Encoding.UTF8.GetBytes(signatureBase))));

        var verdict = HttpMessageSignatureVerifier.Verify(Request(fields), Requirements() with { Components = ["authorization"] }, key.PublicKey);

        Assert.Equal(accepted, verdict is HttpSignatureVerdict.Accepted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sig1=(\"authorization\" \"content-type\"")]
    [InlineData("sig1=(\"@method\" \"authorization\" \"content-type\")")]
    [InlineData("sig1=(\"authorization\" \"content-type\" \"absent\")")]
    [InlineData("sig1=(\"authorization\" \"content-type\" \"content-type\")")]
    [InlineData("sig1=(\"authorization\";sf \"content-type\")")]
    [InlineData("sig1=(\"authorization\" \"content-type\");keyid=1")]
    [InlineData("sig1=(\"authorization\" \"content-type\");created=\"now\"")]
    [Trait("Spec", "CRY-55"), Trait("Spec", "CRY-56")]
    public void Verify_MalformedOrUnsupportedInput_IsRefusedNotThrown(string signatureInput)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key) with { SignatureInput = signatureInput };

        Assert.IsType<HttpSignatureVerdict.Refused>(Verify(key, fields));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(299, true)]
    [InlineData(301, false)]
    [Trait("Spec", "CRY-57")]
    public void Verify_Expires_AppliesTheCallersClockAndSkew(int secondsPastExpiry, bool accepted)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key) with { Expires = Created.AddMinutes(5) }, key);
        var time = new FakeTimeProvider(Created.AddMinutes(5).AddSeconds(secondsPastExpiry));

        var verdict = HttpMessageSignatureVerifier.Verify(Request(fields), Requirements() with { Time = time, ClockSkew = TimeSpan.FromMinutes(5) }, key.PublicKey);

        Assert.Equal(accepted, verdict is HttpSignatureVerdict.Accepted);
        Assert.Contains("expires=1618884773", fields.SignatureInput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "CRY-57")]
    public void Verify_ExpiringSignatureWithoutAClock_IsRefused()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key) with { Expires = Created.AddMinutes(5) }, key);

        Assert.IsType<HttpSignatureVerdict.Refused>(HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), key.PublicKey));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Spec", "CRY-14"), Trait("Spec", "CRY-57")]
    public void Verify_TimesAtTheEndOfTheRange_GiveAVerdictRatherThanThrowing(bool expires)
    {
        // Regression (#238 review): expires=253402300799 plus the skew overflowed DateTimeOffset and threw.
        using var key = SigningKey.Generate(KeyCurve.P256);
        var last = DateTimeOffset.FromUnixTimeSeconds(253402300799);
        var parameters = expires ? Parameters(key) with { Expires = last } : Parameters(key) with { Created = last };
        var fields = HttpMessageSigner.Sign(Request(), parameters, key);
        var requirements = Requirements() with { Time = new FakeTimeProvider(Created), ClockSkew = TimeSpan.FromMinutes(5) };

        var verdict = HttpMessageSignatureVerifier.Verify(Request(fields), requirements, key.PublicKey);

        Assert.Equal(expires, verdict is HttpSignatureVerdict.Accepted);
    }

    [Fact]
    [Trait("Spec", "CRY-57")]
    public void Verify_CreatedInTheFuture_IsRefusedBeyondTheSkew()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var fields = HttpMessageSigner.Sign(Request(), Parameters(key), key);
        var requirements = Requirements() with { Time = new FakeTimeProvider(Created.AddSeconds(-10)), ClockSkew = TimeSpan.FromSeconds(5) };

        Assert.IsType<HttpSignatureVerdict.Refused>(HttpMessageSignatureVerifier.Verify(Request(fields), requirements, key.PublicKey));
        Assert.IsType<HttpSignatureVerdict.Accepted>(HttpMessageSignatureVerifier.Verify(Request(fields), requirements with { ClockSkew = TimeSpan.FromSeconds(10) }, key.PublicKey));
    }

    private static HttpSignatureVerdict Verify(SigningKey key, HttpSignatureFields fields) =>
        HttpMessageSignatureVerifier.Verify(Request(fields), Requirements(), key.PublicKey);

    private static HttpSignatureParameters Parameters(SigningKey key) =>
        new() { Label = Label, Components = Components, KeyId = key.PublicKey.DidKey.Value, Created = Created };

    private static HttpSignatureRequirements Requirements() => new() { Label = Label, Components = Components };

    // The content type is sent on two field lines, which the base joins.
    private static HttpSignatureRequest Request(HttpSignatureFields? fields = null, params (string Name, string Value)[] more) => new(
    [
        new("Authorization", "Bearer token"), new("Content-Type", " application/json "), new("content-type", "charset=utf-8"),
        .. fields is null ? [] : new KeyValuePair<string, string>[] { new("Signature-Input", fields.SignatureInput), new("Signature", fields.Signature) },
        .. more.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)),
    ]);

    private static string Input(SigningKey key) => $"(\"authorization\" \"content-type\");created=1618884473;keyid=\"{key.PublicKey.DidKey}\"";

    private static byte[] Base(SigningKey key) =>
        Encoding.UTF8.GetBytes($"\"authorization\": Bearer token\n\"content-type\": application/json, charset=utf-8\n\"@signature-params\": {Input(key)}");

    private static string Sig(byte[] signature) => $"{Label}=:{Convert.ToBase64String(signature)}:";

    private static ECPoint Point(SigningKey key)
    {
        var jwk = key.PublicKey.ToJsonWebKey();
        return new() { X = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.X), Y = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.Y) };
    }
}
