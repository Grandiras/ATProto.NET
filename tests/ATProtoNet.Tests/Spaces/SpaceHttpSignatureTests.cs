using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The HTTP message signatures of the Spaces alpha (<c>atproto-space</c>, RFC 9421).
/// </summary>
/// <remarks>
/// <para>The vectors are not this SDK's own output. They were produced by running
/// <c>packages/space/src/http-signature.ts</c> from bluesky-social/atproto at <c>679724ad</c>
/// (the reference implementation of the 2026-10-01 alpha) — <c>createSpaceSigHeaders</c> over a
/// fixed P-256 key, with structured-headers 1.0.1 and the signing of @atproto/crypto, whose
/// ECDSA is deterministic — and the reference verifier accepted every one before it was pinned here.
/// So these tests fail if the SDK's signature base, component names, labels, parameter
/// canonicalization or encodings drift from the reference.</para>
/// <para>The negative tests are the reference's own cases (<c>http-signature.test.ts</c>), plus
/// the checks this SDK makes beyond it, which are marked.</para>
/// </remarks>
public class SpaceHttpSignatureTests
{
    // The fixed key the vectors were signed with: SHA-256("atproto.net space vector key 1").
    private const string KeyId = "did:key:zDnaehpewypegZXBqk8rh1EGpfU5c2vmdP6bXvg7sE2B2XiCW";

    private const string K256KeyId = "did:key:zQ3shUAFRuXMv1vipFqLoFXTZhmEwwva1mQ4R4iXqF3882H8U";

    private const string Credential =
        "eyJhbGciOiJFUzI1NiIsInR5cCI6ImF0cHJvdG8tc3BhY2UtY3JlZGVudGlhbCtqd3QifQ.eyJzdWIiOiJhdDovL2RpZDpwbGM6YXV0aG9yaXR5L3NwYWNlL2NvbS5leGFtcGxlLmZvcnVtL2RlZmF1bHQifQ.c2ln";

    private const string Delegation =
        "eyJhbGciOiJFUzI1NiIsInR5cCI6ImF0cHJvdG8tc3BhY2UtZGVsZWdhdGlvbitqd3QifQ.eyJhdWQiOiJkaWQ6cGxjOmF1dGhvcml0eSNhdHByb3RvX3NwYWNlX2hvc3QifQ.c2ln";

    private static readonly Did Audience = Did.Parse("did:plc:repoowner");

    private static readonly string UseAuthorization = $"Atproto-Space {Credential}";
    private static readonly string ExchangeAuthorization = $"Bearer {Delegation}";

    // ── Reference vectors ────────────────────────────────────────

    private const string UseInput = "atproto-space=(\"authorization\" \"atproto-space-audience\")";

    private const string UseSignature =
        "atproto-space=:GVrrfzolwFMAI0SLxkSfOq71Ep4SkUNaLTAZ9SRp1pML+wBKwoVmjxRkNYF9idRAr51SihpapFxUHlgmRFWzkw==:";

    // The same signature with s replaced by n - s.
    private const string UseSignatureHighS =
        "atproto-space=:GVrrfzolwFMAI0SLxkSfOq71Ep4SkUNaLTAZ9SRp1pP0BP+0PXqZceubyn6Cdiu/DUmoI4y8+iifm3KcuA1xvg==:";

    private const string ExchangeInput = $"atproto-space=(\"authorization\");keyid=\"{KeyId}\"";

    private const string ExchangeSignature =
        "atproto-space=:CLjYMdudKnilQaMTbySLMwviYwKs2S93frm4q29MIEckBviUGogVBZ4SogYQBBbHl9ycIkR/WxaiEyn34TW9Xg==:";

    [Fact]
    public void VerifyRequest_AReferenceSignature_Verifies()
    {
        var verified = SpaceHttpSignature.VerifyRequest(UseAuthorization, Audience, UseInput, UseSignature, KeyId);

        Assert.Equal(KeyId, verified);
    }

