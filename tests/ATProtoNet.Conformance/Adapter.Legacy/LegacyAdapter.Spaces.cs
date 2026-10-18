using System.Runtime.CompilerServices;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    private const string Fields = "ATProtoNet.Spaces.StructuredFields, ATProtoNet";

    /// <inheritdoc />
    public byte[] Blake3(byte[] input, int outputLength)
    {
        var output = new byte[outputLength];
        HashExtended(null, input, output);
        return output;
    }

    /// <inheritdoc />
    public (byte[] State, byte[] Digest) LtHash(IReadOnlyList<(bool Add, string Element)> operations)
    {
        var hash = new LtHash();
        foreach (var (add, element) in operations)
            _ = add ? hash.Add(element) : hash.Remove(element);
        return (hash.GetState(), hash.Digest());
    }

    /// <inheritdoc />
    public string? VerifySpaceSignature(string authorization, string? audience, string signatureInput, string signature, string? keyId)
    {
        try
        {
            return audience is null || keyId is null
                ? SpaceHttpSignature.VerifyExchange(authorization, signatureInput, signature)
                : SpaceHttpSignature.VerifyRequest(authorization, Did.Parse(audience), signatureInput, signature, keyId);
        }
        catch (SpaceSignatureException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public (string SignatureInput, string Signature)? SignSpaceRequest(string curve, string privateKeyHex, string authorization, string? audience)
    {
        using var key = ImportKey(curve, privateKeyHex);
        try
        {
            var signed = audience is null
                ? SpaceHttpSignature.SignExchange(key, authorization)
                : SpaceHttpSignature.SignRequest(key, authorization, Did.Parse(audience));
            return (signed.SignatureInput, signed.Signature);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public StructuredMember? ReadStructuredField(string field, string key)
    {
        object dictionary;
        try
        {
            dictionary = ParseDictionary(null, field);
        }
        catch (FormatException)
        {
            return null;
        }

        var member = Get(dictionary, key) switch
        {
            null => null,
            var value when value.GetType().Name == "InnerList" => SerializeInnerList(null, value),
            var value => SerializeItem(null, value),
        };
        return new(member, Duplicated(dictionary, key));
    }

    /// <inheritdoc />
    public TokenVerdict VerifySpaceToken(string type, string jwt, string signingKey, string? audience, string? subject, DateTimeOffset now)
    {
        var tokenType = type switch
        {
            "delegation" => SpaceTokenType.Delegation,
            "credential" => SpaceTokenType.Credential,
            _ => SpaceTokenType.ClientAttestation,
        };
        try
        {
            SpaceTokens.Verify(SpaceTokens.Parse(tokenType, jwt), signingKey, audience, subject is null ? null : SpaceUri.Parse(subject), now);
            return new(true);
        }
        catch (SpaceTokenException)
        {
            return new(false);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "HashExtended")]
    private static extern void HashExtended([UnsafeAccessorType("ATProtoNet.Crypto.Blake3, ATProtoNet")] object? _, ReadOnlySpan<byte> input, Span<byte> output);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseDictionary")]
    [return: UnsafeAccessorType("ATProtoNet.Spaces.StructuredFields+Dictionary, ATProtoNet")]
    private static extern object ParseDictionary([UnsafeAccessorType(Fields)] object? _, string input);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Get")]
    private static extern object? Get([UnsafeAccessorType("ATProtoNet.Spaces.StructuredFields+Dictionary, ATProtoNet")] object dictionary, string key);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Duplicated")]
    private static extern bool Duplicated([UnsafeAccessorType("ATProtoNet.Spaces.StructuredFields+Dictionary, ATProtoNet")] object dictionary, string key);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Serialize")]
    private static extern string SerializeInnerList([UnsafeAccessorType(Fields)] object? _, [UnsafeAccessorType("ATProtoNet.Spaces.StructuredFields+InnerList, ATProtoNet")] object list);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Serialize")]
    private static extern string SerializeItem([UnsafeAccessorType(Fields)] object? _, [UnsafeAccessorType("ATProtoNet.Spaces.StructuredFields+Item, ATProtoNet")] object item);
}
