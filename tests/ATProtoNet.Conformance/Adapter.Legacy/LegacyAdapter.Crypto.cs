using System.Security.Cryptography;
using ATProtoNet.Crypto;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public bool VerifySignature(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature) =>
        AtProtoCrypto.VerifySignature(didKey, message, signature);

    /// <inheritdoc />
    public string DidKeyForPrivateKey(SigningCurve curve, ReadOnlySpan<byte> privateKey)
    {
        var ecCurve = curve is SigningCurve.P256 ? ECCurve.NamedCurves.nistP256 : ECCurve.CreateFromValue("1.3.132.0.10");
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ecCurve, D = privateKey.ToArray() });
        using var key = AtProtoCrypto.ImportPrivateKey(ecdsa.ExportPkcs8PrivateKey(), curve is SigningCurve.P256 ? KeyCurve.P256 : KeyCurve.K256);
        return key.ToDidKey();
    }
}
