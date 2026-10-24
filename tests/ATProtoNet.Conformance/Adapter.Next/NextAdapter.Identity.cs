using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Identity.Plc;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> IdentityKnownFailures = new()
    {
    };

    /// <inheritdoc />
    public string SignPlcGenesis(IReadOnlyList<string> rotationKeys, string signingKey, string handle, string pdsEndpoint, SigningCurve curve, ReadOnlySpan<byte> rotationPrivateKey)
    {
        using var key = SigningKey.ImportRawPrivateScalar(rotationPrivateKey, curve is SigningCurve.P256 ? KeyCurve.P256 : KeyCurve.K256);
        var genesis = new PlcUpdate
        {
            RotationKeys = [.. rotationKeys.Select(DidKey.Parse)],
            VerificationMethods = new Dictionary<string, DidKey> { ["atproto"] = DidKey.Parse(signingKey) },
            AlsoKnownAs = [$"at://{handle}"],
            Services = new Dictionary<string, PlcService> { ["atproto_pds"] = new("AtprotoPersonalDataServer", new Uri(pdsEndpoint)) },
        };
        return Encoding.UTF8.GetString(SignedPlcOperation.Sign(genesis, key).ToJson().Span);
    }

    /// <inheritdoc />
    public PlcOperationFacts ReadPlcOperation(string json, string rotationKey)
    {
        var operation = SignedPlcOperation.Parse(Encoding.UTF8.GetBytes(json));
        var unsigned = SignedDrisl.GetUnsignedBytes(DrislJson.ToDrisl(operation.ToJson().Span), out _);
        return new(unsigned, operation.Cid.Value, operation.Operation.Prev is null ? operation.DeriveDid().Value : null, operation.Verify(DidKey.Parse(rotationKey)));
    }
}
