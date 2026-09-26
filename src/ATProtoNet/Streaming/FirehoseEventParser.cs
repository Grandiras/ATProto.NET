using ATProtoNet.Lexicon.Com.AtProto.Sync;

namespace ATProtoNet.Streaming;

/// <summary>
/// Parses raw firehose frames (<c>com.atproto.sync.subscribeRepos</c>) into typed
/// <see cref="FirehoseMessage"/> objects.
/// </summary>
/// <remarks>
/// A frame is two concatenated DAG-CBOR values: a header <c>{op, t}</c> naming the message type
/// (<c>#commit</c>, <c>#identity</c>, …) and the message body. <see cref="FirehoseClient"/> and
/// <see cref="TypedFirehoseConsumer"/> parse for you; use this for frames read some other way.
/// </remarks>
public static class FirehoseEventParser
{
    /// <summary>
    /// Parses one firehose frame.
    /// </summary>
    /// <param name="frame">The frame: a CBOR header followed by the CBOR body.</param>
    /// <returns>
    /// The message, or <see langword="null"/> for a frame that is malformed, has an identifier
    /// that does not parse, or is of a message type this SDK version does not model.
    /// </returns>
    /// <exception cref="EventStreamException">The frame is an error frame (<c>op = -1</c>), such as
    /// <c>FutureCursor</c>; <see cref="EventStreamException.Error"/> names it.</exception>
    public static FirehoseMessage? Parse(ReadOnlyMemory<byte> frame)
    {
        if (!EventStreamFrame.TryReadHeader(frame, out var op, out var type, out var bodyOffset))
            return null;

        var body = frame[bodyOffset..];
        if (op == EventStreamFrame.ErrorOp)
        {
            throw EventStreamException.FromErrorFrame(
                "firehose", EventStreamFrame.ReadError(body) ?? new EventStreamError("Unknown", null));
        }

        return op == EventStreamFrame.MessageOp && type is not null ? ParseBody(type, body) : null;
    }

    /// <summary>Whether this SDK version models the message type <paramref name="type"/>.</summary>
    internal static bool IsKnownType(string type) =>
        type is "#commit" or "#sync" or "#identity" or "#account" or "#info";

    /// <summary>
    /// Deserializes a message body of the given header type, or returns null when the type is not
    /// modelled or the body does not bind.
    /// </summary>
    internal static FirehoseMessage? ParseBody(string type, ReadOnlyMemory<byte> body) => type switch
    {
        "#commit" => EventStreamFrame.Deserialize<CommitEvent>(body),
        "#sync" => EventStreamFrame.Deserialize<SyncEvent>(body),
        "#identity" => EventStreamFrame.Deserialize<IdentityEvent>(body),
        "#account" => EventStreamFrame.Deserialize<AccountEvent>(body),
        "#info" => EventStreamFrame.Deserialize<InfoEvent>(body),
        _ => null,
    };
}
