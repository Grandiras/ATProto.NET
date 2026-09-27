using System.Buffers;
using System.Formats.Cbor;
using System.Text.Json;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

// The framing every AT Protocol event stream shares (firehose, labels, chat moderation): a DAG-CBOR
// header {op, t} followed by a DAG-CBOR body. op = 1 is a message whose type t names a variant of the
// subscription's union (#commit); op = -1 is an error, whose body is {error, message}, after which the
// server closes the stream.
internal static class EventStreamFrame
{
    // A regular message.
    public const int MessageOp = 1;

    // An error; the server closes the stream after it.
    public const int ErrorOp = -1;

    // Reads a frame's header.
    //
    // frame: The whole frame.
    //
    // op: The header's op.
    //
    // type: The header's t, when present.
    //
    // bodyOffset: Where the body starts.
    //
    // Returns: Whether the header could be read.
    public static bool TryReadHeader(ReadOnlyMemory<byte> frame, out int op, out string? type, out int bodyOffset)
    {
        op = 0;
        type = null;
        bodyOffset = 0;

        try
        {
            var reader = new CborReader(frame, CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                switch (reader.ReadTextString())
                {
                    case "op":
                        op = reader.ReadInt32();
                        break;
                    case "t":
                        type = reader.ReadTextString();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            bodyOffset = frame.Length - reader.BytesRemaining;
            return bodyOffset < frame.Length;
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
        {
            return false;
        }
    }

    // Reads the body of an op = -1 frame, or null when it names no error.
    public static EventStreamError? ReadError(ReadOnlyMemory<byte> body)
    {
        try
        {
            var reader = new CborReader(body, CborConformanceMode.Lax);
            string? error = null, message = null;
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (key is "error" or "message" && reader.PeekState() == CborReaderState.TextString)
                {
                    var value = reader.ReadTextString();
                    if (key == "error")
                        error = value;
                    else
                        message = value;
                }
                else
                {
                    reader.SkipValue();
                }
            }

            return string.IsNullOrEmpty(error) ? null : new EventStreamError(error, message);
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
        {
            return null;
        }
    }

    // Reads only the body's top-level seq, skipping everything else: the cheap way to keep the cursor
    // moving past a frame that is not parsed in full.
    public static long? ReadSeq(ReadOnlyMemory<byte> body)
    {
        try
        {
            var reader = new CborReader(body, CborConformanceMode.Lax);
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.ReadDefiniteLengthTextStringBytes().Span.SequenceEqual("seq"u8)
                    && reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
                {
                    return reader.ReadInt64();
                }

                reader.SkipValue();
            }
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
        {
        }

        return null;
    }

    // Transcodes a body to JSON and deserializes it as T, or returns null when it does not bind: a missing
    // required field, or an identifier that does not parse.
    //
    // body: The frame body.
    //
    // discriminator: A $type to write ahead of the body, for a union base.
    public static T? Deserialize<T>(ReadOnlyMemory<byte> body, string? discriminator = null) where T : class
    {
        try
        {
            var reader = new CborReader(body, CborConformanceMode.Lax);
            if (reader.PeekState() != CborReaderState.StartMap)
                return null;

            // JSON is bulkier than the CBOR it came from, mostly the base64 blow-up of a commit's
            // blocks, so start above the source size to avoid a regrow on the common frame.
            var buffer = new ArrayBufferWriter<byte>(Math.Max(256, body.Length * 2));

            // SkipValidation: the writer's structure comes from the transcoder, not from the frame.
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
            {
                writer.WriteStartObject();
                if (discriminator is not null)
                    writer.WriteString("$type", discriminator);

                // A body carrying its own $type would otherwise write the property twice.
                DagCborJson.WriteMapBody(reader, writer, DagCborJsonForm.Flattened, depth: 1, static key => key == "$type");
                writer.WriteEndObject();
            }

            return JsonSerializer.Deserialize<T>(buffer.WrittenSpan, AtProtoJsonDefaults.Options);
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex) || ex is JsonException)
        {
            return null;
        }
    }
}

// A handler of the AT Protocol event streams (firehose, labels, chat moderation), whose frames are an
// EventStreamFrame header and body.
internal abstract class CborEventStreamHandler<T>(StreamConsumerOptions options) : EventStreamHandler<T>(options)
    where T : class
{
    public sealed override async ValueTask<(T? Message, EventStreamError? Error)> ReadAsync(
        StreamSocketMessage message, CancellationToken cancellationToken)
    {
        var frame = message.Data;
        if (!EventStreamFrame.TryReadHeader(frame, out var op, out var type, out var bodyOffset))
        {
            Dropped(StreamDropReason.Malformed, null, "The frame header could not be read.");
            return default;
        }

        var body = frame[bodyOffset..];
        if (op == EventStreamFrame.ErrorOp)
            return (null, EventStreamFrame.ReadError(body) ?? new EventStreamError("Unknown", null));

        if (op != EventStreamFrame.MessageOp || string.IsNullOrEmpty(type))
        {
            Dropped(StreamDropReason.Malformed, EventStreamFrame.ReadSeq(body), $"Unexpected frame op {op}.");
            return default;
        }

        return (await HandleAsync(type, body, cancellationToken).ConfigureAwait(false), null);
    }

    // Turns an op = 1 frame into a message to deliver, or returns null to skip it, having recorded any
    // position the frame carried.
    //
    // type: The header's t.
    //
    // body: The frame body; valid only until this call returns.
    protected abstract ValueTask<T?> HandleAsync(string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    // The endpoint of the XRPC subscription nsid, resuming after cursor.
    protected static Uri Endpoint(string serviceUrl, string nsid, string? cursor) =>
        new($"{serviceUrl.TrimEnd('/')}/xrpc/{nsid}" + (cursor is null ? string.Empty : $"?cursor={Uri.EscapeDataString(cursor)}"));

    protected static Uri Endpoint(string serviceUrl, string nsid, long? cursor) =>
        Endpoint(serviceUrl, nsid, cursor?.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