    [Fact]
    public void VerifyExchange_AReferenceSignature_ReturnsTheKeyItNames()
    {
        var verified = SpaceHttpSignature.VerifyExchange(ExchangeAuthorization, ExchangeInput, ExchangeSignature);

        Assert.Equal(KeyId, verified);
    }

    [Fact]
    public void VerifyRequest_HighSSignature_IsAccepted()
    {
        // The rule against high-S is for content-addressed data; a request signature is not one.
        SpaceHttpSignature.VerifyRequest(UseAuthorization, Audience, UseInput, UseSignatureHighS, KeyId);
    }

    [Fact]
    public void VerifyRequest_OptionalParametersAndOtherLabels_AreAccepted()
    {
        // An optional alg and keyid, signed over the canonical input, next to a label that is not ours.
        SpaceHttpSignature.VerifyRequest(
            UseAuthorization,
            Audience,
            $"other=(\"authorization\");keyid=\"other\", atproto-space=(\"authorization\" \"atproto-space-audience\");alg=\"ecdsa-p256-sha256\";keyid=\"{KeyId}\"",
            "other=:YWJj:, atproto-space=:RP1GzeKEeoo6nbEFV79relUTq9+0znynm/MsejW8SM1Iro48zrMxYHlEwBDv6D226zuPQHb41nWGnJLqoOpzGw==:",
            KeyId);
    }

    [Fact]
    public void VerifyRequest_InputSpelledLooselyButSignedCanonically_IsAccepted()
    {
        // The reference verifies over the canonical re-serialization of the parsed input, so extra
        // spaces and a swapped parameter order do not matter to the signer who signed the canonical form.
        SpaceHttpSignature.VerifyRequest(
            UseAuthorization,
            Audience,
            $"atproto-space=(  \"authorization\"   \"atproto-space-audience\"  );  keyid=\"{KeyId}\";alg=\"ecdsa-p256-sha256\"",
            "atproto-space=:fFS8K19Z5q5npJDUMlONqvHVCEBwUGj/MfQCe9Ufvl4/LxNFPvoQZDMH6CV958IJKCBMgNyxmU5bewsabBSKCw==:",
            KeyId);
    }

    [Fact]
    public void VerifyExchange_EveryParameterType_IsSignedInItsCanonicalForm()
    {
        // created (integer), nonce (string with escapes), tag (token), flag (boolean), b (bytes) and
        // d (decimal, 1.50 canonically 1.5), as the reference's structured-headers serializes them.
        var input = $"atproto-space=(\"authorization\");keyid=\"{KeyId}\";created=1618884473;nonce=\"a\\\"b\\\\c\";tag=tok;flag;b=:YWJj:;d=1.50";

        var verified = SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization,
            input,
            "atproto-space=:js2fWnFHG5zngTwyuK97JQvBz1Kla9qZwNjvY4FylZEdlKc8gMeveIrK6cpFRlu6sfabAf0l5HjswO9/m9zGmg==:");

