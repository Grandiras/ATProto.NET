namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Builds the genesis <c>plc_operation</c> of an account (the rotation keys in order, the verification
    /// method <c>atproto</c>, the handle as <c>at://</c>, the PDS as <c>atproto_pds</c>) and signs it with the
    /// raw private scalar of its first rotation key; returns the signed operation as the JSON the directory accepts.
    /// </summary>
    string SignPlcGenesis(IReadOnlyList<string> rotationKeys, string signingKey, string handle, string pdsEndpoint, SigningCurve curve, ReadOnlySpan<byte> rotationPrivateKey);

    /// <summary>Reads a signed PLC operation in the directory's JSON and checks its signature against a rotation <c>did:key</c>.</summary>
    PlcOperationFacts ReadPlcOperation(string json, string rotationKey);
}

/// <summary>What a signed PLC operation encodes and derives.</summary>
/// <param name="UnsignedDrisl">The canonical DRISL of the operation without <c>sig</c>: what the signature covers.</param>
/// <param name="Cid">The CID of the signed operation.</param>
/// <param name="Did">The DID a genesis operation creates; <see langword="null"/> for any other operation.</param>
/// <param name="SignatureValid">Whether the signature verifies against the rotation key.</param>
public sealed record PlcOperationFacts(byte[] UnsignedDrisl, string Cid, string? Did, bool SignatureValid);
