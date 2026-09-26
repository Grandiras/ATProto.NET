using System.Formats.Cbor;
using ATProtoNet.Lexicon.Com.AtProto.Label;

namespace ATProtoNet.Labeling;

/// <summary>
/// Encodes the frames of a labeler's <c>com.atproto.label.subscribeLabels</c> event stream, for a
/// service that emits one.
/// </summary>
/// <remarks>
/// <para>Each frame is one binary WebSocket message: a DRISL header (<c>{t, op}</c>) followed by
/// a DRISL body, as the <see href="https://atproto.com/specs/event-stream">event stream spec</see>
/// defines. <see cref="Streaming.LabelStreamConsumer"/> is the reading side.</para>
/// <para>Hosting the WebSocket, sequencing events and replaying from a cursor are the labeler's
/// own: send <see cref="Encode(LabelStreamMessage)"/>'s bytes as a binary message, and answer a
/// cursor ahead of the latest sequence number with
/// <see cref="EncodeError(string, string?)"/> (<c>FutureCursor</c>) before closing.</para>
/// </remarks>
public static class LabelStreamFrames
{
    /// <summary>
    /// Encodes a <c>#labels</c> or <c>#info</c> message as a frame.
    /// </summary>
    /// <param name="message">
    /// The message. Every label of a <see cref="LabelsEvent"/> must be signed: the spec requires
    /// <c>ver</c> and <c>sig</c> on labels a service hands to another. Sign them with
    /// <see cref="LabelSigner"/>.
    /// </param>
    /// <returns>The frame: header and body, ready to send as one binary WebSocket message.</returns>
    /// <exception cref="ArgumentException">
    /// A label is unsigned, carries no <c>ver</c>, or lacks a required field; or the message is
    /// of a type the stream does not carry, or an info message has no name.
    /// </exception>
    public static byte[] Encode(LabelStreamMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var writer = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        switch (message)
        {
            case LabelsEvent labels:
                WriteLabels(writer, labels);
                break;

            case LabelInfoEvent info:
                WriteInfo(writer, info);
                break;

            default:
                throw new ArgumentException(
                    $"{message.GetType().Name} is not a subscribeLabels message.", nameof(message));
        }

        return writer.Encode();
    }

    /// <summary>
    /// Encodes an error frame: the last frame before the server closes the stream.
    /// </summary>
    /// <param name="error">
    /// The error name, such as <see cref="Streaming.EventStreamErrors.FutureCursor"/>.
    /// </param>
    /// <param name="message">A human-readable description, or <see langword="null"/>.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="ArgumentException"><paramref name="error"/> is empty.</exception>
    public static byte[] EncodeError(string error, string? message = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(error);

        var writer = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);

        // An error header names no type.
        writer.WriteStartMap(1);
        writer.WriteTextString("op");
        writer.WriteInt32(-1);
        writer.WriteEndMap();

        writer.WriteStartMap(message is null ? 1 : 2);
        writer.WriteTextString("error");
        writer.WriteTextString(error);
        if (message is not null)
        {
            writer.WriteTextString("message");
            writer.WriteTextString(message);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteLabels(CborWriter writer, LabelsEvent labels)
    {
        if (labels.Labels is null)
            throw new ArgumentException("A #labels message needs its labels.", nameof(labels));

        foreach (var label in labels.Labels)
        {
            if (label is null)
                throw new ArgumentException("A #labels message holds a null label.", nameof(labels));

            if (label.Sig is not { Length: > 0 } || label.Version is null)
            {
                throw new ArgumentException(
                    $"The label '{label.Val}' on {label.Uri} is unsigned; sign it with a LabelSigner first.",
                    nameof(labels));
            }
        }

        WriteHeader(writer, "#labels");

        writer.WriteStartMap(2);
        writer.WriteTextString("seq");
        writer.WriteInt64(labels.Seq);
        writer.WriteTextString("labels");
        writer.WriteStartArray(labels.Labels.Count);
        foreach (var label in labels.Labels)
            LabelSigning.Write(writer, label, includeSignature: true);
        writer.WriteEndArray();
        writer.WriteEndMap();
    }

    private static void WriteInfo(CborWriter writer, LabelInfoEvent info)
    {
        if (string.IsNullOrEmpty(info.Name))
            throw new ArgumentException("An #info message needs a name.", nameof(info));

        WriteHeader(writer, "#info");

        writer.WriteStartMap(info.Message is null ? 1 : 2);
        writer.WriteTextString("name");
        writer.WriteTextString(info.Name);
        if (info.Message is not null)
        {
            writer.WriteTextString("message");
            writer.WriteTextString(info.Message);
        }

        writer.WriteEndMap();
    }

    private static void WriteHeader(CborWriter writer, string type)
    {
        // DRISL key order: "t" is shorter than "op".
        writer.WriteStartMap(2);
        writer.WriteTextString("t");
        writer.WriteTextString(type);
        writer.WriteTextString("op");
        writer.WriteInt32(1);
        writer.WriteEndMap();
    }
}
