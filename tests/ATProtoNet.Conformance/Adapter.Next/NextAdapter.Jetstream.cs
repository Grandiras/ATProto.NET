using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public JsonObject ParseJetstreamFrame(string json, int protocol) => throw Later(7);

    /// <inheritdoc />
    public JsonArray? DecodeJetstreamBlock(byte[] block) => throw Later(7);

    /// <inheritdoc />
    public JsonObject? ReadJetstreamSegmentHeader(byte[] header) => throw Later(7);
}
