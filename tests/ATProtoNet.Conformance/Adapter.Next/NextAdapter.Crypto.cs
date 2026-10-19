using ATProtoNet.Crypto;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> CryptoKnownFailures = new()
    {
    };

    /// <inheritdoc />
    public bool VerifySignature(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature) =>
        DidKey.TryParse(didKey, out var key) && key.ToVerificationKey().Verify(message, signature);

    /// <inheritdoc />
    public string DidKeyForPrivateKey(SigningCurve curve, ReadOnlySpan<byte> privateKey)
    {
        using var key = SigningKey.ImportRawPrivateScalar(privateKey, curve is SigningCurve.P256 ? KeyCurve.P256 : KeyCurve.K256);
        return key.PublicKey.DidKey.Value;
    }
}
