using System.Text.Json;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public byte[]? EncodeDataModel(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return DagCborEncoder.Encode(document.RootElement);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? DecodeDataModel(ReadOnlySpan<byte> cbor, DrislMode mode)
    {
        if (mode is DrislMode.Strict) throw new NotSupportedException("0.8 has no strict DRISL reader for general values.");
        try
        {
            return DagCborDecoder.Decode(cbor.ToArray()).GetRawText();
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
