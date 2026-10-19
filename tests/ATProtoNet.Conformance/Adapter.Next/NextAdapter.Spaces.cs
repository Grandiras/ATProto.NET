using ATProtoNet.Crypto;
using ATProtoNet.Crypto.HttpSignatures;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    // The space request signature of spaces.md (SPC-22…24), over the public HTTP signature API: label
    // atproto-space, a P-256 key, and exactly ("authorization") on the credential exchange (the key named by
    // keyid) or ("authorization" "atproto-space-audience") on a credentialed request.
    private const string SpaceLabel = "atproto-space";
    private static readonly string[] ExchangeComponents = ["authorization"];
    private static readonly string[] RequestComponents = ["authorization", "atproto-space-audience"];

    private static readonly Dictionary<string, string> SpacesKnownFailures = new()
    {
        ["spaces/lthash.json#*"] = "LtHash is built in phase 2 (design/05-rewrite-plan.md).",
        ["spaces/space-tokens.json#*"] = "Space tokens are built in phase 6 (design/05-rewrite-plan.md).",
    };

    /// <inheritdoc />
    public byte[] Blake3(byte[] input, int outputLength)
    {
        var output = new byte[outputLength];
        global::ATProtoNet.Crypto.Blake3.Hash(input, output);
        return output;
    }

    /// <inheritdoc />
    public (byte[] State, byte[] Digest) LtHash(IReadOnlyList<(bool Add, string Element)> operations) => throw Later(2);

    /// <inheritdoc />
    public string? VerifySpaceSignature(string authorization, string? audience, string signatureInput, string signature, string? keyId)
    {
        var request = new HttpSignatureRequest(Fields(authorization, audience, ("Signature-Input", signatureInput), ("Signature", signature)));
        var exchange = audience is null || keyId is null;
        var components = exchange ? ExchangeComponents : RequestComponents;
        var requirements = new HttpSignatureRequirements { Label = SpaceLabel, Components = components };
        var verdict = exchange
            ? HttpMessageSignatureVerifier.Verify(request, requirements, id => id is null ? null : P256(id))
            : P256(keyId!) is { } key ? HttpMessageSignatureVerifier.Verify(request, requirements, key) : null;
        return verdict is HttpSignatureVerdict.Accepted accepted && accepted.Parameters.Components.SequenceEqual(components)
            ? accepted.Key.DidKey.Value
            : null;
    }

    /// <inheritdoc />
    public (string SignatureInput, string Signature)? SignSpaceRequest(string curve, string privateKeyHex, string authorization, string? audience)
    {
        using var key = SigningKey.ImportRawPrivateScalar(Convert.FromHexString(privateKeyHex), curve == "P-256" ? KeyCurve.P256 : KeyCurve.K256);
        if (key.Curve != KeyCurve.P256) return null;
        var fields = HttpMessageSigner.Sign(new HttpSignatureRequest(Fields(authorization, audience)), new HttpSignatureParameters
        {
            Label = SpaceLabel,
            Components = audience is null ? ExchangeComponents : RequestComponents,
            KeyId = audience is null ? key.PublicKey.DidKey.Value : null,
        }, key);
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
    public TokenVerdict VerifySpaceToken(string type, string jwt, string signingKey, string? audience, string? subject, DateTimeOffset now) => throw Later(6);

    private static VerificationKey? P256(string didKey) =>
        DidKey.TryParse(didKey, out var key) && key.Curve == KeyCurve.P256 ? key.ToVerificationKey() : null;

    private static IEnumerable<KeyValuePair<string, string>> Fields(string authorization, string? audience, params (string Name, string Value)[] more) =>
        [new("Authorization", authorization), .. audience is null ? [] : new KeyValuePair<string, string>[] { new("Atproto-Space-Audience", audience) },
            .. more.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))];
}
