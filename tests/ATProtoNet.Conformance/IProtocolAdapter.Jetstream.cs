using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Reads one Jetstream WebSocket message on wire protocol <paramref name="protocol"/> (1 or 2),
    /// never throwing.
    /// </summary>
    /// <returns>
    /// <c>kind</c> is <c>event</c>, <c>info</c>, <c>error</c> or <c>dropped</c>. An event has
    /// <c>type</c> (<c>commit</c>, <c>identity</c>, <c>account</c>, <c>sync</c>), <c>did</c>,
    /// <c>timeUs</c>, <c>cursor</c> and its fields: a commit <c>operation</c>, <c>collection</c>,
    /// <c>rkey</c>, <c>rev</c>, <c>cid</c>, <c>uri</c> and <c>record</c> (the JSON record); an
    /// identity <c>handle</c>, <c>seq</c>, <c>time</c>; an account <c>active</c>, <c>status</c>,
    /// <c>seq</c>; a sync <c>rev</c>, <c>seq</c> and <c>blocks</c> (padded base64). A field the
    /// frame carries but that does not parse is <see langword="null"/>. An info has <c>name</c>
    /// and <c>message</c>, an error <c>error</c> and <c>message</c>, a dropped frame
    /// <c>reason</c> (<c>malformed</c> or <c>unknownType</c>).
    /// </returns>
    JsonObject ParseJetstreamFrame(string json, int protocol);

    /// <summary>
    /// Decodes a decompressed <c>.jss</c> segment block into its rows, or returns
    /// <see langword="null"/> when the block is rejected.
    /// </summary>
    /// <returns>
    /// One object per row: <c>seq</c>, <c>witnessedAt</c>, <c>indexedAt</c>, <c>timeUs</c>,
    /// <c>kind</c> (<c>create</c>, <c>update</c>, <c>delete</c>, <c>identity</c>, <c>account</c>,
    /// <c>sync</c>, <c>createResync</c>), <c>did</c>, <c>collection</c>, <c>rkey</c>, <c>rev</c>,
    /// <c>payload</c> (hex) and <c>event</c>, the row as a live event in the shape of
    /// <see cref="ParseJetstreamFrame"/> (<see langword="null"/> when it projects to none).
    /// </returns>
    JsonArray? DecodeJetstreamBlock(byte[] block);

    /// <summary>
    /// Reads a segment's 256-byte fixed header, or returns <see langword="null"/> when it is
    /// rejected. Members: <c>checksum</c> (16 hex digits), <c>version</c>, <c>blockCount</c>,
    /// <c>eventCount</c>, <c>uniqueDidCount</c>, <c>minSeq</c>, <c>maxSeq</c>,
    /// <c>minWitnessedAt</c>, <c>maxWitnessedAt</c>, <c>footerOffset</c>,
    /// <c>blockIndexOffset</c>, <c>didBloomOffset</c>, <c>blockDidBloomOffset</c>,
    /// <c>collectionIndexOffset</c>.
    /// </summary>
    JsonObject? ReadJetstreamSegmentHeader(byte[] header);
}
