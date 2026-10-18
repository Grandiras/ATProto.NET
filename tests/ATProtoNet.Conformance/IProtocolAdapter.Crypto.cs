namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Whether <paramref name="signature"/> (64-byte low-S <c>r‖s</c>) is a valid ECDSA signature
    /// over SHA-256 of <paramref name="message"/> by the <c>did:key</c>. Never throws for a
    /// malformed signature.
    /// </summary>
    bool VerifySignature(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature);

    /// <summary>The <c>did:key</c> of the public key of a raw 32-byte private scalar.</summary>
    string DidKeyForPrivateKey(SigningCurve curve, ReadOnlySpan<byte> privateKey);
}

/// <summary>The curves atproto signs with.</summary>
public enum SigningCurve
{
    /// <summary>NIST P-256 (<c>ES256</c>).</summary>
    P256,
    /// <summary>secp256k1 (<c>ES256K</c>).</summary>
    K256,
}