        Assert.Equal(KeyId, verified);
    }

    // ── What this SDK signs ──────────────────────────────────────

    [Fact]
    public void SignRequest_SignsTheReferenceSignatureBase()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var signature = SpaceHttpSignature.SignRequest(key, UseAuthorization, Audience);

        Assert.Equal(UseInput, signature.SignatureInput);

        // Verified against a base written out by hand from the reference: RFC 9421 lines joined by
        // LF, no trailing LF.
        var expectedBase =
            $"\"authorization\": {UseAuthorization}\n\"atproto-space-audience\": did:plc:repoowner\n\"@signature-params\": (\"authorization\" \"atproto-space-audience\")";
        Assert.True(AtProtoCrypto.VerifySignature(key.ToDidKey(), Encoding.UTF8.GetBytes(expectedBase), Decode(signature.Signature)));
    }

    [Fact]
    public void SignExchange_SignsTheReferenceSignatureBase()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var signature = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        Assert.Equal($"atproto-space=(\"authorization\");keyid=\"{key.ToDidKey()}\"", signature.SignatureInput);

        var expectedBase =
            $"\"authorization\": {ExchangeAuthorization}\n\"@signature-params\": (\"authorization\");keyid=\"{key.ToDidKey()}\"";
        Assert.True(AtProtoCrypto.VerifySignature(key.ToDidKey(), Encoding.UTF8.GetBytes(expectedBase), Decode(signature.Signature)));
    }

    [Fact]
    public void Sign_ProducesA64ByteBase64SignatureUnderTheLabel()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var signature = SpaceHttpSignature.SignRequest(key, UseAuthorization, Audience).Signature;

        Assert.StartsWith("atproto-space=:", signature, StringComparison.Ordinal);
        Assert.EndsWith(":", signature, StringComparison.Ordinal);
        Assert.Equal(64, Decode(signature).Length);
    }

    [Fact]
    public void SignAndVerify_RoundTrip()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var request = SpaceHttpSignature.SignRequest(key, UseAuthorization, Audience);
        var exchange = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        SpaceHttpSignature.VerifyRequest(UseAuthorization, Audience, request.SignatureInput, request.Signature, key.ToDidKey());
        Assert.Equal(key.ToDidKey(), SpaceHttpSignature.VerifyExchange(ExchangeAuthorization, exchange.SignatureInput, exchange.Signature));
    }

    [Fact]
    public void Sign_WithAKeyThatIsNotP256_Throws()
    {
        using var key = AtProtoCrypto.GenerateK256Key();

        Assert.Throws<ArgumentException>(() => SpaceHttpSignature.SignExchange(key, ExchangeAuthorization));
        Assert.Throws<ArgumentException>(() => SpaceHttpSignature.SignRequest(key, UseAuthorization, Audience));
    }

    // ── Verification refuses what the reference refuses ──────────

    private static SpaceSignatureHeaders Signed(AtProtoKey key) =>
        SpaceHttpSignature.SignRequest(key, UseAuthorization, Audience);

    private static void VerifyUse(
        string authorization, string audience, SpaceSignatureHeaders signature, string keyId) =>
        SpaceHttpSignature.VerifyRequest(authorization, Did.Parse(audience), signature.SignatureInput, signature.Signature, keyId);

    [Fact]
    public void VerifyRequest_ChangedAuthorization_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(
            () => VerifyUse(UseAuthorization + "-changed", Audience.Value, Signed(key), key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_ChangedAudience_IsRejected()
    {
        // A signature made for one account's repo does not read another's.
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(
            () => VerifyUse(UseAuthorization, "did:plc:someoneelse", Signed(key), key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_AnotherSigningKey_IsRejected()
    {
        using var signer = AtProtoCrypto.GenerateP256Key();
        using var holder = AtProtoCrypto.GenerateP256Key();

        var ex = Assert.Throws<SpaceSignatureException>(
            () => VerifyUse(UseAuthorization, Audience.Value, Signed(signer), holder.ToDidKey()));

        Assert.Contains("Invalid HTTP message signature", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRequest_KeyidDifferingFromTheCredentialKey_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        var ex = Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput + $";keyid=\"{other.ToDidKey()}\"", signed.Signature, key.ToDidKey()));

        Assert.Contains("keyid does not match the credential key", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(";keyid=1")]
    [InlineData(";keyid")]
    [InlineData(";keyid=tok")]
    public void VerifyRequest_KeyidThatIsNotAString_IsRejected(string parameter)
    {
        // The reference compares the parameter with the credential key as a value, so a boolean,
        // integer or token never equals the did:key string.
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput + parameter, signed.Signature, key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_AChangedSignatureParameter_IsRejected()
    {
        // A parameter added after signing is part of the base, so the signature no longer covers it.
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput + ";alg=\"ecdsa-p256-sha256\"", signed.Signature, key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_AudienceNotCoveredBySignature_IsRejected()
    {
        // Signed over the authorization alone, the audience header is attacker-controlled.
        using var key = AtProtoCrypto.GenerateP256Key();
        var authorizationOnly = SpaceHttpSignature.SignExchange(key, UseAuthorization);

        var ex = Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, authorizationOnly.SignatureInput, authorizationOnly.Signature, key.ToDidKey()));

        Assert.Contains("must cover exactly", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyExchange_ACredentialSignature_IsRejected()
    {
        // The exchange covers the authorization alone and names its key; a credential-use
        // signature is neither.
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        var ex = Assert.Throws<SpaceSignatureException>(
            () => SpaceHttpSignature.VerifyExchange(UseAuthorization, signed.SignatureInput, signed.Signature));

        Assert.Contains("must cover exactly", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(\"atproto-space-audience\")")]
    [InlineData("(\"atproto-space-audience\" \"authorization\")")]
    [InlineData("(\"authorization\" \"atproto-space-audience\" \"content-type\")")]
    [InlineData("(\"authorization\" \"authorization\" \"atproto-space-audience\")")]
    [InlineData("(\"authorization\";sf \"atproto-space-audience\")")]
    [InlineData("(authorization \"atproto-space-audience\")")]
    [InlineData("(\"Authorization\" \"atproto-space-audience\")")]
    [InlineData("(\"@method\" \"atproto-space-audience\")")]
    [InlineData("()")]
    public void VerifyRequest_WrongComponents_AreRejected(string components)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, $"atproto-space={components}", Signed(key).Signature, key.ToDidKey()));
    }

    [Theory]
    [InlineData("not-a-list")]
    [InlineData("")]
    [InlineData("atproto-space")]
    [InlineData("atproto-space=1")]
    [InlineData("atproto-space=\"authorization\"")]
    [InlineData("other=(\"authorization\" \"atproto-space-audience\")")]
    public void VerifyRequest_MalformedOrMissingInput_IsRejected(string input)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, input, Signed(key).Signature, key.ToDidKey()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-byte-sequence")]
    [InlineData("atproto-space=not-a-byte-sequence")]
    [InlineData("atproto-space=:YWJj:")]               // 3 bytes
    [InlineData("atproto-space=:!!!:")]                // not base64
    [InlineData("atproto-space=1")]
    [InlineData("other=:YWJj:")]                       // our label is absent
    [InlineData("atproto-space=:YWJj:;x=1")]           // a signature carries no parameters
    [InlineData("atproto-space=(:YWJj:)")]             // an inner list, not a byte sequence
    public void VerifyRequest_InvalidSignatureField_IsRejected(string signature)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, Signed(key).SignatureInput, signature, key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_DerEncodedSignature_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);
        var compact = Decode(signed.Signature);

        // The same signature, DER-encoded: it would verify under another parser.
        var der = Der(compact);
        Assert.NotEqual(64, der.Length);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput, $"atproto-space=:{Convert.ToBase64String(der)}:", key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_SignatureWithScalarsOutOfRange_IsRejectedNotFaulted()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var zeros = Convert.ToBase64String(new byte[64]);
        var ones = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 64).ToArray());

        foreach (var sig in new[] { zeros, ones })
            Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
                UseAuthorization, Audience, Signed(key).SignatureInput, $"atproto-space=:{sig}:", key.ToDidKey()));
    }

    [Theory]
    [InlineData("ecdsa-p384-sha384")]
    [InlineData("ed25519")]
    [InlineData("rsa-pss-sha512")]
    public void VerifyRequest_OtherAlgorithms_AreRejected(string algorithm)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        var ex = Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput + $";alg=\"{algorithm}\"", signed.Signature, key.ToDidKey()));

        Assert.Contains("algorithm", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyExchange_OtherAlgorithm_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization, signed.SignatureInput + ";alg=\"ecdsa-p384-sha384\"", signed.Signature));
    }

    [Fact]
    public void VerifyRequest_AlgSuppliedAsAToken_IsRejected()
    {
        // The reference compares the value with the string, and a token is not one.
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput + ";alg=ecdsa-p256-sha256", signed.Signature, key.ToDidKey()));
    }

    [Fact]
    public void Verify_AKeyThatIsNotP256_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);
        var exchange = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        var ex = Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput, signed.Signature, K256KeyId));
        Assert.Contains("P-256", ex.Message, StringComparison.Ordinal);

        Assert.Contains("P-256", Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization,
            exchange.SignatureInput.Replace(key.ToDidKey(), K256KeyId, StringComparison.Ordinal),
            exchange.Signature)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(";keyid")]
    [InlineData(";keyid=123")]
    [InlineData(";keyid=\"not-a-key\"")]
    [InlineData(";keyid=\"did:key:z\"")]
    [InlineData(";keyid=\"did:web:example.com\"")]
    public void VerifyExchange_MissingOrInvalidKeyid_IsRejected(string parameters)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization, $"atproto-space=(\"authorization\"){parameters}", signed.Signature));
    }

    [Fact]
    public void VerifyExchange_SignedByAKeyOtherThanTheKeyid_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();
        var signed = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization,
            signed.SignatureInput.Replace(key.ToDidKey(), other.ToDidKey(), StringComparison.Ordinal),
            signed.Signature));
    }

    [Fact]
    public void VerifyExchange_ChangedDelegationToken_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = SpaceHttpSignature.SignExchange(key, ExchangeAuthorization);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            ExchangeAuthorization + "x", signed.SignatureInput, signed.Signature));
    }

    [Fact]
    public void Verify_EmptyAuthorization_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            string.Empty, Audience, signed.SignatureInput, signed.Signature, key.ToDidKey()));
        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyExchange(
            string.Empty, signed.SignatureInput, signed.Signature));
    }

    // ── Beyond the reference ─────────────────────────────────────

    [Fact]
    public void VerifyRequest_LabelRepeatedInSignatureInput_IsRejected()
    {
        // RFC 8941 lets the last of a repeated key win; across several field lines that is
        // ambiguous, so the SDK refuses it where the reference takes the last.
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, $"{signed.SignatureInput}, {signed.SignatureInput}", signed.Signature, key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_LabelRepeatedInSignature_IsRejected()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        Assert.Throws<SpaceSignatureException>(() => SpaceHttpSignature.VerifyRequest(
            UseAuthorization, Audience, signed.SignatureInput, $"{signed.Signature}, {signed.Signature}", key.ToDidKey()));
    }

    [Fact]
    public void VerifyRequest_SignatureIsReusableForTheSameCredentialAndAudience()
    {
        // Nothing binds a signature to a method, URL, nonce or moment: replaying it is how a syncer
        // may cache one per (credential, audience).
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = Signed(key);

        for (var i = 0; i < 3; i++)
            VerifyUse(UseAuthorization, Audience.Value, signed, key.ToDidKey());
    }

    [Fact]
    public void VerifyRequest_SignatureDoesNotTransferToAnotherCredential()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceSignatureException>(
            () => VerifyUse("Atproto-Space another.credential.jwt", Audience.Value, Signed(key), key.ToDidKey()));
    }

    private static byte[] Decode(string signatureHeader) =>
        Convert.FromBase64String(signatureHeader["atproto-space=:".Length..^1]);

    // The DER SEQUENCE of two INTEGERs a verifier that takes DER would read this r || s as.
    private static byte[] Der(byte[] compact)
    {
        static byte[] Integer(ReadOnlySpan<byte> value)
        {
            var trimmed = value.TrimStart((byte)0);
            var body = trimmed.Length > 0 && (trimmed[0] & 0x80) != 0 ? [0, .. trimmed] : trimmed.ToArray();
            return [0x02, (byte)body.Length, .. body];
        }

        var content = Integer(compact.AsSpan(0, 32)).Concat(Integer(compact.AsSpan(32))).ToArray();
        return [0x30, (byte)content.Length, .. content];
    }
}
