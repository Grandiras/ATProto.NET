namespace ATProtoNet.Crypto;

/// <summary>The elliptic curves atproto signs with; the set is fixed by the protocol.</summary>
/// <remarks>See <see href="https://atproto.com/specs/cryptography"/>.</remarks>
public enum KeyCurve
{
    /// <summary>NIST P-256 (secp256r1), JWS algorithm <c>ES256</c>.</summary>
    P256,

    /// <summary>secp256k1, JWS algorithm <c>ES256K</c>.</summary>
    K256,
}

/// <summary>Which ECDSA signature forms a verification accepts.</summary>
/// <remarks>
/// atproto requires low-S signatures on content-addressed data (commits, labels, PLC operations), so a
/// signature has exactly one valid form. Bearer tokens and HTTP message signatures are not content-addressed,
/// and generic signers emit high-S about half the time. See <see href="https://atproto.com/specs/cryptography"/>.
/// </remarks>
public enum SignaturePolicy
{
    /// <summary>Only signatures with <c>s ≤ n/2</c> verify.</summary>
    LowSOnly,

    /// <summary>A high-S signature is normalised before it is verified; for JWS and HTTP message signatures only.</summary>
    AcceptHighS,
}
