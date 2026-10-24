using ATProtoNet.Crypto;
using System.Text;
using ATProtoNet.Crypto.HttpSignatures;
using ATProtoNet.Spaces;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    // The space request signature of spaces.md (SPC-22, SPC-24), over the public HTTP signature API: label and tag
    // atproto-space-credential, a P-256 key, and exactly ("authorization" "atproto-space-audience").
    private const string SpaceLabel = "atproto-space-credential";
    private static readonly string[] RequestComponents = ["authorization", "atproto-space-audience"];

    private static readonly Dictionary<string, string> SpacesKnownFailures = new()
    {
        ["spaces/space-tokens.json#token whose header has no typ"] =
            "Refused either way, under another name: Jws.TryDecode refuses the header without alg and the array payload (BadJwt) before typ is read; the reference reads typ first (BadJwtType).",
    };

    /// <inheritdoc />
    public byte[] Blake3(byte[] input, int outputLength)
    {
        var output = new byte[outputLength];
        global::ATProtoNet.Crypto.Blake3.Hash(input, output);
        return output;
    }

    /// <inheritdoc />
    public (byte[] State, byte[] Digest) LtHash(IReadOnlyList<(bool Add, string Element)> operations)
    {
        var hash = global::ATProtoNet.Spaces.LtHash.Empty();
        foreach (var (add, element) in operations) ApplyElement(hash, Encoding.UTF8.GetBytes(element), add);
        return (hash.GetState(), hash.Digest());
    }

    /// <inheritdoc />
    public string? VerifySpaceSignature(string? authorization, string? audience, string? signatureInput, string? signature, string keyId)
    {
        if (string.IsNullOrEmpty(authorization) || string.IsNullOrEmpty(audience) || P256(keyId) is not { } key) return null;
        var request = new HttpSignatureRequest(Fields(("Authorization", authorization), ("Atproto-Space-Audience", audience), ("Signature-Input", signatureInput), ("Signature", signature)));
        var requirements = new HttpSignatureRequirements { Label = SpaceLabel, Components = RequestComponents, Tag = SpaceLabel };
        return HttpMessageSignatureVerifier.Verify(request, requirements, key) is HttpSignatureVerdict.Accepted accepted && accepted.Parameters.Components.SequenceEqual(RequestComponents)
            ? accepted.Key.DidKey.Value
            : null;
    }

    /// <inheritdoc />
    public (string SignatureInput, string Signature)? SignSpaceRequest(string curve, string privateKeyHex, string authorization, string audience)
    {
        using var key = SigningKey.ImportRawPrivateScalar(Convert.FromHexString(privateKeyHex), curve == "P-256" ? KeyCurve.P256 : KeyCurve.K256);
        if (key.Curve != KeyCurve.P256) return null;
        var fields = HttpMessageSigner.Sign(new HttpSignatureRequest(Fields(("Authorization", authorization), ("Atproto-Space-Audience", audience))),
            new HttpSignatureParameters { Label = SpaceLabel, Components = RequestComponents, Tag = SpaceLabel }, key);
        return (fields.SignatureInput, fields.Signature);
    }

    /// <inheritdoc />
    public StructuredMember? ReadStructuredField(string field, string key)
    {
        try
        {
            var dictionary = StructuredFields.ParseDictionary(field);
            return new(dictionary.TryGetValue(key, out var member) ? StructuredFields.Serialize(member) : null, dictionary.IsDuplicated(key));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public TokenVerdict VerifySpaceToken(string type, string jwt, string signingKey, string? audience, string? subject, DateTimeOffset now)
    {
        var expected = type switch { "delegation" => SpaceTokenType.Delegation, "credential" => SpaceTokenType.Credential, _ => SpaceTokenType.ClientAttestation };
        SpaceToken token;
        try
        {
            token = SpaceToken.Parse(jwt, expected, new FixedTime(now));
        }
        catch (SpaceTokenException e)
        {
            return new(false, e.Error.ToString());
        }
        // The reference's order after parsing: audience, subject, then the signature.
        if (audience is not null && token.Audience != audience) return new(false, nameof(SpaceTokenError.BadJwtAudience));
        if (subject is not null && token.Subject != subject) return new(false, nameof(SpaceTokenError.BadJwtSub));
        if (!DidKey.TryParse(signingKey, out var key) || !token.VerifySignature(key.ToVerificationKey())) return new(false, nameof(SpaceTokenError.BadJwtSignature));
        return new(true, Issuer: token.Issuer, Audience: token.Audience, KeyId: token.ConfirmationKey?.Value, TokenId: token.Jti, ExpiresAt: token.ExpiresAt.ToUnixTimeSeconds());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static VerificationKey? P256(string didKey) =>
        DidKey.TryParse(didKey, out var key) && key.Curve == KeyCurve.P256 ? key.ToVerificationKey() : null;

    private static IEnumerable<KeyValuePair<string, string>> Fields(params (string Name, string? Value)[] fields) =>
        [.. fields.Where(f => f.Value is not null).Select(f => new KeyValuePair<string, string>(f.Name, f.Value!))];
}
