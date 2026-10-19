using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public JsonObject ParseFirehoseFrame(byte[] frame) => throw Later(7);

    /// <inheritdoc />
    public SyncVerdict ApplySyncEvent(byte[] frame, SyncState? state, string? signingKey, DateTimeOffset now) => throw Later(7);
}
