using System.Buffers;
using System.Formats.Cbor;
using System.Text.Json;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// The framing every AT Protocol event stream shares (firehose, labels, chat moderation): a
/// DAG-CBOR header <c>{op, t}</c> followed by a DAG-CBOR body. <c>op = 1</c> is a message whose
/// type <c>t</c> names a variant of the subscription's union (<c>#commit</c>); <c>op = -1</c> is
/// an error, whose body is <c>{error, message}</c>, after which the server closes the stream.
/// </summary>
internal static class EventStreamFrame
{
    /// <summary>A regular message.</summary>
    public const int MessageOp = 1;

    /// <summary>An error; the server closes the stream after it.</summary>
    public const int ErrorOp = -1;

    /// <summary>
    /// Reads a frame's header.
    /// </summary>
    /// <param name="frame">The whole frame.</param>
    /// <param name="op">The header's <c>op</c>.</param>
    /// <param name="type">The header's <c>t</c>, when present.</param>
    /// <param name="bodyOffset">Where the body starts.</param>
    /// <returns>Whether the header could be read.</returns>
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
        catch (Exception ex) when (IsMalformed(ex))
        {
            return false;
        }
    }

    /// <summary>Reads the body of an <c>op = -1</c> frame, or null when it names no error.</summary>
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
        catch (Exception ex) when (IsMalformed(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads only the body's top-level <c>seq</c>, skipping everything else: the cheap way to keep
    /// the cursor moving past a frame that is not parsed in full.
    /// </summary>
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
        catch (Exception ex) when (IsMalformed(ex))
        {
        }

        return null;
    }

    /// <summary>
    /// Transcodes a body to JSON and deserializes it as <typeparamref name="T"/>, or returns null
    /// when it does not bind: a missing required field, or an identifier that does not parse.
    /// </summary>
    /// <param name="body">The frame body.</param>
    /// <param name="discriminator">A <c>$type</c> to write ahead of the body, for a union base.</param>
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
        catch (Exception ex) when (IsMalformed(ex) || ex is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// What <see cref="CborReader"/> throws for input that is not well-formed, or not the shape
    /// being read: never a bug in the caller.
    /// </summary>
    public static bool IsMalformed(Exception ex) =>
        ex is CborContentException or InvalidOperationException or FormatException or OverflowException
            or ArgumentException;
}
