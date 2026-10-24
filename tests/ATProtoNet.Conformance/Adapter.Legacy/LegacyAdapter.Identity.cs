using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public string SignPlcGenesis(IReadOnlyList<string> rotationKeys, string signingKey, string handle, string pdsEndpoint, SigningCurve curve, ReadOnlySpan<byte> rotationPrivateKey)
    {
        var ecCurve = curve is SigningCurve.P256 ? ECCurve.NamedCurves.nistP256 : ECCurve.CreateFromValue("1.3.132.0.10");
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ecCurve, D = rotationPrivateKey.ToArray() });
        using var key = AtProtoCrypto.ImportPrivateKey(ecdsa.ExportPkcs8PrivateKey(), curve is SigningCurve.P256 ? KeyCurve.P256 : KeyCurve.K256);
        var genesis = PlcOperationBuilder.CreateGenesisOperation(rotationKeys, signingKey, Handle.Parse(handle), pdsEndpoint);
        return PlcOperationBuilder.Sign(genesis, key).ToJson();
    }

    /// <inheritdoc />
    public PlcOperationFacts ReadPlcOperation(string json, string rotationKey)
    {
        var operation = JsonNode.Parse(json)!.AsObject();
        var signed = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(operation));
        var signature = Base64Url.DecodeFromChars((string)operation["sig"]!);
        operation.Remove("sig");
        var unsigned = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(operation));
        var genesis = operation["prev"] is null;
        return new(unsigned, CidComputation.ComputeForDagCbor(signed).Value, genesis ? PlcOperationBuilder.DeriveDid(signed).Value : null,
            AtProtoCrypto.VerifySignature(rotationKey, unsigned, signature));
    }
}
