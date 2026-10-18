namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>BLAKE3 of <paramref name="input"/>, <paramref name="outputLength"/> bytes of extended output.</summary>
    byte[] Blake3(byte[] input, int outputLength);

    /// <summary>
    /// The LtHash state (2,048 bytes) and digest after applying <paramref name="operations"/>
    /// (add or remove an element) to the empty state, in order.
    /// </summary>
    (byte[] State, byte[] Digest) LtHash(IReadOnlyList<(bool Add, string Element)> operations);

    /// <summary>
    /// Verifies a space request's HTTP message signature: a credentialed request signed by
    /// <paramref name="keyId"/> for <paramref name="audience"/>, or, when both are
    /// <see langword="null"/>, a credential exchange signed by the key its <c>keyid</c> names.
    /// </summary>
    /// <returns>The signing key's <c>did:key</c>, or <see langword="null"/> when the signature is refused.</returns>
    string? VerifySpaceSignature(string authorization, string? audience, string signatureInput, string signature, string? keyId);

    /// <summary>
    /// Signs a space request (for <paramref name="audience"/>) or a credential exchange (without one)
    /// with the given key (<paramref name="curve"/>, private scalar in hex), returning the
    /// <c>Signature-Input</c> and <c>Signature</c> field values, or <see langword="null"/> when refused.
    /// </summary>
    (string SignatureInput, string Signature)? SignSpaceRequest(string curve, string privateKeyHex, string authorization, string? audience);

    /// <summary>
    /// Parses <paramref name="field"/> as an RFC 8941 dictionary and returns its member
    /// <paramref name="key"/> in canonical serialization, or <see langword="null"/> when the field
    /// does not parse.
    /// </summary>
    StructuredMember? ReadStructuredField(string field, string key);

    /// <summary>
    /// Parses and verifies a space token of <paramref name="type"/> (<c>delegation</c>,
    /// <c>credential</c>, <c>clientAttestation</c>) against <paramref name="signingKey"/>, the
    /// expected audience and subject (each <see langword="null"/>: not checked), at <paramref name="now"/>.
    /// </summary>
    TokenVerdict VerifySpaceToken(string type, string jwt, string signingKey, string? audience, string? subject, DateTimeOffset now);
}

/// <summary>A dictionary member: its canonical serialization (<see langword="null"/> when absent) and whether its key was repeated.</summary>
public sealed record StructuredMember(string? Member, bool Duplicated);
