using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public byte[] LabelSigningBytes(string labelJson) => LabelSigning.GetSigningBytes(ParseLabel(labelJson));

    /// <inheritdoc />
    public LabelStatus VerifyLabel(string labelJson, string didKey) => Status(LabelSigning.Verify(ParseLabel(labelJson), didKey));

    /// <inheritdoc />
    public LabelStatus VerifyLabelAgainstIssuer(string labelJson, string? documentJson, string? freshDocumentJson)
    {
        var resolver = new FakeDidResolver();
        if (documentJson is not null)
            resolver.Publish(documentJson, freshDocumentJson);
        return Status(new LabelVerifier(resolver).VerifyAsync(ParseLabel(labelJson)).GetAwaiter().GetResult().Status);
    }

    /// <inheritdoc />
    public string SignLabel(string labelJson, string curve, string privateKeyHex)
    {
        var label = ParseLabel(labelJson);
        using var key = ImportKey(curve, privateKeyHex);
        return JsonSerializer.Serialize(new LabelSigner(label.Src, key).Sign(label), AtProtoJsonDefaults.Options);
    }

    /// <inheritdoc />
    public byte[]? EncodeLabelFrame(string messageJson)
    {
        var message = JsonNode.Parse(messageJson)!;
        try
        {
            if (message["error"] is { } error)
                return LabelStreamFrames.EncodeError((string)error!, (string?)message["message"]);
            return LabelStreamFrames.Encode((string?)message["t"] switch
            {
                "#labels" => new LabelsEvent
                {
                    Seq = (long)message["seq"]!,
                    Labels = [.. message["labels"]!.AsArray().Select(l => ParseLabel(l!.ToJsonString()))],
                },
                "#info" => new LabelInfoEvent { Name = (string)message["name"]!, Message = (string?)message["message"] },
                var t => throw new NotSupportedException($"No {t} frames."),
            });
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Label ParseLabel(string json) => JsonSerializer.Deserialize<Label>(json, AtProtoJsonDefaults.Options)!;

    /// <summary>A key from its private scalar.</summary>
    internal static AtProtoKey ImportKey(string curve, string privateKeyHex)
    {
        var (ecCurve, keyCurve) = curve == "P-256"
            ? (ECCurve.NamedCurves.nistP256, KeyCurve.P256)
            : (ECCurve.CreateFromValue("1.3.132.0.10"), KeyCurve.K256);
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ecCurve, D = Convert.FromHexString(privateKeyHex) });
        return AtProtoCrypto.ImportPrivateKey(ecdsa.ExportPkcs8PrivateKey(), keyCurve);
    }

    private static LabelStatus Status(LabelVerificationStatus status) => status switch
    {
        LabelVerificationStatus.Valid => LabelStatus.Valid,
        LabelVerificationStatus.Unsigned => LabelStatus.Unsigned,
        LabelVerificationStatus.UnsupportedVersion => LabelStatus.UnsupportedVersion,
        LabelVerificationStatus.NoLabelKey => LabelStatus.NoLabelKey,
        LabelVerificationStatus.IssuerUnresolved => LabelStatus.IssuerUnresolved,
        LabelVerificationStatus.InvalidSignature => LabelStatus.InvalidSignature,
        _ => LabelStatus.Malformed,
    };
}
