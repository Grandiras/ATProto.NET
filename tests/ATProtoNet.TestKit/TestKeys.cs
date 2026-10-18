namespace ATProtoNet.TestKit;

/// <summary>
/// Deterministic key pairs, taken from the atproto-interop-tests <c>crypto/w3c_didkey_*.json</c>
/// vectors, so a test's signatures and <c>did:key</c>s are the same on every run.
/// </summary>
public static class TestKeys
{
    /// <summary>A K-256 (secp256k1) private key, as 32 bytes in hex.</summary>
    public const string K256PrivateKeyHex = "9085d2bef69286a6cbb51623c8fa258629945cd55ca705cc4e66700396894e0c";

    /// <summary>The <c>did:key</c> of <see cref="K256PrivateKeyHex"/>.</summary>
    public const string K256DidKey = "did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme";

    /// <summary>The public multikey of <see cref="K256PrivateKeyHex"/>, as a DID document lists it.</summary>
    public const string K256Multibase = "zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme";

    /// <summary>A P-256 (secp256r1) private key, as 32 bytes in hex.</summary>
    public const string P256PrivateKeyHex = "82ebbd63ebbd9ff60141a69bd4c9be282f2415e8eafa9d42c0ed396daccca979";

    /// <summary>The <c>did:key</c> of <see cref="P256PrivateKeyHex"/>.</summary>
    public const string P256DidKey = "did:key:zDnaeTiq1PdzvZXUaMdezchcMJQpBdH2VN4pgrrEhMCCbmwSb";

    /// <summary>The public multikey of <see cref="P256PrivateKeyHex"/>, as a DID document lists it.</summary>
    public const string P256Multibase = "zDnaeTiq1PdzvZXUaMdezchcMJQpBdH2VN4pgrrEhMCCbmwSb";
}
