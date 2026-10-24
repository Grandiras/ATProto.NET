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
    /// Verifies a credentialed space request's HTTP message signature, made with the credential's
    /// bound key <paramref name="keyId"/> for <paramref name="audience"/>. A <see langword="null"/>
    /// field is absent from the request.
    /// </summary>
    /// <returns>The signing key's <c>did:key</c>, or <see langword="null"/> when the signature is refused.</returns>
    string? VerifySpaceSignature(string? authorization, string? audience, string? signatureInput, string? signature, string keyId);

    /// <summary>
    /// Signs a credentialed space request for <paramref name="audience"/> with the given key
    /// (<paramref name="curve"/>, private scalar in hex), returning the <c>Signature-Input</c> and
    /// <c>Signature</c> field values, or <see langword="null"/> when refused.
    /// </summary>
    (string SignatureInput, string Signature)? SignSpaceRequest(string curve, string privateKeyHex, string authorization, string audience);

    /// <summary>
    /// The shared secret the author's KEM key (<paramref name="authorPrivateKeyHex"/>, a P-256
    /// scalar) derives from a requester's <paramref name="keyShare"/>, or <see langword="null"/>
    /// when the key share is refused.
    /// </summary>
    byte[]? KemDecapsulate(string authorPrivateKeyHex, string keyShare);

    /// <summary>
    /// Encapsulates a fresh secret to the author's KEM key (a <c>did:key</c>), returning the key
    /// share and the secret, or <see langword="null"/> when the key is refused.
    /// </summary>
    (string KeyShare, byte[] Secret)? KemEncapsulate(string authorKey);

    /// <summary>
    /// The MAC of a space MAC context: <paramref name="type"/> (<c>atproto-space-repo</c> or
    /// <c>atproto-space-record</c>), its length-prefixed <paramref name="fields"/>, then
    /// <paramref name="data"/>, or <see langword="null"/> when a field does not fit its prefix.
    /// </summary>
    byte[]? SpaceMac(byte[] sharedSecret, string type, IReadOnlyList<string> fields, byte[] data);

    /// <summary>The <c>Atproto-Space-Want-Mac</c> value for a key share.</summary>
    string FormatWantMac(string type, string keyShare);

    /// <summary>The <c>Atproto-Space-Mac</c> value for a MAC.</summary>
    string FormatMac(string type, byte[] mac);

    /// <summary>The key share of an <c>Atproto-Space-Want-Mac</c> value of <paramref name="type"/>, or <see langword="null"/> when refused.</summary>
    string? ParseWantMac(string? value, string type);

    /// <summary>The MAC of an <c>Atproto-Space-Mac</c> value of <paramref name="type"/>, or <see langword="null"/> when refused.</summary>
    byte[]? ParseMac(string? value, string type);

    /// <summary>
    /// Verifies a space repository export of <paramref name="repo"/> in <paramref name="space"/>
    /// against the response's <paramref name="mac"/> under <paramref name="sharedSecret"/>.
    /// </summary>
    /// <returns>The commit and the records, or <see langword="null"/> when the export is refused.</returns>
    SpaceRepoVerdict? VerifySpaceRepo(byte[] archive, string repo, string space, byte[] sharedSecret, byte[] mac);

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

/// <summary>A verified space repository export: its commit and its records' paths and CIDs, in order.</summary>
public sealed record SpaceRepoVerdict(long Ver, string Repo, string Space, string Rev, byte[] Data, IReadOnlyList<(string Path, string Cid)> Records);

/// <summary>A dictionary member: its canonical serialization (<see langword="null"/> when absent) and whether its key was repeated.</summary>
public sealed record StructuredMember(string? Member, bool Duplicated);
